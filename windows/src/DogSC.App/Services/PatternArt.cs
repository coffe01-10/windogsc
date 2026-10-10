using DogSC.Core;

namespace DogSC;

/// <summary>
/// 把 PatternLibrary 的背景规格烘成 PNG：底层纯色/渐变 + 逐层透明叠加
/// （网格、点阵、等角线、径向辉光、高斯光斑），参数与 Mac SharedFrameRenderer 一致。
/// </summary>
public static class PatternArt
{
    public static (string Base, List<string> Layers) Bake(
        PatternLibrary.WallpaperBake bake, int canvasW, int canvasH, string directory)
    {
        Directory.CreateDirectory(directory);
        var index = 0;
        var basePath = Path.Combine(directory, "wp-base.png");
        if (bake.BaseKind == "linear")
        {
            Render(canvasW, canvasH, basePath, context =>
            {
                var brush = new LinearGradientBrush(ToColor(bake.BaseColorA), ToColor(bake.BaseColorB),
                    new Point(0, 0), new Point(1, 1));
                context.DrawRectangle(brush, null, new Rect(0, 0, canvasW, canvasH));
            });
        }
        else
        {
            Render(canvasW, canvasH, basePath, context =>
                context.DrawRectangle(new SolidColorBrush(ToColor(bake.BaseColorA)), null,
                    new Rect(0, 0, canvasW, canvasH)));
        }

        var layers = new List<string>();
        foreach (var layer in bake.Layers)
        {
            var path = Path.Combine(directory, "wp-layer" + index++ + ".png");
            Render(layer.PngW, layer.PngH, path, context => Draw(layer, context));
            layers.Add(path);
        }

        return (basePath, layers);
    }

    private static void Draw(PatternLibrary.WallpaperLayer layer, DrawingContext context)
    {
        switch (layer.Kind)
        {
            case "grid":
            case "diaggrid":
            {
                var tile = Math.Max(layer.P1, 4);
                var pen = new Pen(new SolidColorBrush(WithOpacity(layer.ColorA, layer.P2)),
                    Math.Max(tile * 0.008, 0.75));
                for (var y = 0.0; y < layer.PngH; y += tile)
                {
                    for (var x = 0.0; x < layer.PngW; x += tile)
                    {
                        if (layer.Kind == "diaggrid")
                        {
                            context.DrawLine(pen, new Point(x, y), new Point(x + tile, y + tile));
                            context.DrawLine(pen, new Point(x, y + tile), new Point(x + tile, y));
                        }
                        else
                        {
                            var inset = pen.Thickness / 2;
                            context.DrawRectangle(null, pen,
                                new Rect(x + inset, y + inset, Math.Max(tile - pen.Thickness, 1),
                                    Math.Max(tile - pen.Thickness, 1)));
                        }
                    }
                }

                break;
            }
            case "dots":
            {
                var tile = Math.Max(layer.P1, 4);
                var radius = Math.Max(tile * 0.026, 0.8);
                var brush = new SolidColorBrush(WithOpacity(layer.ColorB, layer.P2));
                for (var y = tile / 2; y < layer.PngH + tile / 2; y += tile)
                {
                    for (var x = tile / 2; x < layer.PngW + tile / 2; x += tile)
                    {
                        context.DrawEllipse(brush, null, new Point(x, y), radius, radius);
                    }
                }

                break;
            }
            case "glow":
            {
                // Mac CIRadialGradient：r0 内纯色0，r0..r1 渐变到背景色1。
                context.DrawRectangle(new SolidColorBrush(ToColor(layer.ColorB)), null,
                    new Rect(0, 0, layer.PngW, layer.PngH));
                var radius = Math.Max(layer.P2, 1);
                var inner = Math.Clamp(layer.P1 / radius, 0.01, 0.99);
                var brush2 = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5, 0.5),
                    RadiusX = 0.5,
                    RadiusY = 0.5,
                };
                brush2.GradientStops.Add(new GradientStop(ToColor(layer.ColorA), 0));
                brush2.GradientStops.Add(new GradientStop(ToColor(layer.ColorA), inner));
                brush2.GradientStops.Add(new GradientStop(ToColor(layer.ColorB), 1));
                context.DrawEllipse(brush2, null, new Point(layer.PngW / 2.0, layer.PngH / 2.0),
                    radius, radius);
                break;
            }
            case "blob":
            {
                // Mac CIGaussianGradient：中心色 → 透明的软光斑。
                var radius = Math.Max(layer.P1, 1);
                var brush3 = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5, 0.5),
                    RadiusX = 0.5,
                    RadiusY = 0.5,
                };
                brush3.GradientStops.Add(new GradientStop(ToColor(layer.ColorA), 0));
                brush3.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
                context.DrawEllipse(brush3, null, new Point(layer.PngW / 2.0, layer.PngH / 2.0),
                    radius, radius);
                break;
            }
        }
    }

    private static void Render(int width, int height, string path, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            draw(context);
        }

        var bitmap = new RenderTargetBitmap(Math.Max(width, 1), Math.Max(height, 1), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static Color WithOpacity(uint argb, double opacity)
    {
        var color = ToColor(argb);
        color.A = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
        return color;
    }
}
