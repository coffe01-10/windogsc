namespace DogSC.Core;

// Geometry is shared by handles, preview and export, including rotated extents.
public readonly record struct OverlayBox(double X, double Y, double Width, double Height, double Rotation = 0)
{
    public (double X, double Y) Point(double u, double v)
    {
        var a = Rotation * Math.PI / 180; var x = (u - .5) * Width; var y = (v - .5) * Height;
        return (X + x * Math.Cos(a) - y * Math.Sin(a), Y + x * Math.Sin(a) + y * Math.Cos(a));
    }
    public (double U, double V) Local(double x, double y)
    {
        var a = Rotation * Math.PI / 180; x -= X; y -= Y;
        return (.5 + (x * Math.Cos(a) + y * Math.Sin(a)) / Width, .5 + (-x * Math.Sin(a) + y * Math.Cos(a)) / Height);
    }
    public OverlayBox Fit(double width, double height)
    {
        var a = Rotation * Math.PI / 180;
        var extentX = Math.Abs(Math.Cos(a)) * Width + Math.Abs(Math.Sin(a)) * Height;
        var extentY = Math.Abs(Math.Sin(a)) * Width + Math.Abs(Math.Cos(a)) * Height;
        var scale = Math.Min(1, Math.Min(width / extentX, height / extentY));
        var ex = extentX * scale / 2; var ey = extentY * scale / 2;
        return this with { Width = Width * scale, Height = Height * scale,
            X = Math.Clamp(X, ex, Math.Max(ex, width - ex)), Y = Math.Clamp(Y, ey, Math.Max(ey, height - ey)) };
    }
    public OverlayBox Resize(int corner, double dx, double dy, double minWidth, double maxWidth)
    {
        var u = corner is 0 or 3 ? 0d : 1d; var v = corner < 2 ? 0d : 1d;
        var anchor = Point(1 - u, 1 - v); var tip = Point(u, v);
        var vx = tip.X - anchor.X; var vy = tip.Y - anchor.Y;
        var scale = Math.Clamp(((tip.X + dx - anchor.X) * vx + (tip.Y + dy - anchor.Y) * vy) / (vx * vx + vy * vy), minWidth / Width, maxWidth / Width);
        return this with { X = anchor.X + vx * scale / 2, Y = anchor.Y + vy * scale / 2, Width = Width * scale, Height = Height * scale };
    }
}
