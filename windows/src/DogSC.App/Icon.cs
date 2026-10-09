namespace DogSC;

public enum IconKind
{
    Monitor,
    Window,
    Region,
    Device,
    Mic,
    Camera,
    Speaker,
    Folder,
    Close,
    Menu,
    Play,
    Pause,
    Stop,
    Flag,
    Record,
    Grip,
    Undo,
    Redo,
    SkipStart,
    SkipEnd,
    Layout,
    Zoom,
    Sun,
    Moon,
    ChevronLeft,
    ChevronRight,
}

public sealed class Icon : FrameworkElement
{
    static readonly IReadOnlyDictionary<IconKind, Geometry> Geometries = Enum.GetValues<IconKind>()
        .Where(kind => kind != IconKind.Record)
        .ToDictionary(kind => kind, kind =>
        {
            var geometry = Geometry.Parse(PathFor(kind));
            geometry.Freeze();
            return geometry;
        });
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(IconKind),
        typeof(Icon),
        new FrameworkPropertyMetadata(IconKind.Monitor, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(
            typeof(Icon),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public Icon()
    {
        Width = 22;
        Height = 22;
        UseLayoutRounding = true;
        IsHitTestVisible = false;
    }

    public IconKind Kind
    {
        get => (IconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var brush = Foreground ?? Brushes.Black;
        if (Kind == IconKind.Record)
        {
            drawing.DrawEllipse(brush, null, new Point(ActualWidth / 2, ActualHeight / 2), ActualWidth * 0.34, ActualHeight * 0.34);
            return;
        }

        var pen = new Pen(brush, 1.6)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        drawing.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        var filled = Kind is IconKind.Play or IconKind.Pause or IconKind.Stop;
        var geometry = Geometries[Kind];
        var bounds = geometry.Bounds;
        // 按图形的实际边界居中，麦克风、扬声器等非对称路径不再偏向画布一侧。
        drawing.PushTransform(new TranslateTransform(12 - (bounds.Left + bounds.Width / 2) + (Kind == IconKind.Play ? 1 : 0), 12 - (bounds.Top + bounds.Height / 2)));
        drawing.DrawGeometry(filled ? brush : null, pen, geometry);
        drawing.Pop();
        drawing.Pop();
        drawing.Pop();
    }

    static string PathFor(IconKind kind) => kind switch
    {
        IconKind.Monitor => "M3,6 H21 V16 H3 Z M8,19 H16 M12,16 V19",
        IconKind.Window => "M4,4 H20 V20 H4 Z M4,9 H20",
        IconKind.Region => "M9,4 H4 V9 M15,4 H20 V9 M20,15 V20 H15 M4,15 V20 H9",
        IconKind.Device => "M3,6 H21 V18 H3 Z M7,10 H11 V14 H7 Z M15,10 H18 M15,14 H18",
        IconKind.Mic => "M9,6 C9,4.3 10.3,3 12,3 C13.7,3 15,4.3 15,6 V11 C15,12.7 13.7,14 12,14 C10.3,14 9,12.7 9,11 Z M8,11 C8,13.8 9.8,16 12,16 C14.2,16 16,13.8 16,11 M12,16 V19 M9,19 H15",
        IconKind.Camera => "M3,7 H15 V17 H3 Z M15,10 L21,7 V17 L15,14",
        IconKind.Speaker => "M4,9 H8 L13,5 V19 L8,15 H4 Z M16,9 C18,10 18,14 16,15",
        IconKind.Folder => "M3,7 V5 H9 L11,7 H21 V19 H3 Z",
        IconKind.Close => "M6,6 L18,18 M18,6 L6,18",
        IconKind.Menu => "M5,7 H19 M5,12 H19 M5,17 H19",
        IconKind.Play => "M8,5 L18,12 L8,19 Z",
        IconKind.Pause => "M7,5 H10 V19 H7 Z M14,5 H17 V19 H14 Z",
        IconKind.Stop => "M6,6 H18 V18 H6 Z",
        IconKind.Flag => "M6,4 V20 M6,5 H16 L13,9 L16,13 H6",
        IconKind.Grip => "M9,5 L9,5.1 M15,5 L15,5.1 M9,12 L9,12.1 M15,12 L15,12.1 M9,19 L9,19.1 M15,19 L15,19.1",
        IconKind.Undo => "M9,5 L4,10 L9,15 M4,10 H14 C21,10 21,20 14,20",
        IconKind.Redo => "M15,5 L20,10 L15,15 M20,10 H10 C3,10 3,20 10,20",
        IconKind.SkipStart => "M5,5 V19 M18,5 L8,12 L18,19 Z",
        IconKind.SkipEnd => "M19,5 V19 M6,5 L16,12 L6,19 Z",
        IconKind.Layout => "M3,5 H21 V19 H3 Z M8,5 V19 M8,10 H21",
        IconKind.Zoom => "M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 M15,15 L21,21 M7,10 H13 M10,7 V13",
        IconKind.Sun => "M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7 M12,2 V4 M12,20 V22 M2,12 H4 M20,12 H22 M5,5 L6.5,6.5 M17.5,17.5 L19,19 M5,19 L6.5,17.5 M17.5,6.5 L19,5",
        IconKind.Moon => "M19,15 A8,8 0 1 1 9,4 A7,7 0 0 0 19,15 Z",
        IconKind.ChevronLeft => "M15,5 L8,12 L15,19",
        IconKind.ChevronRight => "M9,5 L16,12 L9,19",
        _ => "M4,4 H20 V20 H4 Z",
    };
}
