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
    ChevronUp,
    ChevronDown,
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

        var pen = new Pen(brush, 1.45)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        drawing.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        var filled = Kind is IconKind.Play or IconKind.Pause or IconKind.Stop or IconKind.SkipStart or IconKind.SkipEnd;
        var geometry = Geometries[Kind];
        var bounds = geometry.Bounds;
        // 按图形的实际边界居中，麦克风、扬声器等非对称路径不再偏向画布一侧。
        drawing.PushTransform(new TranslateTransform(12 - (bounds.Left + bounds.Width / 2) + (Kind == IconKind.Play ? 1 : 0), 12 - (bounds.Top + bounds.Height / 2)));
        drawing.DrawGeometry(filled ? brush : null, filled ? null : pen, geometry);
        drawing.Pop();
        drawing.Pop();
        drawing.Pop();
    }

    static string PathFor(IconKind kind) => kind switch
    {
        IconKind.Monitor => "M5,5 H19 Q21,5 21,7 V15 Q21,17 19,17 H5 Q3,17 3,15 V7 Q3,5 5,5 Z M8,20 H16 M12,17 V20",
        IconKind.Window => "M6,4 H18 Q20,4 20,6 V18 Q20,20 18,20 H6 Q4,20 4,18 V6 Q4,4 6,4 Z M4,9 H20 M7,6.5 H7.1 M10,6.5 H10.1",
        IconKind.Region => "M9,4 H4 V9 M15,4 H20 V9 M20,15 V20 H15 M4,15 V20 H9",
        IconKind.Device => "M5,5 H19 Q21,5 21,7 V17 Q21,19 19,19 H5 Q3,19 3,17 V7 Q3,5 5,5 Z M7,9 H11 V15 H7 Z M15,9 H18 M15,12 H18 M15,15 H17",
        IconKind.Mic => "M9,6 C9,4.3 10.3,3 12,3 C13.7,3 15,4.3 15,6 V11 C15,12.7 13.7,14 12,14 C10.3,14 9,12.7 9,11 Z M8,11 C8,13.8 9.8,16 12,16 C14.2,16 16,13.8 16,11 M12,16 V19 M9,19 H15",
        IconKind.Camera => "M5,6.5 H13 Q15,6.5 15,8.5 V15.5 Q15,17.5 13,17.5 H5 Q3,17.5 3,15.5 V8.5 Q3,6.5 5,6.5 Z M15,10 L20,7.5 Q21,7 21,8 V16 Q21,17 20,16.5 L15,14",
        IconKind.Speaker => "M4,9 H8 L13,5 V19 L8,15 H4 Q3,15 3,14 V10 Q3,9 4,9 Z M16,9 C18,10.5 18,13.5 16,15 M18.5,6.5 C22,9.5 22,14.5 18.5,17.5",
        IconKind.Folder => "M3,8 V6 Q3,4 5,4 H9 L12,7 H19 Q21,7 21,9 V17 Q21,19 19,19 H5 Q3,19 3,17 Z M3,8 H21",
        IconKind.Close => "M7,7 L17,17 M17,7 L7,17",
        IconKind.Menu => "M5,8 H19 M5,12 H19 M5,16 H19",
        IconKind.Play => "M8,5.8 Q8,4.5 9.2,5.3 L18.1,11 Q19.5,12 18.1,13 L9.2,18.7 Q8,19.5 8,18.2 Z",
        IconKind.Pause => "M7,5 H9 Q10,5 10,6 V18 Q10,19 9,19 H7 Q6,19 6,18 V6 Q6,5 7,5 Z M15,5 H17 Q18,5 18,6 V18 Q18,19 17,19 H15 Q14,19 14,18 V6 Q14,5 15,5 Z",
        IconKind.Stop => "M7,6 H17 Q18,6 18,7 V17 Q18,18 17,18 H7 Q6,18 6,17 V7 Q6,6 7,6 Z",
        IconKind.Flag => "M6,4 V20 M6,5 H16 L13,9 L16,13 H6",
        IconKind.Grip => "M9,5 L9,5.1 M15,5 L15,5.1 M9,12 L9,12.1 M15,12 L15,12.1 M9,19 L9,19.1 M15,19 L15,19.1",
        IconKind.Undo => "M9,5 L4,10 L9,15 M4,10 H14 C21,10 21,19 14,19 H12",
        IconKind.Redo => "M15,5 L20,10 L15,15 M20,10 H10 C3,10 3,19 10,19 H12",
        IconKind.SkipStart => "M5,6 H7 V18 H5 Z M18,6 L8,12 L18,18 Z",
        IconKind.SkipEnd => "M17,6 H19 V18 H17 Z M6,6 L16,12 L6,18 Z",
        IconKind.Layout => "M5,4 H19 Q21,4 21,6 V18 Q21,20 19,20 H5 Q3,20 3,18 V6 Q3,4 5,4 Z M8,4 V20 M8,10 H21",
        IconKind.Zoom => "M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 M15,15 L21,21 M7,10 H13 M10,7 V13",
        IconKind.Sun => "M12,7 A5,5 0 1 0 12,17 A5,5 0 1 0 12,7 M12,2 V4 M12,20 V22 M2,12 H4 M20,12 H22 M5,5 L6.5,6.5 M17.5,17.5 L19,19 M5,19 L6.5,17.5 M17.5,6.5 L19,5",
        IconKind.Moon => "M19,15 A8,8 0 1 1 9,4 A7,7 0 0 0 19,15 Z",
        IconKind.ChevronLeft => "M14.5,6 L8.5,12 L14.5,18",
        IconKind.ChevronRight => "M9.5,6 L15.5,12 L9.5,18",
        IconKind.ChevronUp => "M6,14.5 L12,8.5 L18,14.5",
        IconKind.ChevronDown => "M6,9.5 L12,15.5 L18,9.5",
        _ => "M4,4 H20 V20 H4 Z",
    };
}
