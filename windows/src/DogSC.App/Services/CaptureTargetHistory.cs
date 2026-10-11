using DogSC.Core;

namespace DogSC;

internal static class CaptureTargetHistory
{
    public static void Remember(AppSettings settings, CaptureTarget target)
    {
        var item = new RecentCaptureTarget { Kind = target.Kind.ToString(), Label = target.Label, DeviceName = target.DeviceName,
            X = target.X, Y = target.Y, Width = target.Width, Height = target.Height,
            ProcessName = target.Kind == CaptureKind.Window ? Native.Windows().FirstOrDefault(w => w.Handle == target.WindowHandle)?.ProcessName : null };
        settings.RecentCaptureTargets.RemoveAll(old => Same(old, item));
        settings.RecentCaptureTargets.Insert(0, item);
        settings.RecentCaptureTargets = settings.RecentCaptureTargets.Take(5).ToList();
    }
    static bool Same(RecentCaptureTarget a, RecentCaptureTarget b) => a.Kind == b.Kind && a.Kind switch
    {
        "Window" => a.Label == b.Label && a.ProcessName == b.ProcessName,
        "Device" => a.DeviceName == b.DeviceName,
        "Region" => (a.X, a.Y, a.Width, a.Height) == (b.X, b.Y, b.Width, b.Height),
        _ => a.Label == b.Label,
    };
    public static CaptureTarget? Resolve(RecentCaptureTarget recent, IReadOnlyList<string> devices)
    {
        if (recent.Kind == "Window")
        {
            var window = Native.Windows().FirstOrDefault(w => w.Title == recent.Label && w.ProcessName == recent.ProcessName);
            if (window is null) return null;
            return new(CaptureKind.Window, window.Title, window.Left, window.Top, window.Width, window.Height, null) { WindowHandle = window.Handle };
        }
        if (recent.Kind == "Device")
            return recent.DeviceName is { } name && devices.Contains(name) ? new(CaptureKind.Device, name, 0, 0, 0, 0, name) : null;
        if (recent.Kind == "Display")
        {
            var monitor = Native.Monitors().FirstOrDefault(m => m.Name == recent.Label);
            return monitor is null ? null : new(CaptureKind.Display, monitor.Name, monitor.Left, monitor.Top, monitor.Width, monitor.Height, null);
        }
        if (recent.Kind == "Region" && Native.Monitors().Any(m => m.Contains(recent.X, recent.Y)
            && m.Contains(recent.X + recent.Width - 1, recent.Y + recent.Height - 1)))
            return new(CaptureKind.Region, recent.Label, recent.X, recent.Y, recent.Width, recent.Height, null);
        return null;
    }
}
