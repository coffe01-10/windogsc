using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DogSC;

static class WindowPlacement
{
    public static void Drag(Window window, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left || args.ButtonState != MouseButtonState.Pressed || args.Handled)
        {
            return;
        }
        for (var source = args.OriginalSource as DependencyObject; source is not null && source != window;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.RangeBase
                or System.Windows.Controls.Primitives.Selector)
            {
                return;
            }
        }
        args.Handled = true;
        try
        {
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标已松开时 DragMove 会抛出此异常。
        }
    }

    public static Rect WorkArea(Window window)
    {
        var work = SystemParameters.WorkArea;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, 2);
        if (GetMonitorInfo(monitor, ref info) && PresentationSource.FromVisual(window)?.CompositionTarget is { } target)
        {
            var topLeft = target.TransformFromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
            var bottomRight = target.TransformFromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
            work = new Rect(topLeft, bottomRight);
        }
        return work;
    }

    public static void KeepInWorkArea(Window window, bool center = false)
    {
        var work = WorkArea(window);
        var width = Math.Max(1, work.Width - 24);
        var height = Math.Max(1, work.Height - 24);
        window.MinWidth = Math.Min(window.MinWidth, width);
        window.MinHeight = Math.Min(window.MinHeight, height);
        window.Width = Math.Min(window.Width, width);
        window.Height = Math.Min(window.Height, height);
        window.Left = center ? work.Left + (work.Width - window.Width) / 2
            : Math.Clamp(window.Left, work.Left, work.Right - window.Width);
        window.Top = center ? work.Top + (work.Height - window.Height) / 2
            : Math.Clamp(window.Top, work.Top, work.Bottom - window.Height);
    }

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

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    struct RectData { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int Size;
        public RectData Monitor;
        public RectData Work;
        public uint Flags;
    }
}
