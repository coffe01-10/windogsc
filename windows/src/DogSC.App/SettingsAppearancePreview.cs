namespace DogSC;

// The system preview masks the right half of one complete miniature, so its
// content lines stay aligned across the light/dark boundary.
sealed class SettingsAppearancePreview(string appearance) : FrameworkElement
{
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var bounds = new Rect(RenderSize);
        if (bounds.Width < 1 || bounds.Height < 1) return;
        drawing.PushClip(new RectangleGeometry(bounds, 6, 6));
        DrawMiniature(drawing, appearance == "dark");
        if (appearance == "system")
        {
            drawing.PushClip(new RectangleGeometry(new Rect(bounds.Width / 2, 0, bounds.Width / 2, bounds.Height)));
            DrawMiniature(drawing, true);
            drawing.Pop();
        }
        drawing.Pop();
        drawing.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)), .5),
            new Rect(.25, .25, bounds.Width - .5, bounds.Height - .5), 6, 6);
    }

    void DrawMiniature(DrawingContext drawing, bool dark)
    {
        drawing.DrawRectangle(dark ? Gray(26) : Brushes.White, null, new Rect(RenderSize));
        drawing.DrawRectangle(Gray(dark ? 38 : 240), null, new Rect(0, 0, 30, ActualHeight));
        var chrome = new SolidColorBrush(Color.FromArgb(77, 128, 128, 128));
        drawing.DrawEllipse(chrome, null, new Point(9.5, 9.5), 2.5, 2.5);
        drawing.DrawRoundedRectangle(chrome, null, new Rect(7, 18, 16, 7), 2, 2);
        var contentWidth = ActualWidth - 46;
        if (contentWidth <= 0) return; // The preview can briefly have no width during layout.
        for (var line = 0; line < 3; line++)
            drawing.DrawRoundedRectangle(Gray(dark ? 59 : 235), null,
                new Rect(38, (ActualHeight - 25) / 2 + line * 10, contentWidth, 5), 2.5, 2.5);
    }

    static Brush Gray(int value) => new SolidColorBrush(Color.FromRgb((byte)value, (byte)value, (byte)value));
}
