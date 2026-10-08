using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DogSC;

static class WindowPlacement
{
    public static void Fit(Window window, MonitorDesc monitor)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowPos(hwnd, new IntPtr(-1), monitor.Left, monitor.Top, monitor.Width, monitor.Height, 0x0040);
        if (PresentationSource.FromVisual(window)?.CompositionTarget is { } target)
        {
            var origin = target.TransformFromDevice.Transform(new Point(monitor.Left, monitor.Top));
            var size = target.TransformFromDevice.Transform(new Vector(monitor.Width, monitor.Height));
            window.Left = origin.X;
            window.Top = origin.Y;
            window.Width = Math.Max(10, size.X);
            window.Height = Math.Max(10, size.Y);
        }
    }

    public static void Exclude(Window window) =>
        Native.ExcludeFromCapture(new WindowInteropHelper(window).Handle);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
