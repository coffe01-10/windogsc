using System.Diagnostics;
using System.Runtime.InteropServices;
using DogSC.Core;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using WinRT;

namespace DogSC;

sealed class NativeWindowCapture : IAsyncDisposable
{
    readonly object _gate = new();
    readonly NativeCaptureDevice _device;
    readonly GraphicsCaptureItem _item;
    readonly Direct3D11CaptureFramePool _pool;
    readonly GraphicsCaptureSession _session;
    readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource _stop = new();
    readonly int _fps;
    SizeInt32 _poolSize;
    byte[]? _latest;
    long _lastCopy;
    bool _disposed;
    (int X, int Y, int Width, int Height) _content;
    Task? _writer;
    public int Width { get; }
    public int Height { get; }
    public Exception? Error { get; private set; }
    public bool TargetClosed { get; private set; }
    public (int X, int Y, int Width, int Height) Content { get { lock (_gate) return _content; } }

    public NativeWindowCapture(IntPtr hwnd, int fps)
    {
        _fps = fps;
        if (!GraphicsCaptureSession.IsSupported()) throw new InvalidOperationException(L.Text("此 Windows 环境不支持原生窗口录制。"));
        _item = CreateItem(hwnd);
        _device = new NativeCaptureDevice();
        try
        {
            _poolSize = _item.Size;
            Width = CanvasLayout.Even(_poolSize.Width); Height = CanvasLayout.Even(_poolSize.Height);
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device.Device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
            _session = _pool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = false;
            _pool.FrameArrived += FrameArrived; _item.Closed += ItemClosed;
            _session.StartCapture();
        }
        catch
        {
            _disposed = true;
            if (_pool is not null) _pool.FrameArrived -= FrameArrived;
            _item.Closed -= ItemClosed;
            _session?.Dispose(); _pool?.Dispose(); _device.Dispose(); _stop.Dispose();
            throw;
        }
    }

    static GraphicsCaptureItem CreateItem(IntPtr hwnd)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem", new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"));
        var iid = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
        Marshal.ThrowExceptionForHR(NativeCaptureDevice.Method<CreateForWindow>(factory.ThisPtr, 3)(factory.ThisPtr, hwnd, ref iid, out var item));
        try { return MarshalInspectable<GraphicsCaptureItem>.FromAbi(item); }
        finally { NativeCaptureDevice.Release(ref item); }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateForWindow(IntPtr self, IntPtr window, ref Guid iid, out IntPtr item);

    void ItemClosed(GraphicsCaptureItem sender, object args) => TargetClosed = true;

    void FrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                var size = _poolSize;
                using (var frame = sender.TryGetNextFrame())
                {
                    if (frame is null) return;
                    size = frame.ContentSize;
                    if (size.Width < 2 || size.Height < 2) return;
                    var now = Stopwatch.GetTimestamp();
                    if (_latest is not null && size.Width == _poolSize.Width && size.Height == _poolSize.Height
                        && (now - _lastCopy) / (double)Stopwatch.Frequency < .8 / _fps) return;
                    var image = _device.Read(frame.Surface, size.Width, size.Height);
                    (_latest, _content) = Fit(image.Pixels, image.Width, image.Height, Width, Height);
                    _lastCopy = now; _firstFrame.TrySetResult();
                }
                if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
                {
                    _poolSize = size;
                    sender.Recreate(_device.Device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
                }
            }
            catch (Exception ex) { Error = ex; _firstFrame.TrySetException(ex); }
        }
    }

    public async Task ReadyAsync() => await _firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));

    public void StartFrames(FfmpegProcess encoder)
    {
        _writer = Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew(); var frame = 0;
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    byte[]? pixels;
                    lock (_gate) pixels = _latest;
                    if (pixels is not null) { await encoder.WriteInputAsync(pixels, _stop.Token); frame++; }
                    var delay = frame / (double)_fps - clock.Elapsed.TotalSeconds;
                    if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception ex) { Error = ex; }
            finally { try { encoder.CompleteInput(); } catch (IOException) { } catch (ObjectDisposedException) { } }
        });
    }

    // A resize keeps a fixed recording canvas and fits the complete window
    // inside it; its content rectangle also maps recorded pointer positions.
    internal static (byte[] Pixels, (int X, int Y, int Width, int Height) Content) Fit(byte[] source, int sourceWidth, int sourceHeight, int width, int height)
    {
        if (sourceWidth == width && sourceHeight == height) return (source, (0, 0, width, height));
        var scale = Math.Min(width / (double)sourceWidth, height / (double)sourceHeight);
        var w = Math.Max(1, (int)Math.Round(sourceWidth * scale)); var h = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var left = (width - w) / 2; var top = (height - h) / 2;
        var output = new byte[width * height * 4];
        for (var i = 3; i < output.Length; i += 4) output[i] = 255;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Min(sourceWidth - 1, (int)((x + .5) / scale));
                var sy = Math.Min(sourceHeight - 1, (int)((y + .5) / scale));
                Buffer.BlockCopy(source, (sy * sourceWidth + sx) * 4, output, ((y + top) * width + x + left) * 4, 4);
            }
        return (output, (left, top, w, h));
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _pool.FrameArrived -= FrameArrived; _item.Closed -= ItemClosed;
        _session.Dispose(); _pool.Dispose();
        _stop.Cancel();
        if (_writer is not null) await _writer;
        _device.Dispose(); _stop.Dispose();
    }
}
