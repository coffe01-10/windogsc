using System.Windows.Media.Effects;

namespace DogSC;

// Shared drawing for EditorPositionPad / EditorTiltPad. The media projection
// is separate; this small plane is only the inspector's orientation cue.
sealed class EditorPadSurface : Canvas
{
    public static readonly DependencyProperty ChromeProperty = DependencyProperty.Register(nameof(Chrome), typeof(Brush), typeof(EditorPadSurface),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(EditorPadSurface),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender, (o, _) => ((EditorPadSurface)o).PaintHandle()));
    public Brush Chrome { get => (Brush)GetValue(ChromeProperty); set => SetValue(ChromeProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    readonly System.Windows.Shapes.Ellipse _halo, _dot;
    readonly bool _tilt;
    bool _coordinateEditing, _dragChanged;
    double _x = .5, _y = .5;
    public event Action? DragStarted;
    public event Action<double, double>? PointChanged;
    public event Action? DragFinished;
    public bool CoordinateEditing
    {
        get => _coordinateEditing;
        set { _coordinateEditing = value; IsHitTestVisible = !value; PaintHandle(); InvalidateVisual(); }
    }
    public bool Active => IsMouseCaptured || CoordinateEditing;
    public Point HandlePoint => new(12 + Math.Clamp(_x, 0, 1) * (Width - 24), 12 + Math.Clamp(_y, 0, 1) * (Height - 24));

    public EditorPadSurface(bool tilt = false, bool compact = false)
    {
        _tilt = tilt; Width = compact ? 158 : 184; Height = tilt ? 126 : compact ? 112 : 132;
        HorizontalAlignment = HorizontalAlignment.Center;
        Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), 12, 12);
        _halo = new System.Windows.Shapes.Ellipse { IsHitTestVisible = false };
        _dot = new System.Windows.Shapes.Ellipse { IsHitTestVisible = false, StrokeThickness = .75,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = .42, BlurRadius = 6, ShadowDepth = 1.5, Direction = 270 } };
        Children.Add(_halo); Children.Add(_dot);
        SetResourceReference(ChromeProperty, "PadChromeBrush"); SetResourceReference(AccentProperty, "AccentBrush");
        _dot.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "PadHandleOutlineBrush");
        MouseLeftButtonDown += (_, e) => { _dragChanged = false; CaptureMouse(); Move(e.GetPosition(this)); PaintHandle(); InvalidateVisual(); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured) Move(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, e) => { if (IsMouseCaptured) { ReleaseMouseCapture(); e.Handled = true; } };
        LostMouseCapture += (_, _) => { PaintHandle(); InvalidateVisual(); DragFinished?.Invoke(); };
        PaintHandle();
    }

    public void SetPoint(double x, double y) { _x = x; _y = y; PaintHandle(); InvalidateVisual(); }

    void Move(Point point)
    {
        var x = Math.Clamp((point.X - 12) / (Width - 24), 0, 1);
        var y = Math.Clamp((point.Y - 12) / (Height - 24), 0, 1);
        if (_tilt)
        {
            if (Math.Abs((x - .5) * 64) <= 1.4) x = .5;
            if (Math.Abs((.5 - y) * 56) <= 1.4) y = .5;
        }
        else { x = Snap(x); y = Snap(y); }
        if (x == _x && y == _y) return;
        // A click on the current snapped point must not add an empty undo.
        if (!_dragChanged) { _dragChanged = true; DragStarted?.Invoke(); }
        SetPoint(x, y); PointChanged?.Invoke(x, y);
    }

    static double Snap(double value)
    {
        foreach (var anchor in new[] { 0d, 1d / 3, .5, 2d / 3, 1d })
            if (Math.Abs(value - anchor) <= .035) return anchor;
        return value;
    }

    void PaintHandle()
    {
        var point = HandlePoint; var halo = Active ? 28d : 24d; var size = Active ? 16d : 14d;
        _halo.Width = _halo.Height = halo; _halo.Fill = Tone(Accent, Active ? .20 : .09);
        _dot.Width = _dot.Height = size; _dot.Fill = Accent;
        SetLeft(_halo, point.X - halo / 2); SetTop(_halo, point.Y - halo / 2);
        SetLeft(_dot, point.X - size / 2); SetTop(_dot, point.Y - size / 2);
    }

    static Brush Tone(Brush brush, double opacity) { var copy = brush.Clone(); copy.Opacity = opacity; copy.Freeze(); return copy; }
    static Brush Gradient(Brush first, double firstOpacity, Brush last, double lastOpacity, Point? end = null)
    {
        static Color ColorOf(Brush brush, double opacity) { var color = ((SolidColorBrush)brush).Color; color.A = (byte)Math.Round(255 * opacity); return color; }
        return new LinearGradientBrush(ColorOf(first, firstOpacity), ColorOf(last, lastOpacity), new Point(0, 0), end ?? new Point(0, 1));
    }
    static Pen Dashed(Brush brush, double a = 3, double b = 4) => new(brush, .75) { DashStyle = new DashStyle(new[] { a / .75, b / .75 }, 0) };

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, Width, Height);
        dc.DrawRoundedRectangle(_tilt ? Gradient(Brushes.Black, .38, Brushes.Black, .20) : Gradient(Chrome, .045, Chrome, .025),
            new Pen(Tone(Active ? Accent : Chrome, Active ? .22 : .075), .75), bounds, 12, 12);
        var x0 = 12d; var x1 = Width - 12; var y0 = 12d; var y1 = Height - 12;
        if (_tilt)
        {
            dc.DrawGeometry(Gradient(Accent, .11, Chrome, .025, new Point(1, 1)),
                new Pen(Gradient(Chrome, .34, Accent, .10), .8), TiltPlane());
        }
        else
        {
            var thirds = Dashed(Tone(Chrome, .065));
            foreach (var fraction in new[] { 1d / 3, 2d / 3 })
            {
                var x = x0 + (x1 - x0) * fraction; var y = y0 + (y1 - y0) * fraction;
                dc.DrawLine(thirds, new Point(x, y0), new Point(x, y1));
                dc.DrawLine(thirds, new Point(x0, y), new Point(x1, y));
            }
        }
        var center = Dashed(Tone(Accent, _tilt ? .13 : .14));
        dc.DrawLine(center, new Point(Width / 2, y0), new Point(Width / 2, y1));
        dc.DrawLine(center, new Point(x0, Height / 2), new Point(x1, Height / 2));
        if (Active)
        {
            var point = HandlePoint; var active = Dashed(Tone(Accent, _tilt ? .27 : .26), 2.5, 3.5);
            dc.DrawLine(active, new Point(point.X, y0), new Point(point.X, y1));
            dc.DrawLine(active, new Point(x0, point.Y), new Point(x1, point.Y));
        }
    }

    Geometry TiltPlane()
    {
        var w = Width - 48; var h = Height - 36; const double radius = 9;
        var ax = (.5 - Math.Clamp(_y, 0, 1)) * 56 * .55 * Math.PI / 180;
        var ay = (Math.Clamp(_x, 0, 1) - .5) * 64 * .55 * Math.PI / 180;
        Point Project(double x, double y)
        {
            x -= w / 2; y -= h / 2;
            var depth = Math.Max(w, h) / .72;
            var firstWeight = 1 - y * Math.Sin(ax) / depth;
            x /= firstWeight; y = y * Math.Cos(ax) / firstWeight;
            var secondWeight = 1 + x * Math.Sin(ay) / depth;
            return new Point(Width / 2 + x * Math.Cos(ay) / secondWeight, Height / 2 + y / secondWeight);
        }
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            var points = new List<Point>();
            foreach (var (cx, cy, start) in new[] { (w - radius, radius, -90d), (w - radius, h - radius, 0d), (radius, h - radius, 90d), (radius, radius, 180d) })
                for (var step = 0; step <= 8; step++)
                {
                    var angle = (start + step * 90d / 8) * Math.PI / 180;
                    points.Add(Project(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle)));
                }
            path.BeginFigure(points[0], true, true); path.PolyLineTo(points.Skip(1).ToArray(), true, false);
        }
        geometry.Freeze(); return geometry;
    }
}
