using System.Globalization;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DogSC.Core;
using Path = System.IO.Path;

namespace DogSC;

public partial class EditorWindow
{
    string _canvasTab = "background", _backgroundTab = "wallpaper";
    string _zoomTransitionPhase = "enter";
    string _motionMode = "zoom";
    readonly Dictionary<string, FluidSegments> _segmentControls = new();
    DispatcherTimer? _cursorPreviewTimer;
    readonly List<(FrameworkElement Image, TranslateTransform Offset)> _patternLayers = [];
    (string? Wallpaper, double Width, double Height, double Scale, double Opacity)? _patternPreviewKey;
    (string Title, string Scope)? _inspectorHeading;

    void InspectorHeader(string title, string scope)
    {
        if (_inspectorHeading == (title, scope)) return;
        var transition = FluidMotion.PrepareSwap(InspectorHeaderContent, InspectorHeaderTransitionLayer, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var heading = Heading(title); heading.Margin = new Thickness(0); heading.VerticalAlignment = VerticalAlignment.Center;
        heading.FontSize = 16;
        row.Children.Add(heading);
        var tagText = new TextBlock { Text = scope, FontSize = 11, FontWeight = FontWeights.Medium };
        tagText.SetResourceReference(TextBlock.ForegroundProperty, "InkSecondaryBrush");
        tagText.SetResourceReference(TextBlock.FontFamilyProperty, "FontMedium");
        var tag = new Border { CornerRadius = new CornerRadius(12), MinHeight = 23, Padding = new Thickness(7, 4, 7, 4),
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = tagText };
        tag.SetResourceReference(Border.BackgroundProperty, "ValueWellBrush"); row.Children.Add(tag);
        InspectorHeaderContent.Content = row;
        _inspectorHeading = (title, scope);
        transition();
    }

    static StackPanel IconLabel(IconKind kind, string title)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new Icon { Kind = kind, Width = 14, Height = 14 });
        var label = new TextBlock { Text = title, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Control.Foreground))
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1) });
        label.SetBinding(TextBlock.FontFamilyProperty, new System.Windows.Data.Binding(nameof(Control.FontFamily))
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1) });
        content.Children.Add(label);
        return content;
    }

    UIElement Segments(IEnumerable<(string Title, string Key)> options, string selected, Action<string> choose, bool icons = false)
    {
        var choices = options.ToArray();
        var groupKey = string.Join("|", choices.Select(x => x.Key));
        var selectedIndex = Array.FindIndex(choices, x => x.Key == selected);
        var buttons = new List<Button>();
        foreach (var (title, key) in choices)
        {
            var button = new Button { Content = title, Style = (Style)FindResource("SegmentButton") };
            if (key is "converge" or "sideSlide" or "light3D" or "smooth" or "clear" or "gentle") button.Content = IconLabel(key switch { "converge" => IconKind.Converge, "sideSlide" => IconKind.SideSlide, "light3D" => IconKind.Cube, "smooth" => IconKind.MotionWave, "clear" => IconKind.Bolt, _ => IconKind.Wind }, title);
            if (key is "zoom" or "screen3d") button.Content = IconLabel(key == "zoom" ? IconKind.Scope : IconKind.Cube, title);
            if (icons)
                button.Content = IconLabel(key switch
                {
                    "background" => IconKind.Layers, "layout" => IconKind.InsetRectangleFilled, "frame" => IconKind.MacWindow,
                    "wallpaper" => IconKind.Photo, "grid" => IconKind.GridPattern,
                    "motion" => IconKind.WaveformRectangle, _ => IconKind.PhotoPlus,
                }, title);
            if (choices.Length == 4 && icons)
            {
                // The English source names need the same compact treatment as
                // the reference's four-way background control.
                button.FontSize = 11; button.Padding = new Thickness(2, 6, 2, 6);
                if (button.Content is StackPanel compact)
                {
                    ((Icon)compact.Children[0]).Width = ((Icon)compact.Children[0]).Height = 11;
                    ((TextBlock)compact.Children[1]).Margin = new Thickness(2, 0, 0, 0);
                }
            }
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            ControlAppearance.SelectSegment(button, selected == key);
            button.Click += (_, _) => choose(key);
            buttons.Add(button);
        }
        var grid = ReuseSegments(groupKey, buttons, selectedIndex);
        var border = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(4), Child = grid, BorderThickness = new Thickness(.75) };
        border.SetResourceReference(Border.BackgroundProperty, "SegmentWellBrush"); border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        return border;
    }

    FluidSegments ReuseSegments(string key, IReadOnlyList<Button> buttons, int selected)
    {
        if (!_segmentControls.TryGetValue(key, out var segments))
            return _segmentControls[key] = new FluidSegments(buttons, selected);
        // Keep the travelling indicator and its current clock when inspector content
        // is rebuilt. Replace callbacks so they still target the current clip.
        if (segments.Parent is Border previous) previous.Child = null;
        segments.Refresh(buttons, selected);
        return segments;
    }

    UIElement ValueSlider(string title, double min, double max, double value, Action<double> set,
        Func<double, string>? format = null, Action<ProjectDocument>? cancelOtherChanges = null)
    {
        format ??= v => v.ToString("0.##", CultureInfo.InvariantCulture);
        var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 3) };
        Slider? slider = null; var synchronizing = false;
        var pill = Parameter(title, min, max, value, v => { set(v); SyncParameterSlider(slider!, v, ref synchronizing); }, format, cancelOtherChanges);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) }; DockPanel.SetDock(pill, Dock.Right); row.Children.Add(pill);
        row.Children.Add(new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center }); panel.Children.Add(row);
        slider = Slider(min, max, value, v => { set(v); pill.Value = v; }, suppress: () => synchronizing);
        System.Windows.Automation.AutomationProperties.SetName(slider, title);
        panel.Children.Add(slider); return panel;
    }

    static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    static string Times(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "×";
    static string Seconds(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + L.Text(" 秒");

    void BuildCanvasInspector()
    {
        if (_cameraSelected) { BuildCameraInspector(); return; }
        InspectorHeader(_canvasTab == "background" ? L.Text("画布") : L.Text("屏幕"), _canvasTab == "background" ? L.Text("整个项目") : L.Text("基础布局"));
        InspectorNavigationHost.Visibility = Visibility.Visible;
        InspectorNavigation.Content = Segments([(L.Text("背景"), "background"), (L.Text("布局"), "layout"), (L.Text("外框"), "frame")], _canvasTab,
            key => { if (_canvasTab == key) return; _canvasTab = key; RebuildInspector(); InspectorScroll.ScrollToTop(); }, icons: true);
        switch (_canvasTab)
        {
            case "background": BuildBackgroundInspector(); break;
            case "frame": BuildFrameInspector(); break;
            default: BuildCanvasLayoutControls(); break;
        }
    }

    void BuildBackgroundInspector()
    {
        var body = new StackPanel();
        var layer = new Grid { IsHitTestVisible = false };
        var group = new Grid { ClipToBounds = true };
        group.Children.Add(body); group.Children.Add(layer);
        Inspector.Children.Add(SectionTitle(L.Text("背景来源")));
        Inspector.Children.Add(Segments([(L.Text("壁纸"), "wallpaper"), (L.Text("网格"), "grid"), (L.Text("动态"), "motion"), (L.Text("自定义"), "custom")],
            _backgroundTab, key =>
            {
                if (_backgroundTab == key) return;
                _activeParameterReadout?.FinishEditing();
                var transition = FluidMotion.PrepareSwap(body, layer, 8);
                _backgroundTab = key;
                _segmentControls["wallpaper|grid|motion|custom"].Select(Array.IndexOf(new[] { "wallpaper", "grid", "motion", "custom" }, key));
                body.Children.Clear(); BuildBackgroundContent(body); transition();
            }, icons: true));
        Inspector.Children.Add(group);
        BuildBackgroundContent(body);
    }

    void BuildBackgroundContent(Panel body)
    {
        var canvas = _project.Canvas;
        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(-4, 10, -4, 4) };
        if (_backgroundTab is "grid" or "motion")
        {
            var options = _backgroundTab == "grid"
                ? new[] { (L.Text("黑曜石网格"), "pattern:obsidianGrid"), (L.Text("工程白网格"), "pattern:engineeringWhiteGrid"),
                    (L.Text("暗夜星阵"), "pattern:midnightDots"), (L.Text("建筑极简点"), "pattern:architecturalDots"), (L.Text("立体等角网"), "pattern:isometricMesh") }
                : new[] { (L.Text("赛博微流网格"), "flow:cyberDriftGrid"), (L.Text("星辉漫步点阵"), "flow:starfieldDots"), (L.Text("极光流体星云"), "flow:auroraFluid") };
            foreach (var (name, key) in options)
            {
                var bake = PatternLibrary.Resolve(key, 150, 112, canvas.PatternScale, canvas.PatternOpacity)!;
                BackgroundTile(tiles, name, key, PatternArt.Preview(bake, 150, 112));
            }
            body.Children.Add(tiles);
            body.Children.Add(ValueSlider(L.Text("图案大小"), .4, 6, canvas.PatternScale, v => canvas.PatternScale = v, Times));
            body.Children.Add(ValueSlider(L.Text("不透明度"), 0, 1, canvas.PatternOpacity, v => canvas.PatternOpacity = v, Percent));
        }
        else if (_backgroundTab == "wallpaper")
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Wallpaper");
            if (Directory.Exists(folder))
                foreach (var path in Directory.EnumerateFiles(folder, "*.jpg", SearchOption.AllDirectories).Take(12))
                    BackgroundTile(tiles, Path.GetFileNameWithoutExtension(path), path, LoadInspectorImage(path));
            body.Children.Add(tiles);
            body.Children.Add(DecorationButton(L.Text("选择壁纸图片或视频…"), () => PickBackgroundMedia()));
            body.Children.Add(ValueSlider(L.Text("壁纸模糊"), 0, 1, canvas.WallpaperBlur, v => canvas.WallpaperBlur = v, Percent));
        }
        else
        {
            body.Children.Add(DecorationButton(L.Text("选择图片或视频…"), () => PickBackgroundMedia()));
            body.Children.Add(Caption(L.Text("图片作为静态背景；MP4 / MOV 视频循环播放并参与导出。")));
            var colors = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
            foreach (var color in new[] { "#F3F1EC", "#F7F7F5", "#D9E4DD", "#1E1F22", "#2C3A4A", "#000000" })
            {
                var swatch = new Button { Width = 28, Height = 28, Background = Brush(color), Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 7, 7), ToolTip = color };
                swatch.Click += (_, _) => { Push(); canvas.Background = color; canvas.Wallpaper = null; Commit(); };
                colors.Children.Add(swatch);
            }
            body.Children.Add(colors);
            ColorEntry(body, L.Text("背景颜色"), canvas.Background, v => { canvas.Background = v; canvas.Wallpaper = null; });
            body.Children.Add(ValueSlider(L.Text("背景模糊"), 0, 1, canvas.WallpaperBlur, v => canvas.WallpaperBlur = v, Percent));
        }
        if (canvas.Wallpaper is not null && _backgroundTab is "wallpaper" or "custom")
            body.Children.Add(DecorationButton(L.Text("清除背景素材"), () => { Push(); canvas.Wallpaper = null; Commit(); }));
    }

    static ImageSource LoadInspectorImage(string path)
    {
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 200; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }

    void BackgroundTile(Panel tiles, string name, string key, ImageSource source)
    {
        var grid = new Grid { Height = 104, ClipToBounds = true };
        grid.Children.Add(new Image { Source = source, Stretch = Stretch.UniformToFill });
        var labelText = new TextBlock { Text = name, Foreground = Brushes.White, FontSize = key.StartsWith("flow:") ? 12 : 11,
            FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
        labelText.SetResourceReference(TextBlock.FontFamilyProperty, "FontMedium");
        var labelContent = new StackPanel { Orientation = Orientation.Horizontal };
        if (key.StartsWith("flow:")) labelContent.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        labelContent.Children.Add(labelText);
        var label = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(18), Margin = new Thickness(6),
            Padding = new Thickness(8, 5, 8, 5), Child = labelContent };
        grid.Children.Add(label);
        var selected = _project.Canvas.Wallpaper == key;
        var outline = new Rectangle { RadiusX = 9, RadiusY = 9, StrokeThickness = selected ? 2 : 1, IsHitTestVisible = false };
        outline.SetResourceReference(Shape.StrokeProperty, selected ? "AccentBrush" : "LineBrush");
        grid.Children.Add(outline);
        grid.SizeChanged += (_, _) =>
        {
            grid.Height = grid.ActualWidth;
            grid.Clip = new RectangleGeometry(new Rect(0, 0, grid.ActualWidth, grid.ActualHeight), 9, 9);
            label.MaxWidth = Math.Max(0, grid.ActualWidth - 12);
            labelText.MaxWidth = Math.Max(0, grid.ActualWidth - (key.StartsWith("flow:") ? 37 : 28));
        };
        var button = new Button { Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), Margin = new Thickness(4),
            Style = (Style)FindResource("QuietButton"), ToolTip = name, BorderThickness = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        System.Windows.Automation.AutomationProperties.SetItemStatus(button, selected ? L.Text("已选中") : "");
        button.Click += (_, _) => { var transition = FluidMotion.PrepareSwap(PreviewCanvas, PreviewTransitionLayer, 0); Push(); _project.Canvas.Wallpaper = key; Commit(); transition(); };
        tiles.Children.Add(button);
    }

    void PickBackgroundMedia()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = L.Text("选择背景图片或视频"), Filter = L.Text("图片或视频|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.mp4;*.mov;*.mkv;*.webm|所有文件|*.*") };
        if (dialog.ShowDialog(this) != true) return;
        var folder = Path.Combine(_directory, "media", "decor"); Directory.CreateDirectory(folder);
        var name = Guid.NewGuid().ToString("N") + Path.GetExtension(dialog.FileName);
        File.Copy(dialog.FileName, Path.Combine(folder, name));
        Push(); _project.Canvas.Wallpaper = "media/decor/" + name; Commit();
    }

    void ColorEntry(Panel panel, string title, string value, Action<string> set)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
        var box = new TextBox { Text = value, Width = 95 };
        System.Windows.Automation.AutomationProperties.SetName(box, title);
        var apply = DecorationButton(L.Text("应用"), () =>
        {
            try { ColorConverter.ConvertFromString(box.Text); }
            catch (FormatException) { box.ToolTip = L.Text("请输入颜色，例如 #528AE8"); return; }
            Push(); set(box.Text); Commit();
        });
        DockPanel.SetDock(apply, Dock.Right); row.Children.Add(apply); row.Children.Add(box); panel.Children.Add(row);
    }

    void BuildCanvasLayoutControls()
    {
        var canvas = _project.Canvas;
        Inspector.Children.Add(SectionTitle(L.Text("画布间距")));
        Inspector.Children.Add(ValueSlider(L.Text("边距"), 0, 180, canvas.Padding, v => canvas.Padding = v));
        Inspector.Children.Add(SectionTitle(L.Text("位置与大小")));
        Inspector.Children.Add(PositionPad());
        Inspector.Children.Add(ValueSlider(L.Text("内容缩放"), .25, 2.5, canvas.Scale, v => canvas.Scale = v, Times));
        Inspector.Children.Add(PadHeading(L.Text("可直接在预览中拖动"), L.Text("重置位置与大小"), () => { Push(); canvas.CenterX = canvas.CenterY = .5; canvas.Scale = 1; Commit(); }));
        Inspector.Children.Add(SectionTitle(L.Text("圆角、描边与阴影")));
        Inspector.Children.Add(ValueSlider(L.Text("屏幕圆角"), 0, 90, canvas.Corner, v => canvas.Corner = v));
        Inspector.Children.Add(ValueSlider(L.Text("外侧描边"), 0, 40, canvas.ScreenBorderWidth, v => canvas.ScreenBorderWidth = v));
        Inspector.Children.Add(ValueSlider(L.Text("阴影"), 0, 100, canvas.Shadow, v => canvas.Shadow = v));
        Inspector.Children.Add(ValueSlider(L.Text("左右倾斜"), -40, 40, canvas.TiltY, v => canvas.TiltY = v, v => v.ToString("0") + "°"));
        Inspector.Children.Add(ValueSlider(L.Text("上下倾斜"), -40, 40, canvas.TiltX, v => canvas.TiltX = v, v => v.ToString("0") + "°"));
        Inspector.Children.Add(ValueSlider(L.Text("边缘虚化"), 0, 1, canvas.FocusBlur, v => canvas.FocusBlur = v, Percent));
    }

    void BuildFrameInspector()
    {
        var canvas = _project.Canvas;
        Inspector.Children.Add(new TextBlock { Text = L.Text("选择外框样式，圆角、描边和阴影可在布局中调整。"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("InkSecondaryBrush"), Margin = new Thickness(0, 12, 0, 4) });
        Inspector.Children.Add(SectionTitle(L.Text("外框")));
        var choices = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 8) };
        foreach (var (title, style) in new[] { (L.Text("无"), "none"), (L.Text("浅色窗口"), "windowLight"), (L.Text("深色窗口"), "windowDark"),
            (L.Text("浅色浏览器"), "browserLight"), (L.Text("深色浏览器"), "browserDark"), (L.Text("设备"), "phone") })
        {
            var tile = new StackPanel();
            var preview = new Canvas { Width = 80, Height = 80, ClipToBounds = true };
            preview.SetResourceReference(Panel.BackgroundProperty, "ControlBrush");
            var settings = new CanvasSettings(); ScreenFrameStyles.ApplyDefaults(settings, style);
            var frame = new FramePlacement(12, 30, 136, 98, 10, 0, 0, 0);
            preview.LayoutTransform = new ScaleTransform(.5, .5); preview.Width = 160; preview.Height = 160;
            var image = new Rectangle { Width = frame.Width, Height = frame.Height, RadiusX = 6, RadiusY = 6,
                Fill = new DrawingBrush(_player.ScreenDrawing) { Stretch = Stretch.UniformToFill } };
            Canvas.SetLeft(image, frame.X); Canvas.SetTop(image, frame.Y); preview.Children.Add(image);
            if (ChromeGeometry.Resolve(settings, frame, 160, 160) is { } spec)
                preview.Children.Add(new Image { Width = 160, Height = 160, Source = ChromeArt.Preview(spec, 160, 160) });
            var thumbnail = new Border { Child = preview, CornerRadius = new CornerRadius(12), Padding = new Thickness(5), Background = (Brush)FindResource("SegmentWellBrush"), BorderThickness = new Thickness(.75), BorderBrush = (Brush)FindResource("LineBrush") };
            var selected = canvas.ScreenFrame == style || style == "phone" && canvas.ScreenFrame == "tablet";
            if (selected) thumbnail.BorderBrush = (Brush)FindResource("InkBrush");
            tile.Children.Add(thumbnail); tile.Children.Add(new TextBlock { Text = title, FontSize = 10, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
            var button = new Button { Content = tile, Padding = new Thickness(0), BorderThickness = new Thickness(0), Margin = new Thickness(3, 3, 3, 8), Style = (Style)FindResource("QuietButton") };
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            button.Click += (_, _) => { if (canvas.ScreenFrame == style) return; Push(); ScreenFrameStyles.ApplyDefaults(canvas, style); Commit(); }; choices.Children.Add(button);
        }
        Inspector.Children.Add(choices);
        var reset = PadHeading("", L.Text("还原样式"), () => { Push(); ScreenFrameStyles.ApplyDefaults(canvas, canvas.ScreenFrame); Commit(); }, IconKind.Counterclockwise);
        ((Button)reset.Children[0]).ToolTip = L.Text("恢复当前样机的默认外观，保留标题和网址");
        Inspector.Children.Add(reset);
        if (canvas.ScreenFrame is "phone" or "tablet")
            Inspector.Children.Add(Segments([(L.Text("手机"), "phone"), (L.Text("平板"), "tablet")], canvas.ScreenFrame,
                key => { Push(); ScreenFrameStyles.ApplyDefaults(canvas, key); Commit(); }));
        if (canvas.ScreenFrame == "none") return;
        if (canvas.ScreenFrame.StartsWith("window")) TextEntry(L.Text("窗口标题"), canvas.ScreenFrameTitle, v => canvas.ScreenFrameTitle = v);
        Inspector.Children.Add(ValueSlider(L.Text("外框缩放"), .6, 1.6, canvas.ScreenFrameScale, v => canvas.ScreenFrameScale = v, Times));
        Inspector.Children.Add(ValueSlider(L.Text("顶栏缩放"), .65, 1.6, canvas.ScreenFrameToolbarScale, v => canvas.ScreenFrameToolbarScale = v, Times));
        if (canvas.ScreenFrame.StartsWith("browser")) TextEntry(L.Text("浏览器地址"), canvas.ScreenFrameAddress, v => canvas.ScreenFrameAddress = v);
    }

    void TextEntry(string title, string value, Action<string> set)
    {
        Inspector.Children.Add(Caption(title)); var box = new TextBox { Text = value };
        System.Windows.Automation.AutomationProperties.SetName(box, title);
        box.LostKeyboardFocus += (_, _) => { if (box.Text == value) return; Push(); set(box.Text); Commit(rebuild: false); };
        Inspector.Children.Add(box);
    }

    void BuildOpeningInspector()
    {
        InspectorHeader(L.Text("开场"), L.Text("整个项目"));
        var canvas = _project.Canvas;
        AddToggle(Inspector, L.Text("全局开场"), canvas.Opening != "none", on => { canvas.Opening = on ? "light3D" : "none"; RebuildInspector(); });
        var openingToggle = Inspector.Children.OfType<CheckBox>().Last(); openingToggle.Content = IconLabel(IconKind.OpeningStack, L.Text("全局开场"));
        openingToggle.FontFamily = (FontFamily)FindResource("FontSemi"); openingToggle.FontWeight = FontWeights.SemiBold;
        if (canvas.Opening == "none") return;
        Inspector.Children.Add(Caption(L.Text("让画面中的元素按顺序进入")));
        Inspector.Children.Add(Caption(L.Text("开场样式")));
        Inspector.Children.Add(Segments([(L.Text("聚拢"), "converge"), (L.Text("侧滑"), "sideSlide"), (L.Text("轻 3D"), "light3D")], canvas.Opening,
            key => { Push(); canvas.Opening = key; Commit(); }));
        Inspector.Children.Add(Caption(L.Text("动效节奏")));
        Inspector.Children.Add(Segments([(L.Text("平滑"), "smooth"), (L.Text("干脆"), "clear"), (L.Text("轻柔"), "gentle")], canvas.OpeningTiming,
            key => { Push(); canvas.OpeningTiming = key; Commit(); }));
        Inspector.Children.Add(new TextBlock { Text = canvas.OpeningTiming switch { "clear" => L.Text("进入与结束更利落，适合节奏紧凑的片段。"), "gentle" => L.Text("柔和地加速与减速，适合舒缓的片段。"), _ => L.Text("均衡的加速与减速，适合大多数元素。") }, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = (Brush)FindResource("InkSecondaryBrush") });
        Inspector.Children.Add(ValueSlider(L.Text("总时长"), .4, 8, canvas.OpeningDuration, v => canvas.OpeningDuration = v, Seconds));
        Inspector.Children.Add(ValueSlider(L.Text("元素间隔"), 0, 1.2, canvas.OpeningInterval, v => canvas.OpeningInterval = v, Seconds));
        var elements = Disclosure(L.Text("参与元素与顺序"), () => L.Format("{0} 个参与元素", canvas.OpeningIncluded.Count));
        foreach (var (element, index) in canvas.OpeningOrder.Select((v, i) => (v, i)))
        {
            var row = new DockPanel();
            var name = element switch { "screen" => L.Text("屏幕"), "camera" => L.Text("摄像头"), _ => L.Text("贴图") };
            var include = new CheckBox { Content = $"{index + 1}  {name}", IsChecked = canvas.OpeningIncluded.Contains(element),
                Style = (Style)FindResource("SwitchCheckBox"), VerticalAlignment = VerticalAlignment.Center };
            include.Click += (_, _) => { Push(); canvas.OpeningIncluded.Remove(element); if (include.IsChecked == true) canvas.OpeningIncluded.Add(element); Commit(); };
            foreach (var (title, delta) in new[] { ("↑", -1), ("↓", 1) })
            {
                var button = DecorationButton(title, () => { var target = index + delta; Push(); canvas.OpeningOrder.RemoveAt(index); canvas.OpeningOrder.Insert(target, element); Commit(); });
                button.IsEnabled = index + delta >= 0 && index + delta < canvas.OpeningOrder.Count;
                button.ToolTip = delta < 0 ? L.Text("提前进入") : L.Text("延后进入");
                DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
            }
            row.Children.Add(include); elements.Children.Add(row);
        }
        Inspector.Children.Add(DecorationButton(L.Text("从片头预览"), () => _player.Seek(0, true)));
        Inspector.Children.Add(Caption(L.Text("从 0 秒开始的贴图由全局开场接管入场；柔化与聚光按各自时段生效。")));
    }

    void BuildCursorInspector()
    {
        InspectorHeader(L.Text("光标"), L.Text("整个项目"));
        var fx = _project.Canvas.Cursor;
        BuildCursorEffectPreview();
        Inspector.Children.Add(Segments([(L.Text("系统"), "pointer"), (L.Text("触点"), "dot"), (L.Text("隐藏"), "hidden")], fx.Mode,
            key => { Push(); fx.Mode = key; fx.Asset = key switch { "dot" => "system.touch-dot", "hidden" => "system.hidden", _ => "auto" }; Commit(); }));
        Inspector.Children.Add(ValueSlider(L.Text("大小"), .5, 4, fx.Size, v => fx.Size = v, Times));
        AddToggle(Inspector, L.Text("静止时隐藏"), fx.IdleHide, v => { fx.IdleHide = v; RebuildInspector(); });
        if (fx.IdleHide) Inspector.Children.Add(ValueSlider(L.Text("隐藏延迟"), .1, 10, fx.IdleDelay, v => fx.IdleDelay = v, Seconds));
        var timing = Disclosure(L.Text("时间校准"), () => $"{fx.TimingAdvance * 1000:+0;-0;0} ms"); AddTimingControls(timing, () => fx.TimingAdvance, v => fx.TimingAdvance = v, 1);
        Inspector.Children.Add(Caption(L.Text("点击反馈")));
        Inspector.Children.Add(ClickEffectSegments([(L.Text("涟漪"), "ripple"), (L.Text("光晕"), "glow"), (L.Text("双环"), "pulse"), (L.Text("微闪"), "flash"), (L.Text("无"), "none")],
            CursorCatalog.ResolveClick(fx), key => { Push(); fx.ClickStyle = key; fx.ClickRipple = key != "none"; Commit(); }));
        var colorRow = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var colors = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(colors, Dock.Right); colorRow.Children.Add(colors);
        colorRow.Children.Add(new TextBlock { Text = L.Text("颜色"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var resetColor = new Button { Content = new Icon { Kind = IconKind.Undo, Width = 12, Height = 12 }, Width = 19, Height = 19, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), ToolTip = L.Text("重置点击颜色") };
        resetColor.Click += (_, _) => { Push(); fx.ClickColor = new CursorFx().ClickColor; Commit(); };
        colors.Children.Add(resetColor);
        foreach (var color in new[] { "#7C5CFC", "#19C3EE", "#36A87E", "#F3BC48", "#E56060", "#FFFFFF" })
        {
            var button = new Button { Background = Brush(color), Width = 19, Height = 19, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), ToolTip = color };
            button.Click += (_, _) => { Push(); fx.ClickColor = color; Commit(); }; colors.Children.Add(button);
        }
        var customColor = new Button { Content = new Icon { Kind = IconKind.Plus, Width = 10, Height = 10 }, Width = 19, Height = 19, Padding = new Thickness(0), ToolTip = L.Text("自定义点击颜色") };
        var colorPopup = new System.Windows.Controls.Primitives.Popup { PlacementTarget = customColor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var colorBody = new StackPanel(); ColorEntry(colorBody, L.Text("点击颜色"), fx.ClickColor, v => fx.ClickColor = v);
        colorPopup.Child = new Border { Child = colorBody, Background = (Brush)FindResource("CardBrush"), Padding = new Thickness(12), CornerRadius = new CornerRadius(10) };
        customColor.Click += (_, _) => colorPopup.IsOpen = true; colors.Children.Add(customColor); Inspector.Children.Add(colorRow);
        Inspector.Children.Add(ValueSlider(L.Text("不透明度"), 0, 1, fx.ClickOpacity, v => fx.ClickOpacity = v, Percent));
        Inspector.Children.Add(ValueSlider(L.Text("范围"), .5, 4, fx.ClickScale, v => fx.ClickScale = v, Times));
        Inspector.Children.Add(Caption(L.Text("运动平滑")));
        Inspector.Children.Add(CursorChipRow(CursorCatalog.Motions.Select(m => (L.Text(m.Name), m.Id)), fx.Motion, v => fx.Motion = v));
        Inspector.Children.Add(ValueSlider(L.Text("运动倾斜强度"), 0, 4, fx.MotionTilt, v => fx.MotionTilt = v));
        Inspector.Children.Add(Caption(L.Text("系统光标样式")));
        Inspector.Children.Add(CursorChipRow(CursorCatalog.Assets.Select(a => (L.Text(a.Name), a.Id)), CursorCatalog.ResolveAsset(fx), v =>
        {
            fx.Asset = v;
            fx.Mode = v switch { "system.hidden" => "hidden", "system.touch-dot" => "dot", _ => "pointer" };
        }));
    }

    void BuildCursorEffectPreview()
    {
        var preview = new Grid { Height = 108, Margin = new Thickness(0, 0, 0, 12), ClipToBounds = true };
        preview.SetResourceReference(Panel.BackgroundProperty, "ControlBrush");
        var pointer = new Image { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var click = new Image { Width = 86, Height = 86, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        preview.Children.Add(click); preview.Children.Add(pointer);
        preview.Children.Add(new TextBlock { Text = L.Text("效果预览"), FontSize = 10, Margin = new Thickness(8), Opacity = .5 });
        Inspector.Children.Add(new Border { CornerRadius = new CornerRadius(9), Child = preview, ClipToBounds = true });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Paint()
        {
            var fx = _project.Canvas.Cursor;
            var progress = clock.Elapsed.TotalSeconds % 2;
            var sprite = CursorArt.Render(CursorCatalog.ResolveAsset(fx) == "auto" ? "system.arrow" : CursorCatalog.ResolveAsset(fx), (int)(24 * fx.Size));
            pointer.Width = sprite?.Width ?? 24; pointer.Height = sprite?.Height ?? 24;
            pointer.Source = sprite is null ? null : BitmapSource.Create(sprite.Width, sprite.Height, 96, 96, PixelFormats.Bgra32, null, sprite.Bgra, sprite.Width * 4);
            click.Source = BitmapSource.Create(86, 86, 96, 96, PixelFormats.Bgra32, null,
                CursorCatalog.RenderClick(86, CursorCatalog.ResolveClick(fx), Math.Min(progress, 1), CursorCatalog.ParseColor(fx.ClickColor), fx.ClickOpacity, 34 * fx.ClickScale), 86 * 4);
            click.Opacity = progress < 1 ? 1 : 0;
        }
        Paint();
        var replay = new Button { Content = new Icon { Kind = IconKind.Redo, Width = 12, Height = 12 }, Width = 23, Height = 23, Padding = new Thickness(0), Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, ToolTip = L.Text("重播效果预览") };
        replay.Click += (_, _) => { clock.Restart(); Paint(); };
        preview.Children.Add(replay);
        _cursorPreviewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _cursorPreviewTimer.Tick += (_, _) => Paint();
        var timer = _cursorPreviewTimer;
        preview.Loaded += (_, _) => timer.Start(); preview.Unloaded += (_, _) => timer.Stop();
    }

    void BuildZoomTargetControls(ZoomSpan zoom)
    {
        var focusMode = Segments([(L.Text("自动跟随"), "automatic"), (L.Text("手动定位"), "manual")], zoom.Origin,
            key => { Push(); if (key == "manual") MakeZoomManual(zoom); else zoom.Origin = key; Commit(); });
        Inspector.Children.Add(focusMode);
        var pose = zoom.Origin == "automatic" ? _cursorTimeline!.Motion.TargetAt(zoom, _player.Time) : new ZoomPose(zoom.Scale, zoom.FocusX, zoom.FocusY);
        const double pw = 320, ph = 180;
        var pad = new Canvas { Width = pw, Height = ph, Clip = new RectangleGeometry(new Rect(0, 0, pw, ph), 10, 10) };
        pad.Children.Add(new Rectangle { Width = pw, Height = ph, Fill = new DrawingBrush(_player.ScreenDrawing) { Stretch = Stretch.UniformToFill } });
        var marker = new Ellipse { Width = 18, Height = 18, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1.5 };
        pad.Children.Add(marker);
        ParameterReadout? vx = null, vy = null;
        var displayedMode = zoom.Origin;
        void Paint(double px, double py)
        {
            Canvas.SetLeft(marker, px * pw - 9); Canvas.SetTop(marker, py * ph - 9);
            if (vx is not null) vx.Value = px; if (vy is not null) vy.Value = py;
            if (displayedMode != zoom.Origin)
            {
                displayedMode = zoom.Origin;
                var modes = (FluidSegments)((Border)focusMode).Child;
                var selectedMode = zoom.Origin == "automatic" ? 0 : 1;
                modes.Select(selectedMode);
                foreach (var (button, index) in modes.Buttons.Select((button, index) => (button, index)))
                    ControlAppearance.SelectSegment(button, index == selectedMode);
            }
        }
        var synchronizing = false;
        var x = Slider(0, 1, pose.X, v => { MakeZoomManual(zoom); zoom.FocusX = v; }, refresh: true, suppress: () => synchronizing);
        var y = Slider(0, 1, pose.Y, v => { MakeZoomManual(zoom); zoom.FocusY = v; }, refresh: true, suppress: () => synchronizing);
        void CancelFocus(ProjectDocument before)
        {
            var original = before.Zooms.First(z => z.Id == zoom.Id);
            zoom.Origin = original.Origin; zoom.FocusX = original.FocusX; zoom.FocusY = original.FocusY;
            var restoredPose = original.Origin == "automatic" ? _cursorTimeline!.Motion.TargetAt(original, _player.Time)
                : new ZoomPose(original.Scale, original.FocusX, original.FocusY);
            Paint(restoredPose.X, restoredPose.Y);
        }
        vx = Parameter(L.Text("缩放焦点 X 坐标"), 0, 1, pose.X, v => { MakeZoomManual(zoom); zoom.FocusX = v; SyncParameterSlider(x, v, ref synchronizing); Paint(zoom.FocusX, zoom.FocusY); }, Percent, CancelFocus);
        vy = Parameter(L.Text("缩放焦点 Y 坐标"), 0, 1, pose.Y, v => { MakeZoomManual(zoom); zoom.FocusY = v; SyncParameterSlider(y, v, ref synchronizing); Paint(zoom.FocusX, zoom.FocusY); }, Percent, CancelFocus);
        x.ValueChanged += (_, _) => Paint(x.Value, y.Value); y.ValueChanged += (_, _) => Paint(x.Value, y.Value);
        _zoomFocusSliders = (x, y); Paint(pose.X, pose.Y);
        void Move(Point point)
        {
            MakeZoomManual(zoom); zoom.FocusX = Math.Clamp(point.X / pw, 0, 1); zoom.FocusY = Math.Clamp(point.Y / ph, 0, 1);
            Paint(zoom.FocusX, zoom.FocusY); Commit(rebuild: false);
        }
        pad.MouseLeftButtonDown += (_, e) => { Push(); pad.CaptureMouse(); Move(e.GetPosition(pad)); e.Handled = true; };
        pad.MouseMove += (_, e) => { if (pad.IsMouseCaptured) Move(e.GetPosition(pad)); };
        pad.MouseLeftButtonUp += (_, _) => { pad.ReleaseMouseCapture(); FlushPendingSave(); RebuildInspector(); };
        Inspector.Children.Add(new Viewbox { Child = pad, Stretch = Stretch.Uniform, Margin = new Thickness(0, 12, 0, 4) });
        var coordinates = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var center = DecorationButton(L.Text("居中缩放焦点"), () => { Push(); MakeZoomManual(zoom); zoom.FocusX = zoom.FocusY = .5; Commit(); });
        center.Content = new Icon { Kind = IconKind.Scope, Width = 14, Height = 14 };
        center.Width = 32; center.Height = 30; center.Padding = new Thickness(0); center.ToolTip = L.Text("焦点居中");
        center.SetResourceReference(Control.BackgroundProperty, "ControlBrush");
        center.SetResourceReference(Control.BorderBrushProperty, "ControlBorderBrush"); center.BorderThickness = new Thickness(.75);
        System.Windows.Automation.AutomationProperties.SetName(center, L.Text("居中缩放焦点"));
        DockPanel.SetDock(center, Dock.Right); coordinates.Children.Add(center);
        var values = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 }; values.Children.Add(CoordinateValue("X", vx)); values.Children.Add(CoordinateValue("Y", vy)); coordinates.Children.Add(values);
        Inspector.Children.Add(coordinates);
        if (zoom.Origin == "automatic") Inspector.Children.Add(new TextBlock { Text = L.Text("录制光标位置 · 拖动焦点即可切换为手动定位"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = (Brush)FindResource("InkSecondaryBrush"), Margin = new Thickness(0, 6, 0, 2) });
        Inspector.Children.Add(SectionTitle(L.Text("运动类型")));
        Inspector.Children.Add(Segments([(L.Text("缩放"), "zoom"), (L.Text("屏幕 3D"), "screen3d")], "zoom", SelectMotionMode));
        Inspector.Children.Add(ValueSlider(L.Text("缩放倍数"), 1, 6, zoom.Scale, v => zoom.Scale = v, Times));
        Inspector.Children.Add(DecorationButton(L.Text("将倍数应用到所有缩放"), () => { Push(); foreach (var z in _project.Zooms) z.Scale = zoom.Scale; Commit(); }));
        Inspector.Children.Add(Caption(L.Text("过渡")));
        Inspector.Children.Add(Segments([(L.Text("进入"), "enter"), (L.Text("保持"), "hold"), (L.Text("退出"), "exit")], _zoomTransitionPhase,
            key => { _zoomTransitionPhase = key; RebuildInspector(); }));
        switch (_zoomTransitionPhase)
        {
            case "enter": Inspector.Children.Add(ValueSlider(L.Text("进入时长"), 0, 5, zoom.EnterDuration ?? .4, v => zoom.EnterDuration = v, Seconds,
                before => zoom.EnterDuration = before.Zooms.First(z => z.Id == zoom.Id).EnterDuration)); break;
            case "exit": Inspector.Children.Add(ValueSlider(L.Text("退出时长"), 0, 5, zoom.ExitDuration ?? .4, v => zoom.ExitDuration = v, Seconds,
                before => zoom.ExitDuration = before.Zooms.First(z => z.Id == zoom.Id).ExitDuration)); break;
            default:
                Inspector.Children.Add(ValueSlider(L.Text("片段总时长"), .12, Math.Max(.12, TimelineMath.Duration(_project) - zoom.Start), zoom.Duration,
                    v => { if (!TimelineEdits.TryMoveZoom(_project, zoom.Id, zoom.Start, v)) TimeLabel.Text = L.Text("时长与另一段缩放重叠。"); }, Seconds,
                    before => { var original = before.Zooms.First(z => z.Id == zoom.Id); zoom.Start = original.Start; zoom.Duration = original.Duration; })); break;
        }
    }

    void UpdateProceduralBackground(double width, double height)
    {
        var canvas = _project.Canvas;
        var key = (canvas.Wallpaper, width, height, canvas.PatternScale, canvas.PatternOpacity);
        if (_patternPreviewKey == key) return;
        _patternPreviewKey = key; ProceduralBackground.Children.Clear(); _patternLayers.Clear();
        ProceduralBackground.Width = width; ProceduralBackground.Height = height;
        var bake = PatternLibrary.Resolve(canvas.Wallpaper, (int)Math.Ceiling(width), (int)Math.Ceiling(height), canvas.PatternScale, canvas.PatternOpacity);
        ProceduralBackground.Visibility = bake is null ? Visibility.Collapsed : Visibility.Visible;
        if (bake is null) return;
        WallpaperImage.Visibility = Visibility.Collapsed;
        Color ColorOf(uint c) => Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
        Brush background = bake.BaseKind == "linear"
            ? new LinearGradientBrush(ColorOf(bake.BaseColorA), ColorOf(bake.BaseColorB), 45)
            : new SolidColorBrush(ColorOf(bake.BaseColorA));
        ProceduralBackground.Background = background;
        foreach (var layer in bake.Layers)
        {
            var offset = new TranslateTransform();
            var image = new Image { Source = PatternArt.Layer(layer), Width = layer.PngW, Height = layer.PngH, Stretch = Stretch.Fill, RenderTransform = offset };
            ProceduralBackground.Children.Add(image); _patternLayers.Add((image, offset));
        }
        UpdateProceduralMotion();
    }

    void UpdateProceduralMotion()
    {
        var wallpaper = _project.Canvas.Wallpaper;
        if (wallpaper?.StartsWith("flow:") != true) return;
        var time = _player.Time; var width = ProceduralBackground.Width; var height = ProceduralBackground.Height;
        for (var i = 0; i < _patternLayers.Count; i++)
        {
            var offset = _patternLayers[i].Offset;
            switch (wallpaper)
            {
                case "flow:cyberDriftGrid" when i == 0:
                    offset.X = (Math.Sin(time * .4) - 1) * width * .08;
                    offset.Y = (Math.Cos(time * .4) - 1) * height * .08; break;
                case "flow:cyberDriftGrid":
                case "flow:starfieldDots":
                    var dark = wallpaper == "flow:cyberDriftGrid";
                    var tile = Math.Max(Math.Min(width, height) * (dark ? .0425 : .032) * _project.Canvas.PatternScale, 9);
                    offset.X = time * tile * (dark ? .10 : .07) % tile - tile;
                    offset.Y = time * tile * (dark ? .065 : .04) % tile - tile; break;
                case "flow:auroraFluid":
                    var fx = new[] { .19, .14, .11 }[i]; var fy = new[] { .27, .22, .17 }[i];
                    offset.X = (Math.Cos(time * fx + i * 2.18) - 1) * width * .36;
                    offset.Y = (Math.Sin(time * fy + i * 2.18 * .73) - 1) * height * .34; break;
            }
        }
    }

    void UpdateFrameChrome()
    {
        var spec = ChromeGeometry.Resolve(_project.Canvas, _pvFrame, _pvOutW, _pvOutH);
        FrameChrome.Visibility = spec is null ? Visibility.Collapsed : Visibility.Visible;
        _projectionKey = null;
        if (spec is null) { FrameChrome.Source = null; return; }
        FrameChrome.Source = ChromeArt.Preview(spec, _pvOutW, _pvOutH);
        FrameChrome.Width = _pvOutW * _pvFrameScale; FrameChrome.Height = _pvOutH * _pvFrameScale;
        Canvas.SetLeft(FrameChrome, -_pvFrame.X * _pvFrameScale); Canvas.SetTop(FrameChrome, -_pvFrame.Y * _pvFrameScale);
        FrameHost.ClipToBounds = false;
    }

    readonly MediaPlayer _backgroundVideo = new() { Volume = 0, ScrubbingEnabled = true };
    VideoDrawing? _backgroundVideoDrawing;
    bool _backgroundPlaying;

    void UpdateBackgroundImageOrVideo(double width, double height)
    {
        var path = DecorationPath(_project.Canvas.Wallpaper);
        WallpaperImage.Visibility = path is null ? Visibility.Collapsed : Visibility.Visible;
        if (path is null)
        {
            if (_backgroundVideo.Source is not null) _backgroundVideo.Close();
            _backgroundPlaying = false; return;
        }
        WallpaperImage.Width = width; WallpaperImage.Height = height;
        WallpaperBlur.Radius = _project.Canvas.WallpaperBlur * 22;
        var video = new[] { ".mp4", ".mov", ".mkv", ".webm" }.Contains(Path.GetExtension(path).ToLowerInvariant());
        if (!Equals(WallpaperImage.Tag, path))
        {
            _backgroundVideo.Close(); _backgroundPlaying = false;
            if (video)
            {
                if (_backgroundVideoDrawing is null)
                {
                    _backgroundVideoDrawing = new VideoDrawing { Player = _backgroundVideo, Rect = new Rect(0, 0, 16, 9) };
                    _backgroundVideo.MediaOpened += (_, _) =>
                    {
                        _backgroundVideoDrawing.Rect = new Rect(0, 0, Math.Max(1, _backgroundVideo.NaturalVideoWidth), Math.Max(1, _backgroundVideo.NaturalVideoHeight));
                        _backgroundVideo.Pause();
                        UpdateBackgroundVideo();
                    };
                }
                WallpaperImage.Source = new DrawingImage(_backgroundVideoDrawing); _backgroundVideo.Open(new Uri(path));
            }
            else WallpaperImage.Source = LoadInspectorImageFull(path);
            WallpaperImage.Tag = path;
        }
        UpdateBackgroundVideo();
    }

    static BitmapSource LoadInspectorImageFull(string path)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return image;
    }

    void UpdateBackgroundVideo()
    {
        if (_backgroundVideo.Source is null || !_backgroundVideo.NaturalDuration.HasTimeSpan) return;
        var duration = _backgroundVideo.NaturalDuration.TimeSpan.TotalSeconds;
        if (duration <= 0) return;
        var time = _player.Time % duration;
        if (!_player.IsPlaying || Math.Abs(_backgroundVideo.Position.TotalSeconds - time) > .2)
            _backgroundVideo.Position = TimeSpan.FromSeconds(time);
        if (_backgroundPlaying != _player.IsPlaying)
        {
            _backgroundPlaying = _player.IsPlaying;
            if (_backgroundPlaying) _backgroundVideo.Play(); else _backgroundVideo.Pause();
        }
    }

    void ApplyOpeningVisual()
    {
        if (!OpeningMotion.IsNative(_project.Canvas)) return;
        var screen = OpeningMotion.Sample(_project, "screen", _player.Time, PreviewCanvas.Width, PreviewCanvas.Height);
        _screenLayerOpacity *= screen.Opacity;
        var matrix = FrameHost.RenderTransform.Value;
        matrix.ScaleAt(screen.Scale, screen.Scale, FrameHost.Width / 2, FrameHost.Height / 2);
        matrix.Translate(screen.X, screen.Y);
        FrameHost.RenderTransform = new MatrixTransform(matrix); ShadowRect.RenderTransform = FrameHost.RenderTransform;
        if (_projectionKey is { } projection && (screen.RotationX != 0 || screen.RotationY != 0))
            UpdatePerspective(projection.X + screen.RotationX, projection.Y + screen.RotationY,
                screen.Perspective, projection.AnchorX, projection.AnchorY);
        var camera = OpeningMotion.Sample(_project, "camera", _player.Time, PreviewCanvas.Width, PreviewCanvas.Height);
        var cameraMatrix = CameraBubble.RenderTransform.Value;
        cameraMatrix.ScaleAt(camera.Scale, camera.Scale, CameraBubble.Width / 2, CameraBubble.Height / 2);
        cameraMatrix.Translate(camera.X, camera.Y);
        CameraBubble.RenderTransform = new MatrixTransform(cameraMatrix); CameraLayer.Opacity *= camera.Opacity;
    }

    void BuildMonitorToolbar()
    {
        AspectRow.Children.Clear();
        if (_cropEditing)
        {
            AspectRow.Children.Add(DecorationButton(L.Text("重置裁切"), () => { Push(); _project.Canvas.Crop = new(); Commit(); }));
            AspectRow.Children.Add(DecorationButton(L.Text("取消"), CancelCrop));
            AspectRow.Children.Add(DecorationButton(L.Text("完成"), EndCrop));
            return;
        }
        var ratio = new CanvasRatioSelector(() => _project.Canvas, updated =>
        {
            var transition = FluidMotion.PrepareSwap(PreviewCanvas, PreviewTransitionLayer, 0);
            Push(); _project.Canvas = updated; Commit(); transition();
        });
        AspectRow.Children.Add(ratio);
        var crop = DecorationButton(L.Text("裁切"), BeginCrop);
        crop.Content = IconLabel(IconKind.Crop, L.Text("裁切"));
        System.Windows.Automation.AutomationProperties.SetName(crop, L.Text("裁切"));
        crop.Tag = "crop"; AspectRow.Children.Add(crop);
        var add = DecorationButton("＋", () => { }); add.Tag = "add"; add.ToolTip = L.Text("添加效果");
        add.Content = new Icon { Kind = IconKind.Plus, Width = 14, Height = 14 };
        System.Windows.Automation.AutomationProperties.SetName(add, L.Text("添加效果"));
        var menu = EditorActionMenus.Create(L.Text("添加到画面"));
        EditorActionMenus.Add(menu, L.Text("柔化或突出"), () => BeginRegionDrawing("blur"), IconKind.MotionViewfinder);
        EditorActionMenus.Add(menu, L.Text("导入贴图…"), AddSticker, IconKind.PhotoPlus);
        var paste = EditorActionMenus.Add(menu, L.Text("粘贴图片"), AddPastedSticker, IconKind.ClipboardDocument);
        menu.Opened += (_, _) => paste.IsEnabled = CanPasteSticker();
        add.ContextMenu = menu; add.Click += (_, _) => { menu.PlacementTarget = add; menu.IsOpen = true; }; AspectRow.Children.Add(add);
        var options = DecorationButton("☷", () => { }); options.Tag = "preview"; options.ToolTip = L.Text("预览选项");
        options.Content = new Icon { Kind = IconKind.Sliders, Width = 14, Height = 14 };
        System.Windows.Automation.AutomationProperties.SetName(options, L.Text("预览选项"));
        var quality = EditorActionMenus.Create(L.Text("预览选项"));
        foreach (var (title, key, detail) in new[] { (L.Text("完整分辨率"), "full", L.Text("保留素材细节")), (L.Text("清晰预览"), "low", L.Text("播放更流畅")) })
        {
            EditorActionMenus.Add(quality, title, () => { SettingsStore.Current.PreviewQuality = key; SettingsStore.Save(); foreach (var editor in Application.Current.Windows.OfType<EditorWindow>()) editor.SyncPreviewQuality(); }, detail: detail, key: key);
        }
        quality.Opened += (_, _) => { foreach (var item in quality.Items.OfType<MenuItem>().Where(item => item.IsCheckable)) item.IsChecked = Equals(item.Tag, SettingsStore.Current.PreviewQuality); };
        foreach (var button in AspectRow.Children.OfType<Button>()) { button.Style = (Style)FindResource("QuietButton"); button.FontSize = 12; button.Padding = new Thickness(8, 5, 8, 5); button.Margin = new Thickness(0); }
        ratio.FontSize = 13; ratio.Height = 32; ratio.Padding = new Thickness(12, 0, 12, 0);
        crop.FontSize = 13; crop.Height = 32; crop.Padding = new Thickness(12, 0, 12, 0);
        options.Style = (Style)FindResource("QuietButton"); options.Padding = new Thickness(8, 5, 8, 5); options.Margin = new Thickness(0);
        options.ContextMenu = quality; options.Click += (_, _) => { quality.PlacementTarget = options; quality.IsOpen = true; }; AspectRow.Children.Add(options);
    }
}
