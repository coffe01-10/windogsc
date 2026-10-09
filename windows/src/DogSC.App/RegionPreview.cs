using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DogSC.Core;

namespace DogSC;

// Each layer samples only the composition beneath it, avoiding recursive VisualBrushes.
sealed class RegionPreview
{
    readonly List<(Rectangle Layer, SoftRegion Region)> _layers = [];

    public void Rebuild(Grid host, FrameworkElement content, IEnumerable<SoftRegion> regions,
        double width, double height, int sourceWidth, int sourceHeight, SourceCrop crop, double focusBlur = 0)
    {
        if (content.Parent is Panel parent) parent.Children.Remove(content);
        host.Children.Clear();
        _layers.Clear();
        FrameworkElement previous = content;
        foreach (var region in regions)
        {
            // Geometry masks avoid allocating and filling a full bitmap on every pointer move.
            var radius = region.Corner * Math.Min(region.Width * sourceWidth, region.Height * sourceHeight);
            var selection = new RectangleGeometry(new Rect((region.X - crop.X) / crop.Width * width,
                (region.Y - crop.Y) / crop.Height * height, region.Width / crop.Width * width, region.Height / crop.Height * height),
                radius * width / (sourceWidth * crop.Width), radius * height / (sourceHeight * crop.Height));
            Geometry mask = selection;
            if (region.Style == "spotlight")
                mask = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, width, height)), selection);
            var maskBrush = new DrawingBrush(new GeometryDrawing(Brushes.White, null, mask))
            {
                Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height),
            };
            maskBrush.Freeze();
            var layer = new Rectangle
            {
                IsHitTestVisible = false,
                Width = width, Height = height,
                Fill = region.Style == "spotlight" ? Brushes.Black : new VisualBrush(previous) { Stretch = Stretch.Fill },
                OpacityMask = maskBrush,
            };
            if (region.Style != "spotlight")
                layer.Effect = new BlurEffect { Radius = (4 + region.Strength * 24) * width / (sourceWidth * crop.Width) };
            var composition = new Grid { Width = width, Height = height };
            composition.Children.Add(previous);
            composition.Children.Add(layer);
            previous = composition;
            _layers.Add((layer, region));
        }
        if (focusBlur > .02)
        {
            var pixels = MaskImage.CreateRadial(256, 144);
            for (var i = 0; i < pixels.Length; i += 4) pixels[i + 3] = pixels[i];
            var bitmap = BitmapSource.Create(256, 144, 96, 96, PixelFormats.Bgra32, null,
                pixels, 256 * 4);
            bitmap.Freeze();
            var composition = new Grid { Width = width, Height = height };
            var layer = new Rectangle
            {
                IsHitTestVisible = false, Width = width, Height = height,
                Fill = new VisualBrush(previous) { Stretch = Stretch.Fill },
                OpacityMask = new ImageBrush(bitmap) { Stretch = Stretch.Fill },
                Effect = new BlurEffect { Radius = (4 + focusBlur * 26) * width / sourceWidth },
            };
            composition.Children.Add(previous);
            composition.Children.Add(layer);
            previous = composition;
        }
        host.Children.Add(previous);
    }

    public void Update(double time, string? editingId = null, bool drawing = false)
    {
        foreach (var (layer, region) in _layers)
            layer.Opacity = (drawing ? 0 : region.Id == editingId ? 1 : EffectTiming.OpacityAt(region.Timing, time)) * (region.Style == "spotlight" ? region.Dimming : 1);
    }
}
