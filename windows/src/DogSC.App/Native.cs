using System.Runtime.InteropServices;
using System.Text;

namespace DogSC;

public sealed record MonitorDesc(string Name, int Left, int Top, int Width, int Height, bool Primary)
{
    public bool Contains(int x, int y) => x >= Left && y >= Top && x < Left + Width && y < Top + Height;
}

public sealed record WindowDesc(IntPtr Handle, string Title, string ProcessName, int Left, int Top, int Width, int Height);

public static class Native
{
    const uint WdaExcludeFromCapture = 0x11;
    const int DwmwaExtendedFrameBounds = 9;
    const int DwmwaCloaked = 14;
    const uint MonitorinfoPrimary = 1;

    public static bool ExcludeFromCapture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var applied = SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
        var error = Marshal.GetLastWin32Error();
        var excluded = applied && GetWindowDisplayAffinity(hwnd, out var affinity) && affinity == WdaExcludeFromCapture;
        if (!excluded) AppPaths.Log($"Capture exclusion failed: HWND={hwnd}, SetWindowDisplayAffinity={applied}, Win32={error}");
        return excluded;
    }

    public static IReadOnlyList<MonitorDesc> Monitors()
    {
        var list = new List<MonitorDesc>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RectData _, IntPtr _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            var width = info.Monitor.Right - info.Monitor.Left;
            var height = info.Monitor.Bottom - info.Monitor.Top;
            if (width < 64 || height < 64)
            {
                return true;
            }

            list.Add(new MonitorDesc(
                MonitorName(info.DeviceName, list.Count + 1),
                info.Monitor.Left,
                info.Monitor.Top,
                width,
                height,
                (info.Flags & MonitorinfoPrimary) != 0));
            return true;
        }, IntPtr.Zero);
        if (list.Count == 0)
        {
            list.Add(new MonitorDesc(L.Text("主显示器"), 0, 0, 1920, 1080, true));
        }

        return list.OrderByDescending(item => item.Primary).ThenBy(item => item.Left).ToList();
    }

    public static MonitorDesc MonitorAtCursor()
    {
        GetCursorPos(out var point);
        return Monitors().FirstOrDefault(item => item.Contains(point.X, point.Y))
            ?? Monitors().First(item => item.Primary || true);
    }

    /// <summary>当前光标的物理屏幕坐标。</summary>
    public static (int X, int Y) CursorPos()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    /// <summary>鼠标左键此刻是否按着（录制轮询用）。</summary>
    public static bool LeftButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    /// <summary>
    /// 当前系统光标对应的样式 id（system.*）。标准 IDC_* 光标句柄是系统共享的，
    /// 可直接与 LoadCursor 返回值比较；应用自定义光标返回 null。
    /// </summary>
    public static string? CursorAssetId()
    {
        var info = new CursorInfo { cbSize = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) || info.hCursor == IntPtr.Zero)
        {
            return null;
        }

        return info.hCursor switch
        {
            var h when h == LoadCursor(IntPtr.Zero, 32512) => "system.arrow",      // IDC_ARROW
            var h when h == LoadCursor(IntPtr.Zero, 32513) => "system.ibeam",      // IDC_IBEAM
            var h when h == LoadCursor(IntPtr.Zero, 32515) => "system.crosshair",  // IDC_CROSS
            var h when h == LoadCursor(IntPtr.Zero, 32649) => "system.pointing-hand", // IDC_HAND
            var h when h == LoadCursor(IntPtr.Zero, 32648) => "system.not-allowed",   // IDC_NO
            var h when h == LoadCursor(IntPtr.Zero, 32646) => "system.open-hand",     // IDC_SIZEALL
            var h when h == LoadCursor(IntPtr.Zero, 32651) => "system.open-hand",     // IDC_PERSON（近似）
            _ => null,
        };
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    static extern bool GetCursorInfo(ref CursorInfo pci);

    [StructLayout(LayoutKind.Sequential)]
    struct CursorInfo
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public PointData ptScreenPos;
    }

    public static IReadOnlyList<WindowDesc> Windows()
    {
        var list = new List<WindowDesc>();
        var self = Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
            {
                return true;
            }

            if (DwmGetInt(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out var pid);
            if ((int)pid == self)
            {
                return true;
            }

            var title = new StringBuilder(512);
            if (GetWindowText(hwnd, title, title.Capacity) <= 0)
            {
                return true;
            }

            var text = title.ToString().Trim();
            if (text.Length == 0)
            {
                return true;
            }

            var rect = new RectData();
            if (DwmGetRect(hwnd, DwmwaExtendedFrameBounds, out var bounds, Marshal.SizeOf<RectData>()) != 0)
            {
                GetWindowRect(hwnd, out bounds);
            }

            rect = bounds;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width < 80 || height < 80)
            {
                return true;
            }

            var process = L.Text("窗口");
            try
            {
                process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                // The window can close while the list is being built.
            }

            list.Add(new WindowDesc(hwnd, text, process, rect.Left, rect.Top, width, height));
            return true;
        }, IntPtr.Zero);
        return list
            .GroupBy(item => item.Handle)
            .Select(group => group.First())
            .OrderBy(item => item.ProcessName)
            .ThenBy(item => item.Title)
            .Take(40)
            .ToList();
    }

    /// <summary>
    /// 取窗口当前的物理矩形（优先 DWM 扩展边框，与列表时的口径一致）。
    /// 窗口被关闭、最小化或隐藏时返回 null。
    /// </summary>
    public static WindowDesc? WindowRect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return null;
        }

        var bounds = new RectData();
        if (DwmGetRect(hwnd, DwmwaExtendedFrameBounds, out bounds, Marshal.SizeOf<RectData>()) != 0)
        {
            if (!GetWindowRect(hwnd, out bounds))
            {
                return null;
            }
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width < 2 || height < 2)
        {
            return null;
        }

        return new WindowDesc(hwnd, "", "", bounds.Left, bounds.Top, width, height);
    }

    public static (int X, int Y, int Width, int Height) EvenRegion(int x, int y, int width, int height)
    {
        if (width < 0)
        {
            x += width;
            width = -width;
        }

        if (height < 0)
        {
            y += height;
            height = -height;
        }

        width = Math.Max(64, width) & ~1;
        height = Math.Max(64, height) & ~1;
        return (x, y, Math.Max(64, width), Math.Max(64, height));
    }

    static string MonitorName(string device, int index)
    {
        var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        if (EnumDisplayDevices(device, 0, ref display, 0) && !string.IsNullOrWhiteSpace(display.DeviceString))
        {
            return display.DeviceString.Trim();
        }

        return index == 1 ? L.Text("主显示器") : L.Text("显示器 ") + index;
    }

    delegate bool MonitorProc(IntPtr monitor, IntPtr hdc, ref RectData rect, IntPtr data);
    delegate bool WindowProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice display, uint flags);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(WindowProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RectData rect);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out PointData point);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    static extern int DwmGetInt(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    static extern int DwmGetRect(IntPtr hwnd, int attribute, out RectData rect, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct RectData
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PointData
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct MonitorInfo
    {
        public int Size;
        public RectData Monitor;
        public RectData Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
}
