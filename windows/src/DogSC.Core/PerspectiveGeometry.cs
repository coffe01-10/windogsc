namespace DogSC.Core;

public readonly record struct ProjectionCorner(double X, double Y, double Weight);

public static class PerspectiveGeometry
{
    // A camera looking at a rotated plane. The same destination quadrilateral
    // drives the WPF mesh and ffmpeg's perspective filter.
    public static ProjectionCorner[] Corners(double width, double height, double tiltX, double tiltY)
    {
        var ax = tiltX * Math.PI / 180;
        var ay = tiltY * Math.PI / 180;
        var distance = Math.Max(width, height) * 1.8;
        var result = new ProjectionCorner[4];
        for (var i = 0; i < 4; i++)
        {
            var x = (i % 2 - 0.5) * width;
            var y = (i / 2 - 0.5) * height;
            var ry = y * Math.Cos(ax);
            var z = y * Math.Sin(ax);
            var rx = x * Math.Cos(ay) + z * Math.Sin(ay);
            z = -x * Math.Sin(ay) + z * Math.Cos(ay);
            var weight = (distance - z) / distance;
            result[i] = new(width / 2 + rx / weight, height / 2 + ry / weight, weight);
        }
        // Fit inside the frame so neither renderer clips the near corners.
        var extent = result.Max(p => Math.Max(Math.Abs((p.X - width / 2) / (width / 2)), Math.Abs((p.Y - height / 2) / (height / 2))));
        var fit = 1 / Math.Max(1, extent);
        return result.Select(p => new ProjectionCorner(width / 2 + (p.X - width / 2) * fit,
            height / 2 + (p.Y - height / 2) * fit, p.Weight)).ToArray();
    }

    public static (double X, double Y) DestinationPoint(ProjectionCorner[] quad, double u, double v)
    {
        var p = quad[0]; var right = quad[1]; var bottom = quad[2];
        var weight = p.Weight + (right.Weight - p.Weight) * u + (bottom.Weight - p.Weight) * v;
        return ((p.X * p.Weight + (right.X * right.Weight - p.X * p.Weight) * u
            + (bottom.X * bottom.Weight - p.X * p.Weight) * v) / weight,
            (p.Y * p.Weight + (right.Y * right.Weight - p.Y * p.Weight) * u
            + (bottom.Y * bottom.Weight - p.Y * p.Weight) * v) / weight);
    }

    public static (double X, double Y) SourcePoint(ProjectionCorner[] quad, double x, double y)
    {
        var p = quad[0];
        var right = quad[1];
        var bottom = quad[2];
        var a = right.X * right.Weight - p.X * p.Weight - x * (right.Weight - p.Weight);
        var b = bottom.X * bottom.Weight - p.X * p.Weight - x * (bottom.Weight - p.Weight);
        var c = (x - p.X) * p.Weight;
        var d = right.Y * right.Weight - p.Y * p.Weight - y * (right.Weight - p.Weight);
        var e = bottom.Y * bottom.Weight - p.Y * p.Weight - y * (bottom.Weight - p.Weight);
        var f = (y - p.Y) * p.Weight;
        var determinant = a * e - b * d;
        return ((c * e - b * f) / determinant, (a * f - c * d) / determinant);
    }
}
