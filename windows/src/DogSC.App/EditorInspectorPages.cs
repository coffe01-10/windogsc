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
    DispatcherTimer? _cursorPreviewTimer;
    readonly List<(FrameworkElement Image, TranslateTransform Offset)> _patternLayers = [];
    (string? Wallpaper, double Width, double Height, double Scale, double Opacity)? _patternPreviewKey;

    void InspectorHeader(string title, string scope)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var tag = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(8, 0, 0, 0), Child = new TextBlock { Text = scope, FontSize = 10 } };
        tag.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        DockPanel.SetDock(tag, Dock.Right); row.Children.Add(tag); row.Children.Add(Heading(title));
        Inspector.Children.Add(row);
    }

    static StackPanel IconLabel(IconKind kind, string title)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new Icon { Kind = kind, Width = 14, Height = 14 });
        var label = new TextBlock { Text = title, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Button.Foreground))
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        content.Children.Add(label);
        return content;
    }

    UIElement Segments(IEnumerable<(string Title, string Key)> options, string selected, Action<string> choose, bool icons = false)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        foreach (var (title, key) in options)
        {
            var button = new Button { Content = title, Padding = new Thickness(5, 6, 5, 6), FontSize = 11,
                Style = (Style)FindResource("ChipButton"), Margin = new Thickness(1) };
            if (icons)
                button.Content = IconLabel(key switch
                {
                    "background" => IconKind.Layers, "layout" => IconKind.InsetRectangle, "frame" => IconKind.Window,
                    "wallpaper" => IconKind.Photo, "grid" => IconKind.GridPattern,
                    "motion" => IconKind.WaveformRectangle, _ => IconKind.PhotoPlus,
                }, title);
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            ControlAppearance.SelectChip(button, selected == key);
            button.Click += (_, _) => choose(key);
            grid.Children.Add(button);
        }
        var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = grid };
        border.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        return border;
    }

    UIElement ValueSlider(string title, double min, double max, double value, Action<double> set,
        Func<double, string>? format = null)
    {
        format ??= v => v.ToString("0.##", CultureInfo.InvariantCulture);
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 3) };
        var label = new TextBlock { Text = format(value), HorizontalAlignment = HorizontalAlignment.Right, FontSize = 11 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "InkSecondaryBrush");
        var row = new DockPanel(); DockPanel.SetDock(label, Dock.Right); row.Children.Add(label);
        row.Children.Add(new TextBlock { Text = title, FontSize = 12 }); panel.Children.Add(row);
        var slider = Slider(min, max, value, v => { set(v); label.Text = format(v); });
        System.Windows.Automation.AutomationProperties.SetName(slider, title);
        panel.Children.Add(slider); return panel;
    }

    static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    static string Times(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "×";
    static string Seconds(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + " 秒";

    void BuildCanvasInspector()
    {
        if (_cameraSelected) { BuildCameraInspector(); return; }
        InspectorHeader("画布", "整个项目");
        Inspector.Children.Add(Segments([("背景", "background"), ("布局", "layout"), ("外框", "frame")], _canvasTab,
            key => { _canvasTab = key; RebuildInspector(); }, icons: true));
        switch (_canvasTab)
        {
            case "background": BuildBackgroundInspector(); break;
            case "frame": BuildFrameInspector(); break;
            default: BuildCanvasLayoutControls(); break;
        }
    }

    void BuildBackgroundInspector()
    {
        var canvas = _project.Canvas;
        Inspector.Children.Add(Caption("背景来源"));
        Inspector.Children.Add(Segments([("壁纸", "wallpaper"), ("网格", "grid"), ("动态", "motion"), ("自定义", "custom")],
            _backgroundTab, key => { _backgroundTab = key; RebuildInspector(); }, icons: true));
        var tiles = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
        if (_backgroundTab is "grid" or "motion")
        {
            var options = _backgroundTab == "grid"
                ? new[] { ("黑曜石网格", "pattern:obsidianGrid"), ("工程白网格", "pattern:engineeringWhiteGrid"),
                    ("暗夜星阵", "pattern:midnightDots"), ("建筑极简点", "pattern:architecturalDots"), ("立体等角网", "pattern:isometricMesh") }
                : new[] { ("赛博微流网格", "flow:cyberDriftGrid"), ("星辉漫步点阵", "flow:starfieldDots"), ("极光流体星云", "flow:auroraFluid") };
            foreach (var (name, key) in options)
            {
                var bake = PatternLibrary.Resolve(key, 150, 112, canvas.PatternScale, canvas.PatternOpacity)!;
                BackgroundTile(tiles, name, key, PatternArt.Preview(bake, 150, 112));
            }
            Inspector.Children.Add(tiles);
            Inspector.Children.Add(ValueSlider("图案大小", .4, 6, canvas.PatternScale, v => canvas.PatternScale = v, Times));
            Inspector.Children.Add(ValueSlider("不透明度", 0, 1, canvas.PatternOpacity, v => canvas.PatternOpacity = v, Percent));
        }
        else if (_backgroundTab == "wallpaper")
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Wallpaper");
            if (Directory.Exists(folder))
                foreach (var path in Directory.EnumerateFiles(folder, "*.jpg", SearchOption.AllDirectories).Take(12))
                    BackgroundTile(tiles, Path.GetFileNameWithoutExtension(path), path, LoadInspectorImage(path));
            Inspector.Children.Add(tiles);
            Inspector.Children.Add(DecorationButton("选择壁纸图片或视频…", () => PickBackgroundMedia()));
            Inspector.Children.Add(ValueSlider("壁纸模糊", 0, 1, canvas.WallpaperBlur, v => canvas.WallpaperBlur = v, Percent));
        }
        else
        {
            Inspector.Children.Add(DecorationButton("选择图片或视频…", () => PickBackgroundMedia()));
            Inspector.Children.Add(Caption("图片作为静态背景；MP4 / MOV 视频循环播放并参与导出。"));
            var colors = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
            foreach (var color in new[] { "#F3F1EC", "#F7F7F5", "#D9E4DD", "#1E1F22", "#2C3A4A", "#000000" })
            {
                var swatch = new Button { Width = 28, Height = 28, Background = Brush(color), Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 7, 7), ToolTip = color };
                swatch.Click += (_, _) => { Push(); canvas.Background = color; canvas.Wallpaper = null; Commit(); };
                colors.Children.Add(swatch);
            }
            Inspector.Children.Add(colors);
            ColorEntry(Inspector, "背景颜色", canvas.Background, v => { canvas.Background = v; canvas.Wallpaper = null; });
            Inspector.Children.Add(ValueSlider("背景模糊", 0, 1, canvas.WallpaperBlur, v => canvas.WallpaperBlur = v, Percent));
        }
        if (canvas.Wallpaper is not null)
            Inspector.Children.Add(DecorationButton("清除背景素材", () => { Push(); canvas.Wallpaper = null; Commit(); }));
    }

    static ImageSource LoadInspectorImage(string path)
    {
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 200; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }

    void BackgroundTile(Panel tiles, string name, string key, ImageSource source)
    {
        var grid = new Grid { Width = 72, Height = 72 };
        grid.Children.Add(new Image { Source = source, Stretch = Stretch.UniformToFill });
        var label = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(170, 30, 30, 30)),
            Padding = new Thickness(3), Child = new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 9, TextWrapping = TextWrapping.Wrap } };
        grid.Children.Add(label); grid.Clip = new RectangleGeometry(new Rect(0, 0, 72, 72), 7, 7);
        var button = new Button { Content = grid, Padding = new Thickness(2), Margin = new Thickness(0, 0, 5, 6),
            Style = (Style)FindResource("ChipButton"), ToolTip = name };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        ControlAppearance.SelectChip(button, _project.Canvas.Wallpaper == key);
        button.Click += (_, _) => { Push(); _project.Canvas.Wallpaper = key; Commit(); };
        tiles.Children.Add(button);
    }

    void PickBackgroundMedia()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择背景图片或视频", Filter = "图片或视频|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.mp4;*.mov;*.mkv;*.webm|所有文件|*.*" };
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
        var apply = DecorationButton("应用", () =>
        {
            try { ColorConverter.ConvertFromString(box.Text); }
            catch (FormatException) { box.ToolTip = "请输入颜色，例如 #528AE8"; return; }
            Push(); set(box.Text); Commit();
        });
        DockPanel.SetDock(apply, Dock.Right); row.Children.Add(apply); row.Children.Add(box); panel.Children.Add(row);
    }

    void BuildCanvasLayoutControls()
    {
        var canvas = _project.Canvas;
        Inspector.Children.Add(Caption("画布间距"));
        Inspector.Children.Add(ValueSlider("边距", 0, 180, canvas.Padding, v => canvas.Padding = v));
        Inspector.Children.Add(Caption("位置与大小"));
        Inspector.Children.Add(PositionPad());
        Inspector.Children.Add(ValueSlider("水平位置", 0, 1, canvas.CenterX, v => canvas.CenterX = v, Percent));
        Inspector.Children.Add(ValueSlider("垂直位置", 0, 1, canvas.CenterY, v => canvas.CenterY = v, Percent));
        Inspector.Children.Add(ValueSlider("内容缩放", .25, 2.5, canvas.Scale, v => canvas.Scale = v, Times));
        Inspector.Children.Add(DecorationButton("重置位置与大小", () => { Push(); canvas.CenterX = canvas.CenterY = .5; canvas.Scale = 1; Commit(); }));
        Inspector.Children.Add(Caption("圆角与阴影"));
        Inspector.Children.Add(ValueSlider("屏幕圆角", 0, 90, canvas.Corner, v => canvas.Corner = v));
        Inspector.Children.Add(ValueSlider("阴影", 0, 100, canvas.Shadow, v => canvas.Shadow = v));
        Inspector.Children.Add(ValueSlider("左右倾斜", -40, 40, canvas.TiltY, v => canvas.TiltY = v, v => v.ToString("0") + "°"));
        Inspector.Children.Add(ValueSlider("上下倾斜", -40, 40, canvas.TiltX, v => canvas.TiltX = v, v => v.ToString("0") + "°"));
        Inspector.Children.Add(ValueSlider("边缘虚化", 0, 1, canvas.FocusBlur, v => canvas.FocusBlur = v, Percent));
        BuildCropControls(); BuildScenePresets();
    }

    void BuildFrameInspector()
    {
        var canvas = _project.Canvas;
        Inspector.Children.Add(Caption("屏幕外框"));
        Inspector.Children.Add(CursorChipRow(new[] { ("无", "none"), ("浅色窗口", "windowLight"), ("深色窗口", "windowDark"),
            ("浅色浏览器", "browserLight"), ("深色浏览器", "browserDark"), ("手机", "phone"), ("平板", "tablet") },
            canvas.ScreenFrame, v => canvas.ScreenFrame = v));
        if (canvas.ScreenFrame == "none") return;
        Inspector.Children.Add(ValueSlider("外框缩放", .6, 1.6, canvas.ScreenFrameScale, v => canvas.ScreenFrameScale = v, Times));
        Inspector.Children.Add(ValueSlider("顶栏缩放", .65, 1.6, canvas.ScreenFrameToolbarScale, v => canvas.ScreenFrameToolbarScale = v, Times));
        TextEntry("窗口标题", canvas.ScreenFrameTitle, v => canvas.ScreenFrameTitle = v);
        if (canvas.ScreenFrame.StartsWith("browser")) TextEntry("浏览器地址", canvas.ScreenFrameAddress, v => canvas.ScreenFrameAddress = v);
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
        InspectorHeader("开场", "整个项目");
        var canvas = _project.Canvas;
        AddToggle(Inspector, "全局开场", canvas.Opening != "none", on => { canvas.Opening = on ? "light3D" : "none"; RebuildInspector(); });
        if (canvas.Opening == "none") return;
        Inspector.Children.Add(Caption("让画面中的元素按顺序进入"));
        Inspector.Children.Add(Segments([("聚拢", "converge"), ("侧滑", "sideSlide"), ("轻 3D", "light3D")], canvas.Opening,
            key => { Push(); canvas.Opening = key; Commit(); }));
        Inspector.Children.Add(Caption("动效节奏"));
        Inspector.Children.Add(Segments([("平滑", "smooth"), ("干脆", "clear"), ("轻柔", "gentle")], canvas.OpeningTiming,
            key => { Push(); canvas.OpeningTiming = key; Commit(); }));
        Inspector.Children.Add(ValueSlider("总时长", .4, 8, canvas.OpeningDuration, v => canvas.OpeningDuration = v, Seconds));
        Inspector.Children.Add(ValueSlider("元素间隔", 0, 1.2, canvas.OpeningInterval, v => canvas.OpeningInterval = v, Seconds));
        var elements = Disclosure("参与元素与顺序");
        foreach (var (element, index) in canvas.OpeningOrder.Select((v, i) => (v, i)))
        {
            var row = new DockPanel();
            var name = element switch { "screen" => "屏幕", "camera" => "摄像头", _ => "贴图" };
            var include = new CheckBox { Content = $"{index + 1}  {name}", IsChecked = canvas.OpeningIncluded.Contains(element),
                Style = (Style)FindResource("SwitchCheckBox"), VerticalAlignment = VerticalAlignment.Center };
            include.Click += (_, _) => { Push(); canvas.OpeningIncluded.Remove(element); if (include.IsChecked == true) canvas.OpeningIncluded.Add(element); Commit(); };
            foreach (var (title, delta) in new[] { ("↑", -1), ("↓", 1) })
            {
                var button = DecorationButton(title, () => { var target = index + delta; Push(); canvas.OpeningOrder.RemoveAt(index); canvas.OpeningOrder.Insert(target, element); Commit(); });
                button.IsEnabled = index + delta >= 0 && index + delta < canvas.OpeningOrder.Count;
                button.ToolTip = delta < 0 ? "提前进入" : "延后进入";
                DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
            }
            row.Children.Add(include); elements.Children.Add(row);
        }
        Inspector.Children.Add(DecorationButton("从片头预览", () => _player.Seek(0, true)));
        Inspector.Children.Add(Caption("从 0 秒开始的贴图由全局开场接管入场；柔化与聚光按各自时段生效。"));
    }

    void BuildCursorInspector()
    {
        InspectorHeader("光标", "整个项目");
        var fx = _project.Canvas.Cursor;
        BuildCursorEffectPreview();
        Inspector.Children.Add(Segments([("系统", "pointer"), ("触点", "dot"), ("隐藏", "hidden")], fx.Mode,
            key => { Push(); fx.Mode = key; fx.Asset = key switch { "dot" => "system.touch-dot", "hidden" => "system.hidden", _ => "auto" }; Commit(); }));
        Inspector.Children.Add(ValueSlider("大小", .5, 4, fx.Size, v => fx.Size = v, Times));
        AddToggle(Inspector, "静止时隐藏", fx.IdleHide, v => fx.IdleHide = v);
        Inspector.Children.Add(ValueSlider("隐藏延迟", .1, 10, fx.IdleDelay, v => fx.IdleDelay = v, Seconds));
        var timing = Disclosure("时间校准"); AddTimingControls(timing, () => fx.TimingAdvance, v => fx.TimingAdvance = v, 1);
        Inspector.Children.Add(Caption("点击反馈"));
        Inspector.Children.Add(Segments([("涟漪", "ripple"), ("光晕", "glow"), ("双环", "pulse"), ("微闪", "flash"), ("无", "none")],
            CursorCatalog.ResolveClick(fx), key => { Push(); fx.ClickStyle = key; fx.ClickRipple = key != "none"; Commit(); }));
        Inspector.Children.Add(Caption("颜色"));
        var colors = new WrapPanel();
        foreach (var color in new[] { "#7C5CFC", "#19C3EE", "#36A87E", "#F3BC48", "#E56060", "#FFFFFF" })
        {
            var button = new Button { Background = Brush(color), Width = 23, Height = 23, Padding = new Thickness(0), Margin = new Thickness(0, 0, 7, 3), ToolTip = color };
            button.Click += (_, _) => { Push(); fx.ClickColor = color; Commit(); }; colors.Children.Add(button);
        }
        Inspector.Children.Add(colors); ColorEntry(Inspector, "点击颜色", fx.ClickColor, v => fx.ClickColor = v);
        Inspector.Children.Add(ValueSlider("不透明度", 0, 1, fx.ClickOpacity, v => fx.ClickOpacity = v, Percent));
        Inspector.Children.Add(ValueSlider("范围", .5, 4, fx.ClickScale, v => fx.ClickScale = v, Times));
        Inspector.Children.Add(Caption("运动平滑"));
        Inspector.Children.Add(CursorChipRow(CursorCatalog.Motions.Select(m => (m.Name, m.Id)), fx.Motion, v => fx.Motion = v));
        Inspector.Children.Add(ValueSlider("运动倾斜强度", 0, 4, fx.MotionTilt, v => fx.MotionTilt = v));
        Inspector.Children.Add(Caption("系统光标样式"));
        Inspector.Children.Add(CursorChipRow(CursorCatalog.Assets.Select(a => (a.Name, a.Id)), CursorCatalog.ResolveAsset(fx), v =>
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
        preview.Children.Add(new TextBlock { Text = "效果预览", FontSize = 10, Margin = new Thickness(8), Opacity = .5 });
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
        _cursorPreviewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _cursorPreviewTimer.Tick += (_, _) => Paint();
        var timer = _cursorPreviewTimer;
        preview.Loaded += (_, _) => timer.Start(); preview.Unloaded += (_, _) => timer.Stop();
    }

    void BuildZoomTargetControls(ZoomSpan zoom)
    {
        Inspector.Children.Add(Segments([("自动跟随", "automatic"), ("手动定位", "manual")], zoom.Origin,
            key => { Push(); if (key == "manual") MakeZoomManual(zoom); else zoom.Origin = key; Commit(); }));
        var pose = zoom.Origin == "automatic" ? _cursorTimeline!.Motion.TargetAt(zoom, _player.Time) : new ZoomPose(zoom.Scale, zoom.FocusX, zoom.FocusY);
        var pad = new Canvas { Width = 240, Height = 135, Clip = new RectangleGeometry(new Rect(0, 0, 240, 135), 8, 8), Margin = new Thickness(0, 12, 0, 4) };
        pad.Children.Add(new Rectangle { Width = 240, Height = 135, Fill = new DrawingBrush(_player.ScreenDrawing) { Stretch = Stretch.UniformToFill } });
        var marker = new Ellipse { Width = 16, Height = 16, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 2 };
        pad.Children.Add(marker); Canvas.SetLeft(marker, pose.X * 224); Canvas.SetTop(marker, pose.Y * 119);
        void Move(Point p)
        {
            MakeZoomManual(zoom); zoom.FocusX = Math.Clamp(p.X / 240, 0, 1); zoom.FocusY = Math.Clamp(p.Y / 135, 0, 1);
            Canvas.SetLeft(marker, zoom.FocusX * 224); Canvas.SetTop(marker, zoom.FocusY * 119); Commit(rebuild: false);
        }
        pad.MouseLeftButtonDown += (_, e) => { Push(); pad.CaptureMouse(); Move(e.GetPosition(pad)); e.Handled = true; };
        pad.MouseMove += (_, e) => { if (pad.IsMouseCaptured) Move(e.GetPosition(pad)); };
        pad.MouseLeftButtonUp += (_, _) => { pad.ReleaseMouseCapture(); FlushPendingSave(); RebuildInspector(); };
        Inspector.Children.Add(pad);
        var x = Slider(0, 1, pose.X, v => { MakeZoomManual(zoom); zoom.FocusX = v; }, refresh: true);
        var y = Slider(0, 1, pose.Y, v => { MakeZoomManual(zoom); zoom.FocusY = v; }, refresh: true);
        _zoomFocusSliders = (x, y); Inspector.Children.Add(Caption("X / Y 焦点")); Inspector.Children.Add(x); Inspector.Children.Add(y);
        Inspector.Children.Add(Caption("运动类型"));
        Inspector.Children.Add(Segments([("缩放", "zoom"), ("屏幕 3D", "screen3d")], "zoom", key => { if (key == "screen3d") ConvertZoomToScreenMotion(zoom); }));
        Inspector.Children.Add(ValueSlider("缩放倍数", 1, 4, zoom.Scale, v => zoom.Scale = v, Times));
        Inspector.Children.Add(DecorationButton("将倍数应用到所有缩放", () => { Push(); foreach (var z in _project.Zooms) z.Scale = zoom.Scale; Commit(); }));
        Inspector.Children.Add(Caption("过渡"));
        Inspector.Children.Add(Segments([("进入", "enter"), ("保持", "hold"), ("退出", "exit")], _zoomTransitionPhase,
            key => { _zoomTransitionPhase = key; RebuildInspector(); }));
        switch (_zoomTransitionPhase)
        {
            case "enter": Inspector.Children.Add(ValueSlider("进入时长", 0, 5, zoom.EnterDuration ?? .4, v => zoom.EnterDuration = v, Seconds)); break;
            case "exit": Inspector.Children.Add(ValueSlider("退出时长", 0, 5, zoom.ExitDuration ?? .4, v => zoom.ExitDuration = v, Seconds)); break;
            default:
                Inspector.Children.Add(ValueSlider("片段总时长", .12, Math.Max(.12, TimelineMath.Duration(_project) - zoom.Start), zoom.Duration,
                    v => { if (!TimelineEdits.TryMoveZoom(_project, zoom.Id, zoom.Start, v)) TimeLabel.Text = "时长与另一段缩放重叠。"; }, Seconds)); break;
        }
    }

    void ConvertZoomToScreenMotion(ZoomSpan zoom)
    {
        Push(); var clip = new ScreenMotionClip { Target = new ScreenMotionState { Scale = zoom.Scale, X = zoom.FocusX, Y = zoom.FocusY } };
        clip.Timing.StartTime = zoom.Start; clip.Timing.Duration = zoom.Duration;
        clip.Timing.LeadInDuration = zoom.EnterDuration ?? .4; clip.Timing.ReturnDuration = zoom.ExitDuration ?? .4;
        _project.Zooms.Remove(zoom); _project.ScreenMotions.Add(clip); _selectedZoom = null; _selectedDecoration = clip.Id;
        _tool = "zoom"; ApplyTrackFlags(TrackFlags | 2); Commit();
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
        CameraBubble.RenderTransform = new MatrixTransform(cameraMatrix); CameraBubble.Opacity *= camera.Opacity;
    }

    void ConvertScreenMotionToZoom(ScreenMotionClip screen)
    {
        var time = screen.Timing;
        var zoom = new ZoomSpan { Start = time.StartTime, Duration = time.Duration, Scale = Math.Clamp(screen.Target.Scale, 1, 4),
            FocusX = screen.Target.X, FocusY = screen.Target.Y, Origin = "manual", EnterDuration = time.LeadInDuration, ExitDuration = time.ReturnDuration };
        if (_project.Zooms.Any(z => zoom.Start < z.Start + z.Duration && z.Start < zoom.Start + zoom.Duration))
        { TimeLabel.Text = "该时段已有缩放，请先移开重叠片段。"; return; }
        Push(); _project.ScreenMotions.Remove(screen); _project.Zooms.Add(zoom);
        _selectedDecoration = null; _selectedZoom = zoom.Id; _tool = "zoom"; Commit();
    }

    void BuildMonitorToolbar()
    {
        AspectRow.Children.Clear();
        var ratio = new ComboBox { Width = 78, Margin = new Thickness(0, 0, 6, 0) };
        foreach (var (title, key) in new[] { ("适应", "source"), ("16:9", "16:9"), ("4:3", "4:3"), ("1:1", "1:1"),
            ("9:16", "9:16"), ("3:4", "3:4"), ("2.39:1", "2.39:1"), ("1:2.39", "1:2.39") })
            ratio.Items.Add(new ComboBoxItem { Content = title, Tag = key });
        if (!ratio.Items.Cast<ComboBoxItem>().Any(i => Equals(i.Tag, _project.Canvas.Aspect)))
            ratio.Items.Add(new ComboBoxItem { Content = _project.Canvas.Aspect, Tag = _project.Canvas.Aspect });
        ratio.SelectedItem = ratio.Items.Cast<ComboBoxItem>().First(i => Equals(i.Tag, _project.Canvas.Aspect));
        ratio.SelectionChanged += (_, _) => { if (ratio.SelectedItem is not ComboBoxItem i || Equals(i.Tag, _project.Canvas.Aspect)) return; Push(); _project.Canvas.Aspect = (string)i.Tag; Commit(); };
        System.Windows.Automation.AutomationProperties.SetName(ratio, "画布比例"); AspectRow.Children.Add(ratio);
        var crop = DecorationButton("裁切", () => { _tool = "layout"; _canvasTab = "layout"; _expandedDetails.Add("裁切与自定义比例"); RebuildInspector(); });
        crop.Content = IconLabel(IconKind.Crop, "裁切");
        System.Windows.Automation.AutomationProperties.SetName(crop, "裁切");
        crop.Tag = "crop"; AspectRow.Children.Add(crop);
        var add = DecorationButton("＋", () => { }); add.Tag = "add"; add.ToolTip = "添加效果";
        add.Content = new Icon { Kind = IconKind.Plus, Width = 14, Height = 14 };
        System.Windows.Automation.AutomationProperties.SetName(add, "添加效果");
        var menu = new ContextMenu();
        void Add(string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("缩放", () => { Push(); var z = TimelineEdits.AddZoom(_project, _player.Time); if (z is null) { _undo.Pop(); return; } _selectedZoom = z.Id; _selectedDecoration = null; _tool = "zoom"; Commit(); });
        Add("屏幕 3D", () => { Push(); var clip = new ScreenMotionClip(); clip.Timing.StartTime = _player.Time; _project.ScreenMotions.Add(clip); _selectedDecoration = clip.Id; _selectedZoom = null; _tool = "zoom"; ApplyTrackFlags(TrackFlags | 2); Commit(); });
        Add("柔化区域", () => BeginRegionDrawing("blur")); Add("聚光区域", () => BeginRegionDrawing("spotlight"));
        Add("贴图…", AddSticker);
        add.ContextMenu = menu; add.Click += (_, _) => { menu.PlacementTarget = add; menu.IsOpen = true; }; AspectRow.Children.Add(add);
        var options = DecorationButton("☷", () => { }); options.Tag = "preview"; options.ToolTip = "预览选项";
        options.Content = new Icon { Kind = IconKind.Sliders, Width = 14, Height = 14 };
        System.Windows.Automation.AutomationProperties.SetName(options, "预览选项");
        var quality = new ContextMenu();
        foreach (var (title, key, detail) in new[] { ("完整分辨率", "full", "保留源素材细节"), ("流畅预览", "low", "使用代理提高播放流畅度") })
        {
            var text = new StackPanel(); text.Children.Add(new TextBlock { Text = title });
            text.Children.Add(new TextBlock { Text = detail, FontSize = 10, Opacity = .6 });
            var item = new MenuItem { Header = text, IsCheckable = true, Tag = key };
            item.Click += (_, _) => { SettingsStore.Current.PreviewQuality = key; SettingsStore.Save(); foreach (var editor in Application.Current.Windows.OfType<EditorWindow>()) editor.SyncPreviewQuality(); };
            quality.Items.Add(item);
        }
        quality.Opened += (_, _) => { foreach (MenuItem item in quality.Items) item.IsChecked = Equals(item.Tag, SettingsStore.Current.PreviewQuality); };
        options.ContextMenu = quality; options.Click += (_, _) => { quality.PlacementTarget = options; quality.IsOpen = true; }; AspectRow.Children.Add(options);
    }
}
