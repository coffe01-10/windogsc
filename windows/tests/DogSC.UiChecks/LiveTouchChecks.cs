using DogSC;
using DogSC.Core;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class LiveTouchChecks
{
    [DllImport("user32.dll", EntryPoint = "SetPhysicalCursorPos")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);

    public static async Task RunAsync()
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
        var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg) ?? throw new InvalidOperationException("FFprobe unavailable");
        var root = Path.Combine(Path.GetTempPath(), "DogSC-live-touch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pointer = Native.CursorPos(); var foreground = GetForegroundWindow();
        var target = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true, Left = 80, Top = 80, Width = 360, Height = 240, Background = Brushes.Cyan };
        RecordingSession? session = null; LiveTouchWindow? touch = null;
        var pulse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) }; var pulses = 0;
        try
        {
            target.Show(); await Task.Delay(150);
            var handle = new WindowInteropHelper(target).Handle; var rect = Native.WindowRect(handle)!;
            var x = rect.Left + rect.Width / 2; var y = rect.Top + rect.Height / 2;
            session = new RecordingSession(ffmpeg, ffprobe, new(CaptureKind.Window, "Live touch test", rect.Left, rect.Top, rect.Width, rect.Height, null)
                { WindowHandle = handle }, new() { FrameRate = 30 }, root);
            await session.StartAsync(); touch = new(session);
            SetCursorPos(x, y); await Task.Delay(80);
            // Both pairs finish between timer samples. The native event observer
            // must retain them, and the indicator must not block the second click.
            mouse_event(0x02, 0, 0, 0, UIntPtr.Zero); mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(80);
            if (!touch.IsVisible || !Native.PointerOverWindow(handle, x, y))
                throw new InvalidOperationException("Live click indicator did not appear or intercepted pointer hit testing");
            mouse_event(0x08, 0, 0, 0, UIntPtr.Zero); mouse_event(0x10, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(80);
            // Prove the indicator actually draws blue while a real desktop
            // capture sees the unchanged cyan target underneath its footprint.
            var rendered = new RenderTargetBitmap(80, 80, 96, 96, PixelFormats.Pbgra32); rendered.Render(touch);
            var indicator = new byte[80 * 80 * 4]; rendered.CopyPixels(indicator, 80 * 4, 0);
            if (!Enumerable.Range(0, 80 * 80).Any(i => indicator[i * 4 + 3] > 50 && indicator[i * 4] > indicator[i * 4 + 1] + 30))
                throw new InvalidOperationException("Live indicator was visible without its blue pulse");
            pulse.Tick += (_, _) => { touch.Pulse(x, y); pulses++; }; pulse.Start();
            var desktop = Path.Combine(root, "desktop.rgb");
            try
            {
                using var capture = FfmpegProcess.Start(ffmpeg, ["-y", "-f", "gdigrab", "-draw_mouse", "0", "-framerate", "30",
                    "-offset_x", (x - 60).ToString(), "-offset_y", (y - 60).ToString(), "-video_size", "120x120", "-i", "desktop",
                    "-frames:v", "6", "-f", "rawvideo", "-pix_fmt", "rgb24", desktop]);
                if (await capture.WaitAsync() != 0) throw new InvalidOperationException(capture.ErrorText());
                var pixels = await File.ReadAllBytesAsync(desktop);
                if (pulses < 2 || pixels.Length != 6 * 120 * 120 * 3)
                    throw new InvalidOperationException("Live capture exclusion test did not overlap visible pulses");
                if (Enumerable.Range(0, pixels.Length / 3).Any(i => pixels[i * 3] > 20 || pixels[i * 3 + 1] < 235 || pixels[i * 3 + 2] < 235))
                    throw new InvalidOperationException("Live indicator leaked into the real desktop capture");
            }
            finally { pulse.Stop(); if (File.Exists(desktop)) File.Delete(desktop); }
            await Task.Delay(400);
            if (touch.IsVisible) throw new InvalidOperationException("Live pulse did not disappear");
            var project = await session.FinishAsync();
            if (project.Assets[0].Cursor.Count(p => p.Click) != 2)
                throw new InvalidOperationException("Quick left/right clicks were lost or recorded twice");
            touch.Close(); touch.Pulse(x, y); // Pending notifications after close must remain harmless.
            Console.WriteLine("LIVE_TOUCH_OK visible pulse, click-through, quick left/right clicks, real desktop capture exclusion, expiration and close");
        }
        finally
        {
            pulse.Stop(); touch?.Close(); if (session is not null) await session.DisposeAsync(); target.Close();
            SetCursorPos(pointer.X, pointer.Y); if (foreground != IntPtr.Zero) SetForegroundWindow(foreground);
            Directory.Delete(root, recursive: true);
        }
    }
}
