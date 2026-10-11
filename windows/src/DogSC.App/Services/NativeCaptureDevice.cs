using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace DogSC;

// D3D11 staging readback for Windows Graphics Capture. The COM slots below
// are the published ID3D11Device/DeviceContext/Texture2D ABI from d3d11.h.
sealed class NativeCaptureDevice : IDisposable
{
    IntPtr _device, _context, _staging;
    TextureDescription _stagingDescription;
    public IDirect3DDevice Device { get; }

    public NativeCaptureDevice()
    {
        var hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
            out _device, out _, out _context);
        if (hr < 0) hr = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
            out _device, out _, out _context); // WARP also supports machines without a usable GPU.
        Marshal.ThrowExceptionForHR(hr);
        var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        IntPtr dxgi = IntPtr.Zero, runtime = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(_device, ref iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out runtime));
            Device = MarshalInterface<IDirect3DDevice>.FromAbi(runtime);
        }
        catch { Release(ref _context); Release(ref _device); throw; }
        finally { Release(ref runtime); Release(ref dxgi); }
    }

    public (byte[] Pixels, int Width, int Height) Read(IDirect3DSurface surface, int width, int height)
    {
        using var access = ((IWinRTObject)surface).NativeObject.As<WinRT.Interop.IUnknownVftbl>(new Guid("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1"));
        var textureId = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        Marshal.ThrowExceptionForHR(Method<GetInterface>(access.ThisPtr, 3)(access.ThisPtr, ref textureId, out var texture));
        try
        {
            Method<GetDescription>(texture, 10)(texture, out var desc);
            if (_staging == IntPtr.Zero || _stagingDescription.Width != desc.Width || _stagingDescription.Height != desc.Height)
            {
                Release(ref _staging);
                desc.Usage = 3; desc.BindFlags = 0; desc.CpuAccessFlags = 0x20000; desc.MiscFlags = 0;
                Marshal.ThrowExceptionForHR(Method<CreateTexture>(_device, 5)(_device, ref desc, IntPtr.Zero, out _staging));
                _stagingDescription = desc;
            }
            Method<CopyResource>(_context, 47)(_context, _staging, texture);
            Marshal.ThrowExceptionForHR(Method<MapResource>(_context, 14)(_context, _staging, 0, 1, 0, out var mapped));
            try
            {
                width = Math.Min(width, (int)desc.Width); height = Math.Min(height, (int)desc.Height);
                var data = new byte[width * height * 4];
                for (var y = 0; y < height; y++) Marshal.Copy(mapped.Data + y * (int)mapped.RowPitch, data, y * width * 4, width * 4);
                return (data, width, height);
            }
            finally { Method<UnmapResource>(_context, 15)(_context, _staging, 0); }
        }
        finally { Release(ref texture); }
    }

    internal static T Method<T>(IntPtr obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));
    internal static void Release(ref IntPtr value) { if (value != IntPtr.Zero) { Marshal.Release(value); value = IntPtr.Zero; } }
    public void Dispose() { Device.Dispose(); Release(ref _staging); Release(ref _context); Release(ref _device); }

    [StructLayout(LayoutKind.Sequential)] struct TextureDescription
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }
    [StructLayout(LayoutKind.Sequential)] struct MappedResource { public IntPtr Data; public uint RowPitch, DepthPitch; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetInterface(IntPtr self, ref Guid iid, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void GetDescription(IntPtr self, out TextureDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateTexture(IntPtr self, ref TextureDescription description, IntPtr initialData, out IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CopyResource(IntPtr self, IntPtr destination, IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int MapResource(IntPtr self, IntPtr resource, uint subresource, uint mapType, uint flags, out MappedResource mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void UnmapResource(IntPtr self, IntPtr resource, uint subresource);
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureCount, uint sdkVersion, out IntPtr device, out uint featureLevel, out IntPtr context);
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
