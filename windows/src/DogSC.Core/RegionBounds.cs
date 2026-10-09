namespace DogSC.Core;

[Flags]
public enum RegionEdge { Move = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

public readonly record struct RegionBounds(double X, double Y, double Width, double Height)
{
    public static RegionBounds Read(SoftRegion region) => new(region.X, region.Y, region.Width, region.Height);
    public void Apply(SoftRegion region) => (region.X, region.Y, region.Width, region.Height) = (X, Y, Width, Height);

    public static RegionBounds Draw(double x1, double y1, double x2, double y2)
    {
        x1 = Math.Clamp(x1, 0, 1); y1 = Math.Clamp(y1, 0, 1);
        x2 = Math.Clamp(x2, 0, 1); y2 = Math.Clamp(y2, 0, 1);
        return new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    public RegionBounds Drag(RegionEdge edge, double dx, double dy)
    {
        if (edge == RegionEdge.Move)
            return this with { X = Math.Clamp(X + dx, 0, 1 - Width), Y = Math.Clamp(Y + dy, 0, 1 - Height) };
        var left = X; var top = Y; var right = X + Width; var bottom = Y + Height;
        if (edge.HasFlag(RegionEdge.Left)) left = Math.Clamp(left + dx, 0, right - .0001);
        if (edge.HasFlag(RegionEdge.Right)) right = Math.Clamp(right + dx, left + .0001, 1);
        if (edge.HasFlag(RegionEdge.Top)) top = Math.Clamp(top + dy, 0, bottom - .0001);
        if (edge.HasFlag(RegionEdge.Bottom)) bottom = Math.Clamp(bottom + dy, top + .0001, 1);
        return new(left, top, right - left, bottom - top);
    }
}
