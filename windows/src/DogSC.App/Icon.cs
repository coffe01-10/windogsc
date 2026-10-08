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
}

public sealed class Icon : FrameworkElement
{
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
        try
        {
            drawing.PushTransform(new ScaleTransform(ActualWidth / 24.0, ActualHeight / 24.0));
            drawing.DrawGeometry(null, pen, Geometry.Parse(PathFor(Kind)));
            drawing.Pop();
        }
        catch
        {
            drawing.DrawEllipse(null, pen, new Point(ActualWidth / 2, ActualHeight / 2), 4, 4);
        }
    }

    static string PathFor(IconKind kind) => kind switch
    {
        IconKind.Monitor => "M3,6 H21 V16 H3 Z M8,19 H16 M12,16 V19",
        IconKind.Window => "M4,4 H20 V20 H4 Z M4,9 H20",
        IconKind.Region => "M9,4 H4 V9 M15,4 H20 V9 M20,15 V20 H15 M4,15 V20 H9",
        IconKind.Device => "M3,8 H13 L20,5 V19 L13,16 H3 Z",
        IconKind.Mic => "M9,6 C9,4.3 10.3,3 12,3 C13.7,3 15,4.3 15,6 V11 C15,12.7 13.7,14 12,14 C10.3,14 9,12.7 9,11 Z M8,11 C8,13.8 9.8,16 12,16 C14.2,16 16,13.8 16,11 M12,16 V19 M9,19 H15",
        IconKind.Camera => "M3,8 H14 L17,5 H21 V19 H3 Z M8,12 A2,2 0 1 0 12,12 A2,2 0 1 0 8,12",
        IconKind.Speaker => "M4,9 H8 L13,5 V19 L8,15 H4 Z M16,9 C18,10 18,14 16,15",
        IconKind.Folder => "M3,7 H10 L12,5 H21 V19 H3 Z",
        IconKind.Close => "M6,6 L18,18 M18,6 L6,18",
        IconKind.Menu => "M5,7 H19 M5,12 H19 M5,17 H19",
        IconKind.Play => "M8,5 L18,12 L8,19 Z",
        IconKind.Pause => "M7,5 H10 V19 H7 Z M14,5 H17 V19 H14 Z",
        IconKind.Stop => "M6,6 H18 V18 H6 Z",
        IconKind.Flag => "M6,4 V20 M6,5 H16 L13,9 L16,13 H6",
        _ => "M4,4 H20 V20 H4 Z",
    };
}
