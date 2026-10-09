namespace DogSC.Core;

public enum BarDockEdge { None, Left, Right, Top, Bottom }

public readonly record struct BarBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public readonly record struct BarAnchor(BarDockEdge Edge, bool RightAligned, bool BottomAligned, double X, double Y)
{
    public static BarAnchor Capture(BarBounds bounds, BarBounds work, BarDockEdge edge)
    {
        var right = edge == BarDockEdge.Right || edge != BarDockEdge.Left && bounds.Left + bounds.Width / 2 >= work.Left + work.Width / 2;
        var bottom = edge == BarDockEdge.Bottom || edge != BarDockEdge.Top && bounds.Top + bounds.Height / 2 >= work.Top + work.Height / 2;
        return new(edge, right, bottom, right ? bounds.Right : bounds.Left, bottom ? bounds.Bottom : bounds.Top);
    }

    public BarBounds Place(BarBounds work, double width, double height, BarBounds? origin = null, double progress = 1)
    {
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);
        var left = Edge == BarDockEdge.Left ? work.Left : Edge == BarDockEdge.Right ? work.Right - width : RightAligned ? X - width : X;
        var top = Edge == BarDockEdge.Top ? work.Top : Edge == BarDockEdge.Bottom ? work.Bottom - height : BottomAligned ? Y - height : Y;
        if (origin is { } start)
        {
            left = start.Left + (left - start.Left) * progress;
            top = start.Top + (top - start.Top) * progress;
        }
        return new(Math.Clamp(left, work.Left, work.Right - width), Math.Clamp(top, work.Top, work.Bottom - height), width, height);
    }
}

public static class BarDockLayout
{
    public static BarDockEdge SnapEdge(BarBounds bounds, BarBounds work, double distance = 28)
    {
        var candidates = new[]
        {
            (BarDockEdge.Left, Math.Abs(bounds.Left - work.Left)),
            (BarDockEdge.Right, Math.Abs(work.Right - bounds.Right)),
            (BarDockEdge.Top, Math.Abs(bounds.Top - work.Top)),
            (BarDockEdge.Bottom, Math.Abs(work.Bottom - bounds.Bottom)),
        };
        var closest = candidates.MinBy(candidate => candidate.Item2);
        return closest.Item2 <= distance ? closest.Item1 : BarDockEdge.None;
    }
}
