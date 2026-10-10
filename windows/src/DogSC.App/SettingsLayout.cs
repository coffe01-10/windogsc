using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace DogSC;

public partial class SettingsWindow
{
    readonly Dictionary<string, StackPanel> _pages = new();
    readonly Dictionary<string, Button> _navigation = new();
    readonly Dictionary<string, Border> _appearanceTiles = new();
    string _section = "";
    Button Fps30 = null!, Fps60 = null!, QualityLow = null!, QualityFull = null!;
    Button CamCircle = null!, CamSquare = null!, CamRounded = null!, Cd0 = null!, Cd3 = null!, Cd5 = null!;
    CheckBox MicDefaultBox = null!, SysAudioDefaultBox = null!, _soundToggle = null!;
    TextBlock FfmpegLabel = null!, UpdateLabel = null!, ResetDone = null!, _projectsPath = null!, _exportPath = null!;
    FluidSegments _qualityChoices = null!, _cameraChoices = null!, _fpsChoices = null!, _countdownChoices = null!;

    TextBlock Text(string text, double size = 13, bool secondary = false, bool semi = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "InkSecondaryBrush" : "InkBrush");
        label.SetResourceReference(TextBlock.FontFamilyProperty, semi ? "FontSemi" : "FontRegular"); return label;
    }
    Button ActionButton(string title, RoutedEventHandler action)
    {
        var button = new Button { Content = title, Style = (Style)FindResource("QuietButton"), FontSize = 12, Padding = new Thickness(10, 7, 10, 7), VerticalAlignment = VerticalAlignment.Center };
        button.Click += action; return button;
    }
    CheckBox Switch(string name, bool selected, RoutedEventHandler action)
    {
        var toggle = new CheckBox { IsChecked = selected, Style = (Style)FindResource("SwitchCheckBox"), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(toggle, name); toggle.Click += action; return toggle;
    }
    StackPanel Page(string key, string title, string? subtitle)
    {
        var page = new StackPanel(); page.Children.Add(Text(title, 22, semi: true));
        if (subtitle is not null) { var helper = Text(subtitle, 12, true); helper.Margin = new Thickness(0, 5, 0, 0); page.Children.Add(helper); }
        _pages[key] = page; return page;
    }
    StackPanel Card(StackPanel page, string title)
    {
        var content = new StackPanel(); var heading = Text(title, 14, semi: true); heading.Margin = new Thickness(0, 0, 0, 14); content.Children.Add(heading);
        var card = new Border { Child = content, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(.75), Padding = new Thickness(16), Margin = new Thickness(0, 22, 0, 0) };
        card.SetResourceReference(Border.BackgroundProperty, "SettingsCardBrush"); card.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); page.Children.Add(card); return content;
    }
    void Divider(Panel panel)
    {
        var divider = new Border { Height = .75, Margin = new Thickness(0, 14, 0, 14) }; divider.SetResourceReference(Border.BackgroundProperty, "LineBrush"); panel.Children.Add(divider);
    }
    void Row(Panel panel, IconKind icon, string title, string description, UIElement accessory, out TextBlock helper)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var symbol = new Icon { Kind = icon, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center }; symbol.SetResourceReference(DogSC.Icon.ForegroundProperty, "InkSecondaryBrush"); row.Children.Add(symbol);
        var labels = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Text(title, 13, semi: true)); helper = Text(description, 12, true); helper.Margin = new Thickness(0, 3, 0, 0); labels.Children.Add(helper);
        Grid.SetColumn(labels, 1); row.Children.Add(labels); Grid.SetColumn(accessory, 2); row.Children.Add(accessory); panel.Children.Add(row);
    }
    Border ChoiceWell(FluidSegments segments)
    {
        var border = new Border { Child = segments, CornerRadius = new CornerRadius(12), Padding = new Thickness(4) }; border.SetResourceReference(Border.BackgroundProperty, "SegmentWellBrush"); return border;
    }
    Button Choice(string title, RoutedEventHandler action)
    {
        var button = ActionButton(title, action); button.Style = (Style)FindResource("SegmentButton"); button.MinHeight = 32; return button;
    }

    void BuildSettingsPages()
    {
        foreach (var (key, title, icon) in new[] { ("general", "通用", IconKind.Settings), ("editor", "编辑器", IconKind.Sliders),
            ("recording", "录制", IconKind.RecordCircle), ("permissions", "权限", IconKind.Shield), ("about", "关于", IconKind.Info) })
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Icon { Kind = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0) }); content.Children.Add(Text(title, 13, semi: true));
            var button = new Button { Content = content, Tag = key, Height = 40, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 5),
                Style = (Style)FindResource("QuietButton"), HorizontalContentAlignment = HorizontalAlignment.Left };
            System.Windows.Automation.AutomationProperties.SetName(button, "设置：" + title); button.Click += (_, _) => SelectSettingsSection(key);
            _navigation[key] = button; NavigationButtons.Children.Add(button);
        }

        var general = Page("general", "通用", "界面、声音与文件");
        var appearance = Card(general, "外观"); var tiles = new UniformGrid { Rows = 1 }; appearance.Children.Add(tiles);
        foreach (var (title, key) in new[] { ("跟随系统", "system"), ("浅色", "light"), ("深色", "dark") })
        {
            var stack = new StackPanel(); var miniature = AppearanceMiniature(key); miniature.Margin = new Thickness(8, 8, 8, 8); stack.Children.Add(miniature);
            var label = Text(title, 12); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 0, 0, 4); stack.Children.Add(label);
            var outline = new Border { Child = stack, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(.75), Padding = new Thickness(8) };
            outline.SetResourceReference(Border.BackgroundProperty, "SegmentWellBrush"); _appearanceTiles[key] = outline;
            var button = ActionButton(title, (_, _) => { var transition = FluidMotion.PrepareSwap(SettingsScroll, SettingsTransitionLayer, 0); ThemeService.Apply(key); SettingsStore.Save(); Paint(); foreach (var editor in Application.Current.Windows.OfType<EditorWindow>()) editor.SyncAppearance(); transition(); });
            button.Content = outline; button.Padding = new Thickness(0); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0, 0, 10, 0); tiles.Children.Add(button);
        }
        ((Button)tiles.Children[^1]).Margin = new Thickness(0);
        var sounds = Card(general, "提示与声音");
        _soundToggle = Switch("导出完成后播放提示音", SettingsStore.Current.ExportCompletionSoundEnabled,
            (_, _) => Set(() => SettingsStore.Current.ExportCompletionSoundEnabled = _soundToggle.IsChecked == true));
        var soundActions = new StackPanel { Orientation = Orientation.Horizontal }; soundActions.Children.Add(ActionButton("▷ 试听", (_, _) => System.Media.SystemSounds.Asterisk.Play())); soundActions.Children.Add(_soundToggle);
        Row(sounds, IconKind.Speaker, "导出完成后播放提示音", "仅在成功导出后播放", soundActions, out _);
        var locations = Card(general, "存储位置");
        Row(locations, IconKind.Folder, "项目位置", "", ActionButton("更改…", (_, _) => ChangeDirectory(true)), out _projectsPath); Divider(locations);
        Row(locations, IconKind.Share, "导出位置", "", ActionButton("更改…", (_, _) => ChangeDirectory(false)), out _exportPath);

        var editor = Page("editor", "编辑器", "预览与编辑工作区"); var preview = Card(editor, "画布与预览");
        QualityLow = Choice("清晰预览", QualityLow_Click); QualityFull = Choice("完整分辨率", QualityFull_Click);
        _qualityChoices = new FluidSegments([QualityLow, QualityFull], 1);
        var quality = ChoiceWell(_qualityChoices); quality.Width = 200;
        Row(preview, IconKind.Monitor, "预览画质", "清晰预览更流畅，完整分辨率保留细节；导出画质不受影响。", quality, out _);
        var layout = Card(editor, "窗口与界面状态");
        Row(layout, IconKind.Undo, "重置编辑窗口布局", "恢复默认轨道显示、时间线缩放及面板大小。", ActionButton("重置布局", ResetLayout_Click), out ResetDone);
        var advanced = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
        Row(advanced, IconKind.Folder, "ffmpeg", "", ActionButton("浏览…", BrowseFfmpeg_Click), out FfmpegLabel);
        editor.Children.Add(new Expander { Header = "导出工具", Content = advanced, Margin = new Thickness(0, 22, 0, 0) });

        var recording = Page("recording", "录制", "开始录制时的默认选项"); var audio = Card(recording, "默认音频输入");
        SysAudioDefaultBox = Switch("默认录制系统声音", true, SysAudioDefault_Changed); MicDefaultBox = Switch("默认录制麦克风声音", false, MicDefault_Changed);
        Row(audio, IconKind.Speaker, "录制系统声音", "默认采集应用程序发出的声音与媒体音频。", SysAudioDefaultBox, out _); Divider(audio);
        Row(audio, IconKind.Mic, "录制麦克风声音", "使用录制条选择的麦克风记录人声解说。", MicDefaultBox, out _);
        var camera = Card(recording, "悬浮摄像头预览");
        CamCircle = Choice("圆形", CamCircle_Click); CamSquare = Choice("圆角方形", CamSquare_Click); CamRounded = Choice("圆角矩形", CamRounded_Click);
        CamCircle.Content = CameraShapeTile("circle", "圆形"); CamSquare.Content = CameraShapeTile("square", "圆角方形"); CamRounded.Content = CameraShapeTile("rounded", "圆角矩形");
        _cameraChoices = new FluidSegments([CamCircle, CamSquare, CamRounded], 0); camera.Children.Add(ChoiceWell(_cameraChoices));
        var hint = Text("新录制的默认摄像头形状，可在编辑器中继续调整。", 12, true); hint.Margin = new Thickness(0, 14, 0, 0); camera.Children.Add(hint);
        Fps30 = Choice("30 FPS", Fps30_Click); Fps60 = Choice("60 FPS", Fps60_Click); _fpsChoices = new FluidSegments([Fps30, Fps60], 0);
        Cd0 = Choice("关闭", Cd0_Click); Cd3 = Choice("3 秒", Cd3_Click); Cd5 = Choice("5 秒", Cd5_Click); _countdownChoices = new FluidSegments([Cd0, Cd3, Cd5], 1);
        var defaults = new StackPanel { Margin = new Thickness(0, 6, 0, 8) }; defaults.Children.Add(Text("录制帧率", 12)); defaults.Children.Add(ChoiceWell(_fpsChoices));
        var cdTitle = Text("录制倒计时", 12); cdTitle.Margin = new Thickness(0, 14, 0, 6); defaults.Children.Add(cdTitle); defaults.Children.Add(ChoiceWell(_countdownChoices));
        recording.Children.Add(new Expander { Header = "帧率与倒计时", Content = defaults, Margin = new Thickness(0, 22, 0, 0) });

        var permissions = Page("permissions", "权限", "管理录制所需的系统权限"); var access = Card(permissions, "Windows 隐私与设备访问");
        Row(access, IconKind.Monitor, "屏幕录制", "使用 Windows 桌面采集；窗口遮挡会影响画面。", Status("桌面采集", "SuccessBrush"), out _); Divider(access);
        Row(access, IconKind.CursorMotion, "光标记录", "记录鼠标移动与点击，生成可编辑的光标轨道。", Status("应用内记录", "SuccessBrush"), out _); Divider(access);
        Row(access, IconKind.Camera, "摄像头", "画中画人像出镜与外接相机输入。", Status("由系统管理", "InkSecondaryBrush"), out _); Divider(access);
        Row(access, IconKind.Mic, "麦克风", "人声解说录音与音频设备采集。", Status("由系统管理", "InkSecondaryBrush"), out _); Divider(access);
        Row(access, IconKind.Shield, "设备不可用时", "请检查设备连接和 Windows 隐私设置。", ActionButton("打开系统设置 ↗", OpenPrivacy_Click), out _);

        var about = Page("about", "关于", null);
        var identity = new DockPanel { Margin = new Thickness(0, 24, 0, 0) };
        var appIcon = new Image { Width = 64, Height = 64, Source = new BitmapImage(new Uri("pack://application:,,,/Assets/AppIconMaster.png")), Margin = new Thickness(0, 0, 16, 0) };
        DockPanel.SetDock(appIcon, Dock.Left); identity.Children.Add(appIcon);
        var appName = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; appName.Children.Add(Text("DogSC", 18, semi: true)); UpdateLabel = Text("", 12, true); UpdateLabel.Margin = new Thickness(0, 6, 0, 0); appName.Children.Add(UpdateLabel); identity.Children.Add(appName); about.Children.Add(identity);
        Divider(about);
        Link(about, "查看源码", "https://github.com/coffe01-10/windogsc", IconKind.Layers);
        Link(about, "反馈建议", "https://github.com/coffe01-10/windogsc/issues", IconKind.Info);
        Link(about, "macOS 原版", "https://github.com/laogou717/dogsc", IconKind.ExternalLink);
        Divider(about); about.Children.Add(Text("免费开源。Windows 版的录制设备与系统权限使用本机能力。", 12, true));
    }

    void SelectSettingsSection(string section, bool animate = true)
    {
        if (_section == section) return;
        var transition = animate ? FluidMotion.PrepareSwap(SettingsScroll, SettingsTransitionLayer) : () => { };
        _section = section; SettingsPage.Content = _pages[section]; SettingsScroll.ScrollToTop();
        FluidMotion.Select(NavigationSelection, _navigation[section], NavigationGrid, animate);
        foreach (var (key, button) in _navigation)
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, key == section ? "已选中" : "");
        transition();
    }
    void PaintSettingsChoices()
    {
        var s = SettingsStore.Current;
        _qualityChoices.Select(s.PreviewQuality == "low" ? 0 : 1); _fpsChoices.Select(s.FrameRate < 60 ? 0 : 1);
        _cameraChoices.Select(s.CameraDefaultShape switch { "square" => 1, "rounded" => 2, _ => 0 });
        _countdownChoices.Select(s.RecordCountdownSeconds switch { <= 0 => 0, >= 5 => 2, _ => 1 });
        _soundToggle.IsChecked = s.ExportCompletionSoundEnabled;
        foreach (var (key, tile) in _appearanceTiles)
        {
            var selected = key == (s.AppearancePreference ?? s.Theme);
            tile.SetResourceReference(Border.BorderBrushProperty, selected ? "SelectionOutlineBrush" : "LineBrush"); tile.BorderThickness = new Thickness(selected ? 1.25 : .75);
        }
        _projectsPath.Text = s.ProjectsDirectory; _projectsPath.TextTrimming = TextTrimming.CharacterEllipsis; _projectsPath.ToolTip = s.ProjectsDirectory;
        _exportPath.Text = s.ExportDirectory; _exportPath.TextTrimming = TextTrimming.CharacterEllipsis; _exportPath.ToolTip = s.ExportDirectory;
    }
    void ChangeDirectory(bool projects)
    {
        var s = SettingsStore.Current; var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = projects ? s.ProjectsDirectory : s.ExportDirectory };
        if (dialog.ShowDialog(this) != true) return;
        if (projects) s.ProjectsDirectory = dialog.FolderName; else s.ExportDirectory = dialog.FolderName;
        SettingsStore.Save(); Paint();
    }
    Border Status(string text, string brush)
    {
        var label = Text(text, 11, semi: true); label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        var badge = new Border { Child = label, CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 4, 9, 4) }; badge.SetResourceReference(Border.BackgroundProperty, "SegmentWellBrush"); return badge;
    }
    void Link(Panel page, string title, string url, IconKind icon)
    {
        var button = ActionButton(title, (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        var row = new DockPanel(); var arrow = new Icon { Kind = IconKind.ExternalLink, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(arrow, Dock.Right); row.Children.Add(arrow);
        var glyph = new Icon { Kind = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 14, 0) }; DockPanel.SetDock(glyph, Dock.Left); row.Children.Add(glyph); row.Children.Add(Text(title));
        button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Height = 44; button.Margin = new Thickness(0, 0, 0, 8); page.Children.Add(button);
    }
    UIElement CameraShapeTile(string shape, string title)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        stack.Children.Add(new Border { Width = shape == "rounded" ? 36 : 28, Height = 28, CornerRadius = new CornerRadius(shape == "circle" ? 14 : 7), Background = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8), Child = new Icon { Kind = IconKind.Camera, Width = 16, Height = 16, Foreground = Brushes.White } });
        var label = Text(title, 12); label.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(label); return stack;
    }
    Border AppearanceMiniature(string key)
    {
        var grid = new Grid { Height = 58 }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var half = 0; half < 2; half++)
        {
            var dark = key == "dark" || key == "system" && half == 1;
            var canvas = new Canvas { Background = dark ? new SolidColorBrush(Color.FromRgb(26, 26, 26)) : Brushes.White, ClipToBounds = true };
            var sidebar = new Border { Width = half == 0 ? 25 : 0, Height = 58, Background = dark ? new SolidColorBrush(Color.FromRgb(38, 38, 38)) : Brushes.WhiteSmoke }; canvas.Children.Add(sidebar);
            for (var line = 0; line < 3; line++)
            {
                var pill = new Border { Width = half == 0 ? 36 : 47, Height = 5, CornerRadius = new CornerRadius(2.5), Background = dark ? new SolidColorBrush(Color.FromRgb(59, 59, 59)) : new SolidColorBrush(Color.FromRgb(235, 235, 235)) };
                Canvas.SetLeft(pill, half == 0 ? 30 : 0); Canvas.SetTop(pill, 15 + line * 10); canvas.Children.Add(pill);
            }
            Grid.SetColumn(canvas, half); grid.Children.Add(canvas);
        }
        var frame = new Border { Child = grid, CornerRadius = new CornerRadius(6), ClipToBounds = true, BorderThickness = new Thickness(.5), BorderBrush = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)) }; return frame;
    }
    void Settings_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
