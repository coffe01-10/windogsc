namespace DogSC;

public partial class RegionOverlay : Window
{
    readonly MonitorDesc _monitor;
    Point _start;
    bool _dragging;
    public CaptureTarget? Result { get; private set; }

    public RegionOverlay(MonitorDesc monitor)
    {
        _monitor = monitor;
        InitializeComponent();
    }

    public static CaptureTarget? Pick(MonitorDesc monitor)
    {
        var window = new RegionOverlay(monitor);
        window.ShowDialog();
        return window.Result;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowPlacement.Exclude(this);
        WindowPlacement.Fit(this, _monitor);
        Canvas.SetLeft(Hint, 24);
        Canvas.SetTop(Hint, 24);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Surface);
        _dragging = true;
        Selection.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var current = e.GetPosition(Surface);
        var rect = new Rect(_start, current);
        Canvas.SetLeft(Selection, rect.X);
        Canvas.SetTop(Selection, rect.Y);
        Selection.Width = rect.Width;
        Selection.Height = rect.Height;
        var physical = ToPhysical(rect);
        SizeLabel.Text = (int)physical.Width + " × " + (int)physical.Height;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        var rect = new Rect(_start, e.GetPosition(Surface));
        var physical = ToPhysical(rect);
        if (physical.Width < 64 || physical.Height < 64)
        {
            Selection.Visibility = Visibility.Collapsed;
            return;
        }

        var even = Native.EvenRegion((int)physical.X, (int)physical.Y, (int)physical.Width, (int)physical.Height);
        Result = new CaptureTarget(CaptureKind.Region, "自选区域", even.X, even.Y, even.Width, even.Height, null);
        DialogResult = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    Rect ToPhysical(Rect dip)
    {
        var left = Math.Min(dip.Left, dip.Right);
        var top = Math.Min(dip.Top, dip.Bottom);
        var right = Math.Max(dip.Left, dip.Right);
        var bottom = Math.Max(dip.Top, dip.Bottom);
        var a = Surface.PointToScreen(new Point(left, top));
        var b = Surface.PointToScreen(new Point(right, bottom));
        return new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }
}
