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
    ArrowUp,
    CanvasPicture,
    OpeningStack,
    MotionViewfinder,
    CursorMotion,
    Crop,
    Sliders,
    Plus,
    Layers,
    InsetRectangle,
    Photo,
    GridPattern,
    WaveformRectangle,
    PhotoPlus,
    Scissors,
    Trash,
    Cube,
    Eye,
    Bookmark,
    Magnet,
    Share,
    Settings,
    Converge,
    SideSlide,
    Ban,
    Sparkles,
    Shield,
    Info,
    Appearance,
    Bell,
    ExternalLink,
    RecordCircle,
}

public sealed class Icon : FrameworkElement
{
    static readonly Geometry OpeningSparkles = CreateGeometry("M7,2 L8.15,5.3 L11.5,6.4 L8.15,7.55 L7,10.9 L5.85,7.55 L2.5,6.4 L5.85,5.3 Z M17.2,2.1 L17.9,4.1 L19.9,4.8 L17.9,5.5 L17.2,7.5 L16.5,5.5 L14.5,4.8 L16.5,4.1 Z");

    static Geometry CreateGeometry(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

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

    protected override void OnRender(DrawingContext drawing) => DrawSymbol(drawing, Kind, Foreground ?? Brushes.Black, ActualWidth, ActualHeight);

    internal static void DrawSymbol(DrawingContext drawing, IconKind kind, Brush brush, double width, double height)
    {
        if (kind == IconKind.Record)
        {
            drawing.DrawEllipse(brush, null, new Point(width / 2, height / 2), width * 0.34, height * 0.34);
            return;
        }

        var pen = new Pen(brush, 1.45)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        var scale = Math.Min(width, height) / 24;
        drawing.PushTransform(new TranslateTransform((width - 24 * scale) / 2, (height - 24 * scale) / 2));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        var filled = kind is IconKind.Play or IconKind.Pause or IconKind.Stop or IconKind.SkipStart or IconKind.SkipEnd;
        var geometry = Geometries[kind];
        var bounds = geometry.Bounds;
        if (kind == IconKind.OpeningStack) bounds.Union(OpeningSparkles.Bounds);
        // 按图形的实际边界居中，麦克风、扬声器等非对称路径不再偏向画布一侧。
        drawing.PushTransform(new TranslateTransform(12 - (bounds.Left + bounds.Width / 2) + (kind == IconKind.Play ? 1 : 0), 12 - (bounds.Top + bounds.Height / 2)));
        drawing.DrawGeometry(filled ? brush : null, filled ? null : pen, geometry);
        if (kind == IconKind.RecordCircle) drawing.DrawEllipse(brush, null, new Point(12, 12), 3.2, 3.2);
        if (kind is IconKind.CanvasPicture or IconKind.Photo or IconKind.PhotoPlus)
            drawing.DrawEllipse(brush, null, kind == IconKind.CanvasPicture ? new Point(15.5, 9.2) : new Point(8, 8), 1.3, 1.3);
        if (kind == IconKind.OpeningStack)
            drawing.DrawGeometry(brush, null, OpeningSparkles);
        drawing.Pop();
        drawing.Pop();
        drawing.Pop();
    }

    static string PathFor(IconKind kind) => kind switch
    {
        IconKind.RecordCircle => "M21,12 A9,9 0 1 0 3,12 A9,9 0 1 0 21,12",
        IconKind.Shield => "M12,2 L21,6 V12 C21,17 17,21 12,23 C7,21 3,17 3,12 V6 Z M9,12 V10 A3,3 0 0 1 15,10 V12 M8,12 H16 V18 H8 Z M12,14 V16",
        IconKind.Info => "M21,12 A9,9 0 1 0 3,12 A9,9 0 1 0 21,12 M12,7 V7.1 M10,11 H12 V17 M10,17 H14",
        IconKind.Appearance => "M21,12 A9,9 0 1 0 3,12 A9,9 0 1 0 21,12 M12,3 V21 M12,5 L17,7 M12,8 L20,11 M12,12 L20,15 M12,16 L16,18",
        IconKind.Bell => "M5,17 H19 L17,14 V10 A5,5 0 0 0 7,10 V14 Z M10,20 Q12,23 14,20 M12,3 V5",
        IconKind.ExternalLink => "M13,3 H21 V11 M21,3 L10,14 M9,5 H5 Q3,5 3,7 V19 Q3,21 5,21 H17 Q19,21 19,19 V15",
        IconKind.Ban => "M21,12 A9,9 0 1 0 3,12 A9,9 0 1 0 21,12 M5.6,5.6 L18.4,18.4",
        IconKind.Sparkles => "M12,3 L14.2,9.8 L21,12 L14.2,14.2 L12,21 L9.8,14.2 L3,12 L9.8,9.8 Z M20,2 V6 M18,4 H22",
        IconKind.Settings => "M10,2 H14 L15,5 L18,6 L21,5 L23,9 L20.5,11 V14 L23,16 L21,20 L18,19 L15,20 L14,23 H10 L9,20 L6,19 L3,20 L1,16 L3.5,14 V11 L1,9 L3,5 L6,6 L9,5 Z M16,12.5 A4,4 0 1 0 8,12.5 A4,4 0 1 0 16,12.5",
        IconKind.Converge => "M3,3 L9,9 M4,9 H9 V4 M21,21 L15,15 M20,15 H15 V20",
        IconKind.SideSlide => "M3,8 H21 M7,4 L3,8 L7,12 M3,16 H21 M17,12 L21,16 L17,20",
        IconKind.Share => "M8,9 H5 Q3,9 3,11 V20 Q3,22 5,22 H19 Q21,22 21,20 V11 Q21,9 19,9 H16 M12,16 V2 M7,7 L12,2 L17,7",
        IconKind.Cube => "M12,3 L21,8 V17 L12,22 L3,17 V8 Z M3,8 L12,13 L21,8 M12,13 V22 M7.5,5.5 L16.5,10.5",
        IconKind.Eye => "M2,12 C6,4 18,4 22,12 C18,20 6,20 2,12 Z M15,12 A3,3 0 1 0 9,12 A3,3 0 1 0 15,12",
        IconKind.Bookmark => "M7,3 H17 Q18,3 18,4 V21 L12,17 L6,21 V4 Q6,3 7,3 Z",
        IconKind.Magnet => "M5,3 H9 V13 A3,3 0 0 0 15,13 V3 H19 V13 A7,7 0 0 1 5,13 Z M5,7 H9 M15,7 H19",
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
        IconKind.ArrowUp => "M12,20 V4 M5,11 L12,4 L19,11",
        // WPF counterparts of the SF Symbols used by InspectorTab / RecorderViews.swift.
        // photo.on.rectangle.angled
        IconKind.CanvasPicture => "M6,7 L3.2,7.9 Q1.8,8.3 2.2,9.7 L5,19.2 Q5.4,20.7 6.9,20.3 L16,17.5 M9,5 H20 Q21.5,5 21.5,6.5 V15.5 Q21.5,17 20,17 H9 Q7.5,17 7.5,15.5 V6.5 Q7.5,5 9,5 Z M8,14 L11.4,10.8 L14.1,13.2 L16.8,10.7 L21,14.7",
        // sparkles.rectangle.stack
        IconKind.OpeningStack => "M11.7,9 H19.5 Q21,9 21,10.5 V18 Q21,19.5 19.5,19.5 H5.5 Q4,19.5 4,18 V12 M11,6.6 H19 M12,4.1 H14 M7,22 H18",
        // viewfinder
        IconKind.MotionViewfinder => "M9,3 H5 Q3,3 3,5 V9 M15,3 H19 Q21,3 21,5 V9 M21,15 V19 Q21,21 19,21 H15 M9,21 H5 Q3,21 3,19 V15",
        // cursorarrow.motionlines
        IconKind.CursorMotion => "M4,3 L4,18 L8.1,14.2 L11.2,20.2 L13.9,18.8 L10.7,12.9 L16.1,12.5 Z M17,4 L20,3 M18.5,7 H22 M17.8,10 L21,11",
        IconKind.Crop => "M6,2 V16 Q6,18 8,18 H22 M2,6 H16 Q18,6 18,8 V22 M9,6 H18 M6,9 V18",
        IconKind.Sliders => "M3,6 H8 M12,6 H21 M3,12 H15 M19,12 H21 M3,18 H5 M9,18 H21 M10,4 V8 M17,10 V14 M7,16 V20",
        IconKind.Plus => "M12,4 V20 M4,12 H20",
        IconKind.Scissors => "M8,8 L20,20 M8,16 L20,4 M9,6 A3,3 0 1 0 3,6 A3,3 0 1 0 9,6 M9,18 A3,3 0 1 0 3,18 A3,3 0 1 0 9,18",
        IconKind.Trash => "M4,6 H20 M9,6 V3 H15 V6 M6,6 L7,21 H17 L18,6 M10,10 V17 M14,10 V17",
        IconKind.Layers => "M12,3 L22,8 L12,13 L2,8 Z M3,12 L12,16.5 L21,12 M3,16 L12,20.5 L21,16",
        IconKind.InsetRectangle => "M4,5 H20 Q22,5 22,7 V17 Q22,19 20,19 H4 Q2,19 2,17 V7 Q2,5 4,5 Z M6,9 H18 V15 H6 Z",
        IconKind.Photo => "M4,4 H20 Q22,4 22,6 V18 Q22,20 20,20 H4 Q2,20 2,18 V6 Q2,4 4,4 Z M2,17 L8.5,10.5 L13,14.7 L16.7,11 L22,17",
        IconKind.GridPattern => "M4,3 H20 Q21,3 21,4 V20 Q21,21 20,21 H4 Q3,21 3,20 V4 Q3,3 4,3 Z M9,3 V21 M15,3 V21 M3,9 H21 M3,15 H21",
        IconKind.WaveformRectangle => "M4,4 H20 Q22,4 22,6 V18 Q22,20 20,20 H4 Q2,20 2,18 V6 Q2,4 4,4 Z M4,12 H7 L9,7 L12,17 L15,10 L17,12 H20",
        IconKind.PhotoPlus => "M4,4 H19 Q21,4 21,6 V11 M21,20 H4 Q2,20 2,18 V6 Q2,4 4,4 M2,17 L8.5,10.5 L13,14.7 L15.5,12.2 M19,13 V21 M15,17 H23",
        _ => "M4,4 H20 V20 H4 Z",
    };
}
