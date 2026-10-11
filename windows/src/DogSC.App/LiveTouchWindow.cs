using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DogSC;

// The indicator is click-through, never activates, and is excluded from capture.
// Post-production click effects still come from the retained pointer track.
sealed class LiveTouchWindow : Window
{
    readonly RecordingSession _recording;
    readonly Ellipse _ring = new() { Stroke = new SolidColorBrush(Color.FromRgb(54, 137, 242)), StrokeThickness = 3 };
    readonly Ellipse _dot = new() { Fill = new SolidColorBrush(Color.FromArgb(120, 54, 137, 242)), Width = 16, Height = 16 };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1d / 60) };
    readonly Stopwatch _clock = new();
    IntPtr _handle;
    bool _closed;
    public LiveTouchWindow(RecordingSession recording)
    {
        _recording = recording;
        Width = Height = 80; Background = Brushes.Transparent; AllowsTransparency = true;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        ShowActivated = false; Topmost = true; IsHitTestVisible = false;
        var grid = new System.Windows.Controls.Grid(); grid.Children.Add(_ring); grid.Children.Add(_dot); Content = grid;
        _timer.Tick += (_, _) => UpdatePulse();
        _recording.PointerClicked += Clicked;
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(_handle, -20);
        SetWindowLong(_handle, -20, style | 0x20 | 0x08000000 | 0x80);
        Native.ExcludeFromCapture(_handle);
    }
    void Clicked(int x, int y)
    {
        Dispatcher.BeginInvoke(new Action(() => Pulse(x, y)));
    }
    internal void Pulse(int x, int y)
    {
        if (_closed || !_recording.IsRunning) return;
        if (!IsVisible) Show();
        var dpi = GetDpiForWindow(_handle) / 96d;
        var size = (int)Math.Round(80 * dpi);
        SetWindowPos(_handle, new IntPtr(-1), x - size / 2, y - size / 2, size, size, 0x10 | 0x40);
        _clock.Restart(); UpdatePulse(); _timer.Start();
    }
    void UpdatePulse()
    {
        var u = Math.Clamp(_clock.Elapsed.TotalSeconds / .36, 0, 1);
        _ring.Width = _ring.Height = 20 + 42 * (1 - Math.Pow(1 - u, 3));
        _ring.Opacity = 1 - u; _dot.Opacity = Math.Max(0, 1 - u * 3);
        if (u >= 1) { _timer.Stop(); Hide(); }
    }
    protected override void OnClosed(EventArgs e)
    {
        _closed = true; _recording.PointerClicked -= Clicked; _timer.Stop(); base.OnClosed(e);
    }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr handle, int index, int value);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
