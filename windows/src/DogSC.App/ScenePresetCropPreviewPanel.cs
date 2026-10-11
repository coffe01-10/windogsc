using DogSC.Core;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DogSC;

sealed class ScenePresetCropPreviewPanel : Border
{
    readonly CancellationTokenSource _thumbnailCancellation = new();
    readonly Func<CancellationToken, Task<BitmapSource?>> _loadThumbnail;
    readonly Grid _preview;
    readonly SourceCrop _crop;
    bool _started;
    public event Action? CancelRequested, ApplyRequested;

    public ScenePresetCropPreviewPanel(ScenePreset preset, ProjectDocument target,
        Func<CancellationToken, Task<BitmapSource?>> loadThumbnail)
    {
        _loadThumbnail = loadThumbnail; _crop = ScenePreset.Clone(preset.Canvas.Crop);
        Width = 500; Padding = new Thickness(24); CornerRadius = new CornerRadius(24); BorderThickness = new Thickness(.75);
        SetResourceReference(BackgroundProperty, "PanelBrush"); SetResourceReference(BorderBrushProperty, "LineBrush");
        var body = new StackPanel(); Child = body;
        var heading = new StackPanel();
        var title = Text("检查裁切适配", 18); title.SetResourceReference(TextBlock.FontFamilyProperty, "FontSemi"); title.FontWeight = FontWeights.SemiBold;
        heading.Children.Add(title); heading.Children.Add(Text(preset.Name, 12, secondary: true, top: 5)); body.Children.Add(heading);
        body.Children.Add(Text($"{preset.SourceWidth} × {preset.SourceHeight} → {target.Width} × {target.Height}", 13, top: 18));
        body.Children.Add(Text("录制尺寸不同，裁切将按比例适配。亮框内的画面会保留。", 12, secondary: true, top: 18));
        var aspect = target.Width / (double)target.Height;
        _preview = new Grid { Width = Math.Min(452, 260 * aspect), Height = Math.Min(260, 452 / aspect), Background = Brushes.Black };
        _preview.Clip = new RectangleGeometry(new Rect(0, 0, _preview.Width, _preview.Height), 10, 10);
        System.Windows.Automation.AutomationProperties.SetName(_preview, "裁切适配预览");
        System.Windows.Automation.AutomationProperties.SetHelpText(_preview, CropSummary(_crop, target.Width, target.Height));
        _preview.Children.Add(new ProgressBar { Width = 32, Height = 4, IsIndeterminate = true, VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(new Border { Child = _preview, Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        body.Children.Add(Text(CropSummary(_crop, target.Width, target.Height), 13, top: 18));
        body.Children.Add(Text("应用后可一次撤销，素材与时间线片段保持原样。", 11, secondary: true, top: 18));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "取消", Tag = "cancel", Style = (Style)FindResource("QuietButton"), IsCancel = true, MinHeight = 32, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 10, 0) };
        cancel.SetResourceReference(Control.BackgroundProperty, "SegmentWellBrush"); cancel.SetResourceReference(Control.BorderBrushProperty, "LineBrush"); cancel.BorderThickness = new Thickness(.75);
        cancel.Click += (_, _) => CancelRequested?.Invoke(); actions.Children.Add(cancel);
        var apply = new Button { Content = "应用预设", Tag = "apply", Style = (Style)FindResource("PrimaryButton"), IsDefault = true, MinHeight = 32, Padding = new Thickness(12, 4, 12, 4) };
        apply.Click += (_, _) => ApplyRequested?.Invoke(); actions.Children.Add(apply); body.Children.Add(actions);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; CancelRequested?.Invoke(); } };
        Loaded += async (_, _) => { if (_started) return; _started = true; await LoadThumbnailAsync(); };
        Unloaded += (_, _) => _thumbnailCancellation.Cancel();
    }

    async Task LoadThumbnailAsync()
    {
        BitmapSource? thumbnail = null;
        try { thumbnail = await _loadThumbnail(_thumbnailCancellation.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception error) { AppPaths.Log("裁切适配预览未加载：" + error.Message); }
        if (_thumbnailCancellation.IsCancellationRequested) return;
        _preview.Children.Clear();
        if (thumbnail is null)
        {
            var unavailable = Text("预览暂不可用，可取消后稍后重试。", 11);
            unavailable.Foreground = Brushes.White; unavailable.HorizontalAlignment = HorizontalAlignment.Center; unavailable.VerticalAlignment = VerticalAlignment.Center;
            _preview.Children.Add(unavailable); return;
        }
        _preview.Children.Add(new Image { Source = thumbnail, Stretch = Stretch.Uniform });
        var rect = new Rect(_crop.X * _preview.Width, _crop.Y * _preview.Height, _crop.Width * _preview.Width, _crop.Height * _preview.Height);
        var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(new Rect(0, 0, _preview.Width, _preview.Height))); mask.Children.Add(new RectangleGeometry(rect));
        _preview.Children.Add(new System.Windows.Shapes.Path { Data = mask, Fill = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0)), IsHitTestVisible = false });
        var frame = new Rectangle { Width = rect.Width, Height = rect.Height, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
        var overlay = new Canvas(); Canvas.SetLeft(frame, rect.X); Canvas.SetTop(frame, rect.Y); overlay.Children.Add(frame); _preview.Children.Add(overlay);
    }

    static TextBlock Text(string value, double size, bool secondary = false, double top = 0)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        if (secondary) text.SetResourceReference(TextBlock.ForegroundProperty, "InkSecondaryBrush"); return text;
    }

    internal static string CropSummary(SourceCrop crop, int width, int height)
    {
        if (crop.X == 0 && crop.Y == 0 && crop.Width == 1 && crop.Height == 1) return "完整画面";
        static double Pixels(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
        return $"上 {Pixels(crop.Y * height)} · 下 {Pixels((1 - crop.Y - crop.Height) * height)} · 左 {Pixels(crop.X * width)} · 右 {Pixels((1 - crop.X - crop.Width) * width)} px";
    }
}
