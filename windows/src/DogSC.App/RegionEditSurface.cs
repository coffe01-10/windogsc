namespace DogSC;

// A stable capture surface: rebuilding effect brushes never interrupts a drag.
public sealed class RegionEditSurface : FrameworkElement
{
    internal Action<DrawingContext>? Paint;
    internal Action<Point>? Down, Move;
    internal Action<bool>? Finish;

    public RegionEditSurface()
    {
        Focusable = true;
        FocusVisualStyle = null;
        MouseLeftButtonDown += (_, e) => { Focus(); Down?.Invoke(e.GetPosition(this)); e.Handled = true; };
        MouseMove += (_, e) => Move?.Invoke(e.GetPosition(this));
        MouseLeftButtonUp += (_, e) => { Finish?.Invoke(false); e.Handled = true; };
        LostMouseCapture += (_, _) => Finish?.Invoke(true);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        Paint?.Invoke(dc);
    }
}
