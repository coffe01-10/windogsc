using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DogSC;

// Installed on the recorder's UI thread, whose message loop receives low-level
// mouse notifications. This retains quick clicks between video-frame samples.
sealed class MouseClickHook : IDisposable
{
    readonly HookProc _callback;
    IntPtr _handle;
    public MouseClickHook(Action<int, int> clicked)
    {
        _callback = (code, message, data) =>
        {
            if (code >= 0 && (message == (IntPtr)0x0201 || message == (IntPtr)0x0204))
            {
                var point = Marshal.PtrToStructure<HookPoint>(data);
                clicked(point.X, point.Y);
            }
            return CallNextHookEx(_handle, code, message, data);
        };
        _handle = SetWindowsHookEx(14, _callback, GetModuleHandle(null), 0);
        if (_handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose()
    {
        if (_handle != IntPtr.Zero) { UnhookWindowsHookEx(_handle); _handle = IntPtr.Zero; }
    }
    [StructLayout(LayoutKind.Sequential)] struct HookPoint { public int X, Y; }
    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
