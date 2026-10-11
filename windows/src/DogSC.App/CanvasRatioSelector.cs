using System.Globalization;
using System.Windows.Controls.Primitives;
using DogSC.Core;

namespace DogSC;

sealed class CanvasRatioSelector : Button
{
    readonly Func<CanvasSettings> _canvas;
    readonly TextBlock _value = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly Popup _popup;
    readonly CanvasRatioPanel _panel;

    public CanvasRatioSelector(Func<CanvasSettings> canvas, Action<CanvasSettings> apply)
    {
        _canvas = canvas; Tag = "ratio"; ToolTip = "画布比例";
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new Icon { Kind = IconKind.AspectRatio, Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        _value.SetBinding(TextBlock.FontFamilyProperty, new System.Windows.Data.Binding(nameof(FontFamily)) { Source = this });
        label.Children.Add(_value);
        var arrow = new Icon { Kind = IconKind.ChevronDown, Width = 9, Height = 9, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        arrow.SetResourceReference(Icon.ForegroundProperty, "InkSecondaryBrush"); label.Children.Add(arrow); Content = label;
        _panel = new CanvasRatioPanel(canvas, apply);
        var shell = new Border { Padding = new Thickness(6), Child = _panel };
        _popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = false, Child = shell };
        _panel.CloseRequested += () => _popup.IsOpen = false;
        _popup.Opened += (_, _) => { FluidMotion.Entrance(_panel); _panel.Focus(); };
        _popup.Closed += (_, _) => Focus();
        Click += (_, _) => { if (!_popup.IsOpen) _panel.Prepare(); _popup.IsOpen = !_popup.IsOpen; };
        Unloaded += (_, _) => _popup.IsOpen = false;
        System.Windows.Automation.AutomationProperties.SetName(this, "画布比例"); Sync();
    }

    public void Sync()
    {
        var canvas = _canvas();
        _value.Text = CanvasRatioPanel.IsCustom(canvas) ? "自定义" : canvas.Aspect is "source" or "适应" ? "适应" : canvas.Aspect;
        System.Windows.Automation.AutomationProperties.SetItemStatus(this, _value.Text);
        _panel.RefreshFromCanvas();
    }
}

// The same six presets and draft/application behavior as EditorCanvasRatioSelector.swift.
sealed class CanvasRatioPanel : Border
{
    static readonly string[] Presets = ["source", "16:9", "4:3", "2.39:1", "1:1", "custom"];
    static readonly string[] Titles = ["适应", "16:9", "4:3", "电影", "1:1", "自定义"];
    readonly Func<CanvasSettings> _canvas;
    readonly Action<CanvasSettings> _apply;
    readonly Grid _presets = new() { Height = 144 };
    readonly Border _selection = FluidMotion.Selection("PopoverSelectionBrush", 10);
    readonly List<Button> _presetButtons = [];
    readonly List<Border> _wells = [];
    readonly StackPanel _details = new() { Margin = new Thickness(0, 12, 0, 0) };
    readonly Border _detailClip = new() { ClipToBounds = true };
    readonly Grid _detailLayer = new() { IsHitTestVisible = false, Height = 0, VerticalAlignment = VerticalAlignment.Top };
    readonly Border _orientation;
    readonly FluidSegments _directions;
    readonly StackPanel _custom = new();
    readonly TextBox _width = Field("自定义比例宽");
    readonly TextBox _height = Field("自定义比例高");
    readonly Button _applyCustom = new() { Content = "应用" };
    bool _editingCustom;
    bool _positioned;
    string _aspectAtSync = "";
    bool _customAtSync;
    double _detailHeight;
    public event Action? CloseRequested;

    public CanvasRatioPanel(Func<CanvasSettings> canvas, Action<CanvasSettings> apply)
    {
        _canvas = canvas; _apply = apply;
        Width = 320; Padding = new Thickness(18); CornerRadius = new CornerRadius(16);
        SetResourceReference(BackgroundProperty, "CardBrush"); SetResourceReference(BorderBrushProperty, "ControlBorderBrush"); BorderThickness = new Thickness(.75);
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = .16 };
        Focusable = true;
        var body = new StackPanel(); Child = body;
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var close = new Button { Content = new Icon { Kind = IconKind.Close, Width = 13, Height = 13 }, Width = 28, Height = 32, Padding = new Thickness(6), Style = ControlStyle("QuietButton") };
        close.Click += (_, _) => CloseRequested?.Invoke(); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        System.Windows.Automation.AutomationProperties.SetName(close, "关闭画布比例");
        var title = new TextBlock { Text = "画布比例", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "FontSemi"); title.FontWeight = FontWeights.SemiBold; heading.Children.Add(title); body.Children.Add(heading);
        for (var column = 0; column < 5; column++) _presets.ColumnDefinitions.Add(new ColumnDefinition { Width = column % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(8) });
        _presets.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
        _presets.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        _presets.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
        Grid.SetColumnSpan(_selection, 5); Grid.SetRowSpan(_selection, 3); _presets.Children.Add(_selection);
        _selection.SetResourceReference(BorderBrushProperty, "ChoiceSelectedBorderBrush");
        for (var i = 0; i < Presets.Length; i++)
        {
            var index = i;
            var well = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(.75), IsHitTestVisible = false };
            well.SetResourceReference(BackgroundProperty, "SegmentWellBrush"); well.SetResourceReference(BorderBrushProperty, "LineBrush");
            Grid.SetRow(well, i / 3 * 2); Grid.SetColumn(well, i % 3 * 2); Panel.SetZIndex(well, -1); _presets.Children.Add(well); _wells.Add(well);
            var button = new Button { Tag = Presets[i], Height = 68, Padding = new Thickness(0), Margin = new Thickness(0), FontSize = 12, Style = ControlStyle("QuietButton"), Content = PresetLabel(i), ToolTip = Titles[i] };
            System.Windows.Automation.AutomationProperties.SetName(button, "画布比例 " + Titles[i]);
            Grid.SetRow(button, i / 3 * 2); Grid.SetColumn(button, i % 3 * 2); Panel.SetZIndex(button, 1);
            button.Click += (_, _) => SelectPreset(index); _presetButtons.Add(button); _presets.Children.Add(button);
        }
        body.Children.Add(_presets);
        var landscape = Direction("横向", IconKind.Landscape, false); var portrait = Direction("竖向", IconKind.Portrait, true);
        _directions = new FluidSegments([landscape, portrait], 0);
        _orientation = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(4), Child = _directions };
        _orientation.SetResourceReference(BackgroundProperty, "SegmentWellBrush");
        var customRow = new Grid(); customRow.ColumnDefinitions.Add(new ColumnDefinition()); customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var fields = new Grid(); fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.Children.Add(_width); var colon = new TextBlock { Text = ":", Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(colon, 1); fields.Children.Add(colon); Grid.SetColumn(_height, 2); fields.Children.Add(_height);
        var fieldWell = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(.75), Child = fields };
        fieldWell.SetResourceReference(BackgroundProperty, "SegmentWellBrush"); fieldWell.SetResourceReference(BorderBrushProperty, "ControlBorderBrush"); customRow.Children.Add(fieldWell);
        _applyCustom.Style = ControlStyle("QuietButton"); _applyCustom.Tag = "applyCustom"; _applyCustom.Margin = new Thickness(8, 0, 0, 0); _applyCustom.Padding = new Thickness(12, 4, 12, 4); Grid.SetColumn(_applyCustom, 1); customRow.Children.Add(_applyCustom); _custom.Children.Add(customRow);
        var help = new TextBlock { Text = "输入宽高比例，例如 3:2（支持 1:10 至 10:1）", FontSize = 11, Margin = new Thickness(0, 12, 0, 0) };
        help.SetResourceReference(TextBlock.ForegroundProperty, "InkSecondaryBrush"); _custom.Children.Add(help);
        _width.TextChanged += (_, _) => _applyCustom.IsEnabled = CustomValue() is not null;
        _height.TextChanged += (_, _) => _applyCustom.IsEnabled = CustomValue() is not null;
        _applyCustom.Click += (_, _) => ApplyCustom();
        _details.Children.Add(_orientation); _details.Children.Add(_custom);
        var detailHost = new Grid { ClipToBounds = true }; _detailClip.Child = _details; detailHost.Children.Add(_detailClip); detailHost.Children.Add(_detailLayer); body.Children.Add(detailHost);
        Loaded += (_, _) => { Paint(false); _positioned = true; };
        SizeChanged += (_, e) => { if (e.WidthChanged) Position(false); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CloseRequested?.Invoke(); e.Handled = true; } };
        Prepare();
    }

    static Style ControlStyle(string key) => (Style)Application.Current.FindResource(key);
    static TextBox Field(string name)
    {
        var field = new TextBox { FontSize = 13, MinHeight = 32, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        field.SetResourceReference(Control.FontFamilyProperty, "FontMedium"); field.FontWeight = FontWeights.Medium; System.Windows.Automation.AutomationProperties.SetName(field, name); return field;
    }

    public static bool IsCustom(CanvasSettings canvas) => canvas.AspectIsCustom || !new[] { "source", "适应", "16:9", "4:3", "9:16", "3:4", "1:1", "2.39:1", "1:2.39" }.Contains(canvas.Aspect);
    static bool Portrait(CanvasSettings canvas)
    {
        var pair = canvas.Aspect.Split(':');
        return pair.Length == 2 && double.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height) && width < height;
    }
    static string Preset(CanvasSettings canvas) => IsCustom(canvas) ? "custom" : canvas.Aspect switch { "适应" => "source", "9:16" => "16:9", "3:4" => "4:3", "1:2.39" => "2.39:1", _ => canvas.Aspect };

    public void Prepare()
    {
        var canvas = _canvas(); _editingCustom = IsCustom(canvas);
        var pair = canvas.Aspect.Split(':');
        _width.Text = !canvas.AspectIsCustom && IsCustom(canvas) && pair.Length == 2 ? pair[0] : canvas.CustomAspectWidth.ToString("0.###", CultureInfo.InvariantCulture);
        _height.Text = !canvas.AspectIsCustom && IsCustom(canvas) && pair.Length == 2 ? pair[1] : canvas.CustomAspectHeight.ToString("0.###", CultureInfo.InvariantCulture);
        _aspectAtSync = canvas.Aspect; _customAtSync = canvas.AspectIsCustom; Paint(false);
    }

    public void RefreshFromCanvas()
    {
        var canvas = _canvas();
        if (_aspectAtSync != canvas.Aspect || _customAtSync != canvas.AspectIsCustom) _editingCustom = IsCustom(canvas);
        _aspectAtSync = canvas.Aspect; _customAtSync = canvas.AspectIsCustom; Paint(_positioned);
    }

    UIElement PresetLabel(int index)
    {
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var glyph = new Grid { Height = 24 };
        if (index is 0 or 5) glyph.Children.Add(new Icon { Kind = index == 0 ? IconKind.FitArrows : IconKind.Sliders, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        else
        {
            var aspect = index switch { 1 => 16d / 9, 2 => 4d / 3, 3 => 2.39, _ => 1 };
            var shape = new System.Windows.Shapes.Rectangle { Width = Math.Min(24, 32 / aspect) * aspect, Height = Math.Min(24, 32 / aspect), RadiusX = 3, RadiusY = 3, StrokeThickness = 1.3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            shape.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) }); glyph.Children.Add(shape);
        }
        content.Children.Add(glyph);
        var label = new TextBlock { Text = Titles[index], FontSize = 12, Margin = new Thickness(0, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        label.SetBinding(TextBlock.FontFamilyProperty, new System.Windows.Data.Binding("FontFamily") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) }); content.Children.Add(label); return content;
    }

    Button Direction(string title, IconKind kind, bool portrait)
    {
        var button = new Button { Style = ControlStyle("SegmentButton"), Tag = portrait ? "portrait" : "landscape", FontSize = 12, Margin = new Thickness(2, 0, 2, 0), Content = DirectionLabel(title, kind) };
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        button.Click += (_, _) => ApplyPreset(Preset(_canvas()), portrait); return button;
    }
    static StackPanel DirectionLabel(string title, IconKind kind)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon { Kind = kind, Width = 14, Height = 14, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = title, FontSize = 12 };
        label.SetBinding(TextBlock.FontFamilyProperty, new System.Windows.Data.Binding("FontFamily") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) }); row.Children.Add(label); return row;
    }

    void SelectPreset(int index)
    {
        if (index == 5) { _editingCustom = true; Paint(true); return; }
        ApplyPreset(Presets[index], Portrait(_canvas()));
    }
    void ApplyPreset(string preset, bool portrait)
    {
        _editingCustom = false; var updated = ScenePreset.Clone(_canvas());
        updated.Aspect = portrait ? preset switch { "16:9" => "9:16", "4:3" => "3:4", "2.39:1" => "1:2.39", _ => preset } : preset;
        updated.AspectIsCustom = false; _apply(updated); RefreshFromCanvas();
    }
    (double Width, double Height)? CustomValue()
    {
        if (!double.TryParse(_width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) || !double.TryParse(_height.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || width / height < .1 || width / height > 10) return null;
        return (width, height);
    }
    void ApplyCustom()
    {
        if (CustomValue() is not { } value) return;
        var updated = ScenePreset.Clone(_canvas()); updated.AspectIsCustom = true;
        updated.CustomAspectWidth = value.Width; updated.CustomAspectHeight = value.Height;
        updated.Aspect = value.Width.ToString(CultureInfo.InvariantCulture) + ":" + value.Height.ToString(CultureInfo.InvariantCulture);
        _apply(updated); RefreshFromCanvas();
    }

    void Paint(bool animate)
    {
        var selected = _editingCustom ? "custom" : Preset(_canvas());
        for (var i = 0; i < _presetButtons.Count; i++)
        {
            var active = Presets[i] == selected;
            _presetButtons[i].SetResourceReference(Control.FontFamilyProperty, active ? "FontMedium" : "FontRegular");
            _presetButtons[i].FontWeight = active ? FontWeights.Medium : FontWeights.Normal;
            _wells[i].Opacity = active ? 0 : 1;
            System.Windows.Automation.AutomationProperties.SetItemStatus(_presetButtons[i], active ? "已选中" : "");
        }
        Position(animate);
        var showOrientation = !_editingCustom && selected is not ("source" or "1:1");
        var detailsChanged = (_custom.Visibility == Visibility.Visible) != _editingCustom || (_orientation.Visibility == Visibility.Visible) != showOrientation;
        var finish = detailsChanged && animate ? FluidMotion.PrepareSwap(_details, _detailLayer) : () => { };
        _custom.Visibility = _editingCustom ? Visibility.Visible : Visibility.Collapsed;
        _orientation.Visibility = showOrientation ? Visibility.Visible : Visibility.Collapsed;
        _directions.Select(Portrait(_canvas()) ? 1 : 0, animate);
        _details.Measure(new Size(Math.Max(1, Width - Padding.Left - Padding.Right - 1.5), double.PositiveInfinity));
        var desired = !_editingCustom && !showOrientation ? 0 : _details.DesiredSize.Height;
        // Commit also refreshes the toolbar. Repeating the same state must not
        // cancel an in-flight disclosure and expose its final height immediately.
        if (detailsChanged || _detailHeight != desired)
        {
            var from = _detailClip.IsLoaded ? _detailClip.ActualHeight : _detailHeight;
            _detailClip.BeginAnimation(FrameworkElement.HeightProperty, null); _detailClip.Height = desired; _detailHeight = desired;
            if (animate && detailsChanged && FluidMotion.Enabled)
                _detailClip.BeginAnimation(FrameworkElement.HeightProperty, new System.Windows.Media.Animation.DoubleAnimation(from, desired, TimeSpan.FromSeconds(.38))
                    { EasingFunction = new FluidSpring { Seconds = .38, EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
        }
        finish();
    }
    void Position(bool animate)
    {
        var key = _editingCustom ? "custom" : Preset(_canvas()); var index = Array.IndexOf(Presets, key);
        if (index >= 0) FluidMotion.Select(_selection, _presetButtons[index], _presets, animate);
    }
}
