using System.Globalization;
using System.Text.Json;
using System.Windows.Media.Effects;
using DogSC.Core;

namespace DogSC;

public partial class EditorWindow : Window
{
    readonly PreviewPlayer _player = new() { Quality = SettingsStore.Current.PreviewQuality };
    readonly Stack<string> _undo = new();
    readonly Stack<string> _redo = new();
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    bool _pendingSave;
    ProjectDocument _project = new();
    string _directory = "";
    string _tool = "layout";
    string? _selectedClip;
    string? _selectedZoom;
    bool _lock;
    bool _dragUndo;
    Point _dragOrigin;
    double _dragX;
    double _dragY;
    string? _dragTarget;
    string? _selectedDecoration;
    readonly RegionPreview _sourceRegions = new();
    readonly RegionPreview _canvasRegions = new();
    readonly Dictionary<string, Border> _stickerImages = [];
    StickerMotion.Track? _stickerTrack;
    CursorTimeline? _cursorTimeline;
    (string Mode, double Size)? _cursorBitmapKey;

    public EditorWindow()
    {
        InitializeComponent();
        InitializeRegionEditor();
        InitializeOverlayEditor();
        _saveTimer.Tick += (_, _) => FlushPendingSave();
        Loaded += (_, _) => WindowPlacement.KeepInWorkArea(this, center: true);
        Closing += (_, _) => { CancelOverlayGesture(); CancelRegionEditing(); FlushPendingSave(); };
        SetToolContent(ToolLayout, IconKind.Layout, "画面");
        SetToolContent(ToolZoom, IconKind.Zoom, "缩放");
        SetToolContent(ToolEffects, IconKind.Region, "效果");
        SetToolContent(ToolAudio, IconKind.Speaker, "声音");
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIconMaster.png"));
        FrameRect.Fill = new DrawingBrush { Drawing = _player.ScreenDrawing, Stretch = Stretch.Fill };
        CameraBubble.Fill = new DrawingBrush { Drawing = _player.CameraDrawing, Stretch = Stretch.Fill };
        CameraBubble.Stroke = Brushes.White;
        foreach (var speed in new[] { 0.5, 0.75, 1d, 1.25, 1.5, 2d, 4, 8, 16, 32, 64, 100 })
        {
            SpeedBox.Items.Add(speed.ToString("0.##", CultureInfo.InvariantCulture) + "×");
        }

        _player.MediaGeometryChanged += () => Dispatcher.Invoke(() => { _cameraMaskAspect ??= CameraSourceAspect; LayoutPreview(); });
        _player.Changed += () => Dispatcher.Invoke(SyncPlayback);
        _player.Failed += message => Dispatcher.Invoke(() => TimeLabel.Text = "预览失败：" + message);
        Timeline.SeekRequested += (_, time) => _player.Seek(time, _player.IsPlaying);
        Timeline.InteractionStarted += (_, _) => { if (_player.IsPlaying) _player.Pause(); };
        Timeline.ClipSelected += (_, id) => { _cameraSelected = false; CancelOverlayGesture(); CancelRegionEditing(); _selectedClip = id; _selectedZoom = null; _selectedDecoration = null; RebuildInspector(); SyncSelection(); ApplyZoomVisual(); };
        Timeline.ZoomSelected += (_, id) => { CancelOverlayGesture(); CancelRegionEditing(); _selectedZoom = id; _selectedClip = null; _selectedDecoration = null; _tool = "zoom"; RebuildInspector(); SyncSelection(); ApplyZoomVisual(); };
        Timeline.DecorationSelected += (_, id) => SelectDecoration(id);
        Timeline.TrimFinished += (_, trim) =>
        {
            Push();
            if (TimelineEdits.TryTrim(_project, trim.ClipId, trim.SourceStart, trim.SourceEnd))
            {
                Commit();
            }
        };
        Timeline.ZoomFinished += (_, move) =>
        {
            Push();
            if (TimelineEdits.TryMoveZoom(_project, move.ZoomId, move.Start, move.Duration))
            {
                Commit();
            }
            else
            {
                _undo.Pop();
            }
        };
        Timeline.DecorationFinished += (_, move) =>
        {
            var timing = DecorationTiming(move.ZoomId);
            if (timing is null) return;
            Push();
            timing.Start = move.Start;
            timing.Duration = move.Duration;
            Commit();
        };
        Timeline.ClipMoved += (_, move) =>
        {
            Push();
            if (TimelineEdits.TryMoveClip(_project, move.ClipId, move.Index))
            {
                Commit();
            }
            else
            {
                _undo.Pop();
            }
        };
        Closed += (_, _) => { _saveTimer.Stop(); _player.Dispose(); };
    }

    public static void Open(string directory)
    {
        try
        {
            var window = new EditorWindow();
            window.Load(directory);
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "DogSC");
        }
    }

    void Load(string directory)
    {
        _directory = directory;
        _project = ProjectFile.Load(directory);
        _cursorTimeline = new CursorTimeline(_project);
        _stickerTrack = new StickerMotion.Track(_project.Canvas.Stickers);
        _selectedClip = _project.Clips.FirstOrDefault()?.Id;
        NameBox.Text = _project.Name;
        Title = "DogSC — " + _project.Name;
        UpdateThemeButton();
        FillAspects();
        Timeline.Project = _project;
        _player.Open(_project, directory);
        _player.SetGains(_project.MicrophoneGain, _project.SystemGain);
        SyncSelection();
        RebuildInspector();
        LayoutPreview();
        _ = LoadFramesAsync();
        _ = LoadWaveformsAsync();
    }

    void FillAspects()
    {
        AspectRow.Children.Clear();
        foreach (var aspect in new[] { ("适应", "source"), ("16:9", "16:9"), ("4:3", "4:3"), ("1:1", "1:1"), ("9:16", "9:16"), ("3:4", "3:4"), ("2.39:1", "2.39:1"), ("1:2.39", "1:2.39") })
        {
            var button = new Button { Content = aspect.Item1, Tag = aspect.Item2, Style = (Style)FindResource("ChipButton") };
            button.Click += (_, _) =>
            {
                Push();
                _project.Canvas.Aspect = (string)button.Tag;
                Commit();
            };
            AspectRow.Children.Add(button);
        }

        PaintAspects();
    }

    void PaintAspects()
    {
        foreach (var button in AspectRow.Children.OfType<Button>())
        {
            var selected = (string)button.Tag == _project.Canvas.Aspect;
            button.Background = (Brush)FindResource(selected ? "AccentBrush" : "WellBrush");
            button.Foreground = (Brush)FindResource(selected ? "OnAccentBrush" : "InkBrush");
        }

        PaintTools();
    }

    void PaintTools()
    {
        foreach (var button in new[] { ToolLayout, ToolZoom, ToolEffects, ToolAudio })
        {
            var selected = (string)button.Tag == _tool;
            button.Background = (Brush)FindResource(selected ? "AccentBrush" : "WellBrush");
            button.Foreground = (Brush)FindResource(selected ? "OnAccentBrush" : "InkBrush");
        }
    }

    void SetToolContent(Button button, IconKind kind, string label)
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        content.Children.Add(new Icon { Kind = kind, Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var text = new TextBlock { Text = label, FontSize = 12, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(text, 2);
        text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = button });
        content.Children.Add(text);
        button.Content = content;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
    }

    void UpdateThemeButton()
    {
        var dark = SettingsStore.Current.Theme == "dark";
        ThemeIcon.Kind = dark ? IconKind.Sun : IconKind.Moon;
        ThemeButton.ToolTip = dark ? "切换到浅色主题" : "切换到深色主题";
    }

    void RebuildInspector()
    {
        Inspector.Children.Clear();
        PaintTools();
        if (_tool == "zoom")
        {
            BuildZoomInspector();
        }
        else if (_tool == "effects")
        {
            BuildDecorations();
        }
        else if (_tool == "audio")
        {
            BuildAudioInspector();
        }
        else
        {
            BuildLayoutInspector();
        }
    }

    void BuildLayoutInspector()
    {
        if (_cameraSelected)
        {
            Inspector.Children.Add(DecorationButton("返回画面设置", () => { _cameraSelected = false; RebuildInspector(); ApplyZoomVisual(); }));
            BuildCameraInspector(); return;
        }
        Inspector.Children.Add(Heading("画面"));
        var presets = new StackPanel { Orientation = Orientation.Horizontal };
        AddPreset(presets, "干净", 0, 0, 0, "#F7F4EE");
        AddPreset(presets, "留白", 80, 16, 24, "#E7E2D8");
        AddPreset(presets, "圆角", 64, 42, 30, "#F3F1EC");
        AddPreset(presets, "深底", 72, 20, 0, "#1E1F22");
        Inspector.Children.Add(presets);
        BuildScenePresets();
        BuildCropControls();
        Inspector.Children.Add(Caption("边距"));
        Inspector.Children.Add(Slider(0, 180, _project.Canvas.Padding, value => _project.Canvas.Padding = value));
        Inspector.Children.Add(Caption("素材缩放"));
        Inspector.Children.Add(Slider(0.6, 1.8, _project.Canvas.Scale, value => _project.Canvas.Scale = value));
        Inspector.Children.Add(Caption("圆角"));
        Inspector.Children.Add(Slider(0, 90, _project.Canvas.Corner, value => _project.Canvas.Corner = value));
        Inspector.Children.Add(Caption("阴影"));
        Inspector.Children.Add(Slider(0, 100, _project.Canvas.Shadow, value => _project.Canvas.Shadow = value));
        Inspector.Children.Add(Caption("倾斜（左右）"));
        Inspector.Children.Add(Slider(-40, 40, _project.Canvas.TiltY, value => _project.Canvas.TiltY = value));
        Inspector.Children.Add(Caption("倾斜（上下）"));
        Inspector.Children.Add(Slider(-40, 40, _project.Canvas.TiltX, value => _project.Canvas.TiltX = value));
        Inspector.Children.Add(Caption("边缘虚化"));
        Inspector.Children.Add(Slider(0, 1, _project.Canvas.FocusBlur, value => _project.Canvas.FocusBlur = value));
        Inspector.Children.Add(Caption("开场"));
        var intros = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, key) in new[] { ("无", "none"), ("淡入", "fade"), ("上移", "rise"), ("放大", "zoom") })
        {
            var chip = new Button
            {
                Content = name,
                Tag = key,
                Style = (Style)FindResource("ChipButton"),
                Margin = new Thickness(0, 0, 4, 0),
            };
            chip.Click += (_, _) =>
            {
                Push();
                _project.Canvas.Intro = (string)chip.Tag;
                Commit();
            };
            intros.Children.Add(chip);
        }

        Inspector.Children.Add(intros);
        Inspector.Children.Add(Caption("光标"));
        var cursorRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, key) in new[] { ("箭头", "pointer"), ("圆点", "dot"), ("隐藏", "hidden") })
        {
            var chip = new Button
            {
                Content = name,
                Tag = key,
                Style = (Style)FindResource("ChipButton"),
                Margin = new Thickness(0, 0, 4, 0),
            };
            chip.Click += (_, _) =>
            {
                Push();
                _project.Canvas.Cursor.Mode = (string)chip.Tag;
                Commit();
            };
            cursorRow.Children.Add(chip);
        }

        Inspector.Children.Add(cursorRow);
        var ripple = new CheckBox
        {
            Content = "点击反馈圈",
            IsChecked = _project.Canvas.Cursor.ClickRipple,
            Foreground = (Brush)FindResource("InkBrush"),
            Margin = new Thickness(0, 6, 0, 0),
        };
        ripple.Click += (_, _) =>
        {
            Push();
            _project.Canvas.Cursor.ClickRipple = ripple.IsChecked == true;
            Commit();
        };
        Inspector.Children.Add(ripple);
        var idle = new CheckBox
        {
            Content = "静止不动时隐藏光标",
            IsChecked = _project.Canvas.Cursor.IdleHide,
            Foreground = (Brush)FindResource("InkBrush"),
        };
        idle.Click += (_, _) =>
        {
            Push();
            _project.Canvas.Cursor.IdleHide = idle.IsChecked == true;
            Commit();
        };
        Inspector.Children.Add(idle);
        BuildCursorDetails();
        Inspector.Children.Add(Caption("位置"));
        Inspector.Children.Add(PositionPad());
        Inspector.Children.Add(Caption("背景"));
        var colors = new WrapPanel();
        foreach (var color in new[] { "#F3F1EC", "#E7E2D8", "#F7F7F5", "#D9E4DD", "#1E1F22", "#2C3A4A" })
        {
            var swatch = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 8),
                Background = Brush(color),
                BorderThickness = new Thickness(1),
                Tag = color,
            };
            swatch.Click += (_, _) =>
            {
                Push();
                _project.Canvas.Background = (string)swatch.Tag;
                Commit();
            };
            colors.Children.Add(swatch);
        }

        Inspector.Children.Add(colors);

        // —— 画面装饰：壁纸 / 贴图 / 局部柔化 ——
        Inspector.Children.Add(Heading("装饰"));

        Inspector.Children.Add(Caption("壁纸图片"));
        var wallRow = new StackPanel { Orientation = Orientation.Horizontal };
        var wallPick = new Button { Content = string.IsNullOrWhiteSpace(_project.Canvas.Wallpaper) ? "选图…" : "换图…", Style = (Style)FindResource("QuietButton"), Margin = new Thickness(0, 0, 6, 0) };
        wallPick.Click += (_, _) => PickDecoration(path => _project.Canvas.Wallpaper = path);
        wallRow.Children.Add(wallPick);
        if (_project.Canvas.Wallpaper is not null)
        {
            var clear = new Button { Content = "清除", Style = (Style)FindResource("QuietButton") };
            clear.Click += (_, _) => { Push(); _project.Canvas.Wallpaper = null; Commit(); };
            wallRow.Children.Add(clear);
            Inspector.Children.Add(wallRow);
            Inspector.Children.Add(Caption("壁纸模糊"));
            Inspector.Children.Add(Slider(0, 1, _project.Canvas.WallpaperBlur, value => _project.Canvas.WallpaperBlur = value));
        }
        else
        {
            Inspector.Children.Add(wallRow);
        }


        if (_project.HasCamera) BuildCameraInspector();
    }

    void BuildCameraInspector()
    {
        Inspector.Children.Add(Heading("画中画"));
        BuildCameraDetails();
        BuildCameraContentControls();
        Inspector.Children.Add(Caption("形状"));
        var shapes = new WrapPanel();
        foreach (var (name, key) in new[] { ("圆形", "circle"), ("方形", "square"), ("横向", "horizontal"), ("纵向", "vertical"), ("原比例", "original"), ("直角", "none") })
        {
            var chip = new Button { Content = name, Tag = key, Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 0) };
            chip.Click += (_, _) => { Push(); _project.Camera.Shape = (string)chip.Tag; Commit(); };
            chip.Background = (Brush)FindResource(key == _project.Camera.Shape ? "AccentBrush" : "WellBrush");
            chip.Foreground = (Brush)FindResource(key == _project.Camera.Shape ? "OnAccentBrush" : "InkBrush");
            shapes.Children.Add(chip);
        }

        Inspector.Children.Add(shapes);
        Inspector.Children.Add(Caption("大小"));
        Inspector.Children.Add(Slider(0.08, 0.46, _project.Camera.Size, value => _project.Camera.Size = value));
        if (_project.Camera.Shape != "circle" && _project.Camera.Shape != "none")
        {
            Inspector.Children.Add(Caption("圆角"));
            Inspector.Children.Add(Slider(0, 0.5, _project.Camera.Corner, value => _project.Camera.Corner = value));
        }

        Inspector.Children.Add(Caption("位置预设"));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Width = 108, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (cx, cy, glyph, label) in new[] { (.12, .15, "↖", "左上"), (.5, .15, "↑", "上方居中"), (.88, .15, "↗", "右上"),
            (.12, .5, "←", "左侧居中"), (.5, .5, "•", "居中"), (.88, .5, "→", "右侧居中"),
            (.12, .85, "↙", "左下"), (.5, .85, "↓", "下方居中"), (.88, .85, "↘", "右下") })
        {
            var cell = new Button { Content = glyph, ToolTip = label, Padding = new Thickness(0), Width = 30, Height = 30, Tag = (cx, cy), Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 4) };
            System.Windows.Automation.AutomationProperties.SetName(cell, "摄像头位置：" + label);
            cell.Click += (_, _) =>
            {
                Push();
                var (x, y) = ((double, double))cell.Tag;
                _project.Camera.CenterX = x;
                _project.Camera.CenterY = y;
                Commit();
            };
            grid.Children.Add(cell);
        }

        Inspector.Children.Add(grid);
        Inspector.Children.Add(Caption("进出动画"));
        var entries = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, key) in new[] { ("无", "none"), ("淡入", "fade"), ("滑入", "rise") })
        {
            var chip = new Button { Content = name, Tag = key, Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 0) };
            chip.Click += (_, _) => { Push(); _project.Camera.Entry = (string)chip.Tag; Commit(); };
            chip.Background = (Brush)FindResource(key == _project.Camera.Entry ? "AccentBrush" : "WellBrush");
            chip.Foreground = (Brush)FindResource(key == _project.Camera.Entry ? "OnAccentBrush" : "InkBrush");
            entries.Children.Add(chip);
        }

        Inspector.Children.Add(entries);
        Inspector.Children.Add(DecorationButton("替换摄像头素材", SwapCamera));
    }

    /// <summary>Mac「替换摄像头」：换一个摄像头源文件，保留片段、效果与偏移。</summary>
    void SwapCamera()
    {
        var assetIds = _project.Clips.Select(item => item.AssetId).Distinct().ToArray();
        if (assetIds.Length == 0)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "视频|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.wmv;*.m4v|全部|*.*",
            Title = "替换摄像头素材",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var file = Path.Combine(_directory, "media", "camera-" + Guid.NewGuid().ToString("N") + Path.GetExtension(dialog.FileName));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(dialog.FileName, file, overwrite: true);
            Push();
            var relative = "media/" + Path.GetFileName(file);
            foreach (var assetId in assetIds)
            {
                MediaSwap.Replace(_project, assetId, MediaSwap.Track.Camera, relative);
            }
            Commit();
        }
        catch (Exception ex)
        {
            TimeLabel.Text = "替换失败：" + ex.Message;
        }
    }

    /// <summary>选图后拷进项目 media/decor/ 并写入相对路径，保证项目可搬移。</summary>
    void PickDecoration(Action<string> assign) => PickDecoration((path, _) => assign(path));

    void PickDecoration(Action<string, string> assign)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp",
            Title = "选择图片",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var target = Path.Combine("media", "decor");
            var folder = Path.Combine(_directory, target);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, Guid.NewGuid().ToString("N") + Path.GetExtension(dialog.FileName));
            File.Copy(dialog.FileName, file, overwrite: true);
            Push();
            assign((target + "/" + Path.GetFileName(file)).Replace('\\', '/'), Path.GetFileName(dialog.FileName));
            Commit();
        }
        catch (Exception ex)
        {
            AppPaths.Log("装饰图导入失败：" + ex.Message);
        }
    }

    (Slider X, Slider Y)? _zoomFocusSliders;

    void MakeZoomManual(ZoomSpan zoom)
    {
        if (zoom.Origin != "automatic") return;
        var target = _cursorTimeline!.Motion.TargetAt(zoom, _player.Time);
        zoom.FocusX = target.X; zoom.FocusY = target.Y; zoom.Origin = "manual";
    }

    void BuildZoomInspector()
    {
        _zoomFocusSliders = null;
        Inspector.Children.Add(Heading("缩放"));
        var auto = new Button { Content = "按点击自动运镜", Style = (Style)FindResource("QuietButton"), HorizontalAlignment = HorizontalAlignment.Left };
        auto.Click += (_, _) =>
        {
            Push();
            var added = TimelineEdits.AutoZoom(_project);
            if (added == 0)
            {
                _undo.Pop();
                Inspector.Children.Add(Caption("没有空闲时段的点击点。可检查指针轨迹，或移动已有手动缩放。"));
                return;
            }

            Commit();
        };
        Inspector.Children.Add(auto);
        var add = new Button { Content = "在播放头添加缩放", Style = (Style)FindResource("QuietButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        add.Click += (_, _) =>
        {
            Push();
            var zoom = TimelineEdits.AddZoom(_project, _player.Time);
            if (zoom is null)
            {
                _undo.Pop();
                return;
            }

            _selectedZoom = zoom.Id;
            _selectedClip = null;
            Commit();
        };
        Inspector.Children.Add(add);
        var zoom = _project.Zooms.FirstOrDefault(item => item.Id == _selectedZoom);
        if (zoom is null)
        {
            Inspector.Children.Add(Caption("选中时间线上的一段缩放后，可以改倍数和焦点。"));
            return;
        }

        Inspector.Children.Add(Caption(zoom.Origin == "automatic" ? "自动跟随：附近点击保持构图，跨区域点击平滑移动。改焦点会转为手动镜头。" : "手动镜头：相邻缩放直接衔接，空白处恢复全景。"));
        Inspector.Children.Add(Caption("倍数"));
        Inspector.Children.Add(Slider(1, 3, zoom.Scale, value => zoom.Scale = value));
        var target = zoom.Origin == "automatic" ? _cursorTimeline!.Motion.TargetAt(zoom, _player.Time)
            : new ZoomPose(zoom.Scale, zoom.FocusX, zoom.FocusY);
        var focusX = Slider(0, 1, target.X, value => { MakeZoomManual(zoom); zoom.FocusX = value; }, refresh: true);
        var focusY = Slider(0, 1, target.Y, value => { MakeZoomManual(zoom); zoom.FocusY = value; }, refresh: true);
        _zoomFocusSliders = (focusX, focusY);
        Inspector.Children.Add(Caption("焦点 X"));
        Inspector.Children.Add(focusX);
        Inspector.Children.Add(Caption("焦点 Y"));
        Inspector.Children.Add(focusY);
        Inspector.Children.Add(MotionSlider("进入时长", 0, 5, zoom.EnterDuration ?? .4, value => zoom.EnterDuration = value, " 秒"));
        Inspector.Children.Add(MotionSlider("退出时长", 0, 5, zoom.ExitDuration ?? .4, value => zoom.ExitDuration = value, " 秒"));
        Inspector.Children.Add(Caption("片段不足以容纳两段过渡时，按比例缩短；相邻镜头直接衔接。"));
        var curves = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, curve) in new[] { ("弹簧", "spring"), ("平滑", "smooth"), ("线性", "linear") })
        {
            var button = new Button { Content = label, Style = (Style)FindResource("QuietButton"), Margin = new Thickness(0, 4, 6, 4),
                Background = zoom.Easing == curve ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("WellBrush"),
                Foreground = (Brush)FindResource(zoom.Easing == curve ? "OnAccentBrush" : "InkBrush") };
            button.Click += (_, _) => { Push(); zoom.Easing = curve; Commit(); };
            curves.Children.Add(button);
        }
        Inspector.Children.Add(curves);
        if (zoom.Easing == "spring")
        {
            var spring = Disclosure("弹簧参数");
            spring.Children.Add(MotionSlider("质量", .1, 10, zoom.SpringMass, value => zoom.SpringMass = value));
            spring.Children.Add(MotionSlider("刚度", 10, 500, zoom.SpringStiffness, value => zoom.SpringStiffness = value));
            spring.Children.Add(MotionSlider("阻尼", 1, 100, zoom.SpringDamping, value => zoom.SpringDamping = value));
        }
        Inspector.Children.Add(Caption("在「缩放」工具下点击画面，也会把焦点放到点击的位置。"));
    }

    StackPanel MotionSlider(string name, double min, double max, double value, Action<double> apply, string unit = "")
    {
        var panel = new StackPanel();
        var label = Caption(name + "  " + value.ToString("0.##", CultureInfo.InvariantCulture) + unit);
        var slider = Slider(min, max, value, v =>
        {
            apply(v);
            label.Text = name + "  " + v.ToString("0.##", CultureInfo.InvariantCulture) + unit;
        });
        System.Windows.Automation.AutomationProperties.SetName(slider, name);
        panel.Children.Add(label); panel.Children.Add(slider);
        return panel;
    }

    void BuildAudioInspector()
    {
        Inspector.Children.Add(Heading("声音"));
        Inspector.Children.Add(Caption("麦克风"));
        Inspector.Children.Add(Slider(0, ProjectDocument.MaxGain, _project.MicrophoneGain, value =>
        {
            _project.MicrophoneGain = value;
            _player.SetGains(_project.MicrophoneGain, _project.SystemGain);
        }, refresh: true));
        Inspector.Children.Add(Caption("系统声音"));
        Inspector.Children.Add(Slider(0, ProjectDocument.MaxGain, _project.SystemGain, value =>
        {
            _project.SystemGain = value;
            _player.SetGains(_project.MicrophoneGain, _project.SystemGain);
        }, refresh: true));
        var recorded = _project.Assets.Any(asset => asset.Microphone is not null || asset.SystemAudio is not null);
        Inspector.Children.Add(Caption(recorded ? "两条声音会在导出时混合。这里的音量只影响预览和导出。" : "这个项目没有单独的声音轨。"));
        // 预览器的音量上限是 100%，超过的部分只在导出时生效，提前说明免得以为没生效。
        if (_project.MicrophoneGain > 1 || _project.SystemGain > 1)
        {
            Inspector.Children.Add(Caption("预览最多播放到 100%；超过的部分以导出为准。"));
        }
    }

    UIElement PositionPad()
    {
        var canvas = new Canvas { Width = 200, Height = 120, Background = (Brush)FindResource("WellBrush") };
        var dot = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = (Brush)FindResource("AccentBrush") };
        canvas.Children.Add(dot);
        void Place()
        {
            Canvas.SetLeft(dot, _project.Canvas.CenterX * (canvas.Width - 14));
            Canvas.SetTop(dot, _project.Canvas.CenterY * (canvas.Height - 14));
        }

        Place();
        canvas.MouseLeftButtonDown += (_, args) =>
        {
            Push();
            MovePad(canvas, args.GetPosition(canvas));
            canvas.CaptureMouse();
        };
        canvas.MouseMove += (_, args) =>
        {
            if (args.LeftButton == MouseButtonState.Pressed)
            {
                MovePad(canvas, args.GetPosition(canvas));
            }
        };
        canvas.MouseLeftButtonUp += (_, _) => canvas.ReleaseMouseCapture();
        return canvas;

        void MovePad(Canvas pad, Point point)
        {
            _project.Canvas.CenterX = Math.Clamp(point.X / pad.Width, 0, 1);
            _project.Canvas.CenterY = Math.Clamp(point.Y / pad.Height, 0, 1);
            Place();
            Commit(rebuild: false);
        }
    }

    Slider Slider(double min, double max, double value, Action<double> apply, bool refresh = false)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Margin = new Thickness(0, 0, 0, 4) };
        var changing = false;
        slider.ValueChanged += (_, args) =>
        {
            if (_lock)
            {
                return;
            }

            if (!changing)
            {
                Push();
                changing = true;
            }
            apply(args.NewValue);
            Commit(rebuild: false);
        };
        slider.PreviewMouseLeftButtonUp += (_, _) => Finish();
        slider.LostMouseCapture += (_, _) => Finish();
        slider.PreviewKeyUp += (_, _) => Finish();
        slider.LostKeyboardFocus += (_, _) => Finish();
        return slider;

        void Finish()
        {
            if (!changing) return;
            changing = false;
            FlushPendingSave();
            if (refresh) RebuildInspector();
        }
    }

    void AddPreset(Panel panel, string name, double padding, double corner, double shadow, string background)
    {
        var button = new Button { Content = name, Style = (Style)FindResource("ChipButton") };
        button.Click += (_, _) =>
        {
            Push();
            _project.Canvas.Padding = padding;
            _project.Canvas.Corner = corner;
            _project.Canvas.Shadow = shadow;
            _project.Canvas.Background = background;
            _project.Canvas.Scale = 1;
            _project.Canvas.CenterX = 0.5;
            _project.Canvas.CenterY = 0.5;
            Commit();
        };
        panel.Children.Add(button);
    }

    TextBlock Heading(string text) => new() { Text = text, FontFamily = (FontFamily)FindResource("FontSemi"), FontSize = 16, Margin = new Thickness(0, 0, 0, 8) };

    TextBlock Caption(string text) => new() { Text = text, Foreground = (Brush)FindResource("InkSecondaryBrush"), Margin = new Thickness(0, 10, 0, 4), TextWrapping = TextWrapping.Wrap };

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    void LayoutPreview()
    {
        if (_project.Width < 2 || PreviewSlot.ActualWidth < 40 || PreviewSlot.ActualHeight < 40)
        {
            return;
        }

        var (outputWidth, outputHeight) = CanvasLayout.OutputSize(_project.Width, _project.Height, _project.Canvas.Aspect, "source");
        var scale = Math.Min(PreviewSlot.ActualWidth / outputWidth, PreviewSlot.ActualHeight / outputHeight);
        var viewWidth = outputWidth * scale;
        var viewHeight = outputHeight * scale;
        PreviewCanvas.Width = viewWidth;
        PreviewCanvas.Height = viewHeight;
        SceneCanvas.Width = viewWidth;
        SceneCanvas.Height = viewHeight;
        StageRect.Width = viewWidth;
        StageRect.Height = viewHeight;
        StageRect.Fill = Brush(_project.Canvas.Background);
        UpdateWallpaper(viewWidth, viewHeight);
        UpdateSticker(viewWidth, viewHeight, outputWidth, outputHeight);

        var frame = CanvasLayout.Place(outputWidth, outputHeight, _project.Width, _project.Height, _project.Canvas);
        var frameScale = viewWidth / outputWidth;
        var frameWidth = Math.Max(2, frame.Width * frameScale);
        var frameHeight = Math.Max(2, frame.Height * frameScale);
        Canvas.SetLeft(FrameHost, frame.X * frameScale);
        Canvas.SetTop(FrameHost, frame.Y * frameScale);
        FrameHost.Width = frameWidth;
        FrameHost.Height = frameHeight;
        var crop = _project.Canvas.Crop;
        if (FrameRect.Fill is DrawingBrush screenBrush)
        {
            screenBrush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
            screenBrush.Viewbox = new Rect(crop.X, crop.Y, crop.Width, crop.Height);
        }
        CursorLayer.Width = frameWidth;
        CursorLayer.Height = frameHeight;
        var radius = frame.Radius * frameScale;
        ScreenTexture.Width = frameWidth;
        ScreenTexture.Height = frameHeight;
        ScreenTexture.Clip = new RectangleGeometry(new Rect(0, 0, frameWidth, frameHeight), radius, radius);
        ShadowRect.Width = frameWidth;
        ShadowRect.Height = frameHeight;
        ShadowRect.RadiusX = radius;
        ShadowRect.RadiusY = radius;
        Canvas.SetLeft(ShadowRect, frame.X * frameScale + frame.ShadowOffset * frameScale * 0.35);
        Canvas.SetTop(ShadowRect, frame.Y * frameScale + frame.ShadowOffset * frameScale);
        ShadowRect.Opacity = frame.ShadowAlpha;
        if (ShadowRect.Effect is BlurEffect blur)
        {
            blur.Radius = Math.Max(0, frame.ShadowBlur * frameScale);
        }

        if (_project.HasCamera && !_project.Camera.Hidden)
        {
            var bubble = CanvasLayout.Bubble(outputWidth, outputHeight, _project.Camera, _cameraMaskAspect ?? CameraSourceAspect);
            CameraBubble.Visibility = Visibility.Visible;
            CameraBubble.Width = bubble.Width * frameScale; CameraBubble.Height = bubble.Height * frameScale;
            Canvas.SetLeft(CameraBubble, bubble.X * frameScale); Canvas.SetTop(CameraBubble, bubble.Y * frameScale);
            var side = Math.Min(CameraBubble.Width, CameraBubble.Height);
            var camRadius = _project.Camera.Shape == "circle" ? side / 2 : _project.Camera.Shape == "none" ? 0 : side * _project.Camera.Corner;
            CameraBubble.RadiusX = CameraBubble.RadiusY = camRadius;
            UpdateCameraContent();
        }
        else
        {
            CameraBubble.Visibility = Visibility.Collapsed;
        }

        CanvasEffects.Width = viewWidth;
        CanvasEffects.Height = viewHeight;
        StickerLayers.Width = viewWidth;
        StickerLayers.Height = viewHeight;
        _sourceRegions.Rebuild(SourceEffects, FrameRect, _project.Canvas.SoftRegions.Where(x => x.Enabled && x.CoordinateSpace == "source"),
            frameWidth, frameHeight, _project.Width, _project.Height, crop);
        _canvasRegions.Rebuild(CanvasEffects, SceneCanvas, _project.Canvas.SoftRegions.Where(x => x.Enabled && x.CoordinateSpace == "canvas"),
            viewWidth, viewHeight, outputWidth, outputHeight, new(), _project.Canvas.FocusBlur);
        ApplyZoomVisual();
    }

    void ApplyZoomVisual()
    {
        var editingTarget = !_player.IsPlaying && _tool == "zoom" ? _project.Zooms.FirstOrDefault(item => item.Id == _selectedZoom) : null;
        var pose = _cursorTimeline?.Motion.At(_player.Time) ?? ZoomPose.Rest;
        var scale = pose.Scale;
        var (fx, fy) = (pose.X, pose.Y);
        if (editingTarget is not null)
        {
            var target = editingTarget.Origin == "automatic" ? _cursorTimeline!.Motion.TargetAt(editingTarget, _player.Time)
                : new ZoomPose(editingTarget.Scale, editingTarget.FocusX, editingTarget.FocusY);
            scale = target.Scale; fx = target.X; fy = target.Y;
            if (editingTarget.Origin == "automatic" && _zoomFocusSliders is { } controls
                && !controls.X.IsMouseCaptureWithin && !controls.Y.IsMouseCaptureWithin
                && !controls.X.IsKeyboardFocusWithin && !controls.Y.IsKeyboardFocusWithin)
            {
                var wasLocked = _lock;
                _lock = true;
                controls.X.Value = fx; controls.Y.Value = fy;
                _lock = wasLocked;
            }
        }
        SourceEffects.RenderTransformOrigin = new Point(fx, fy);

        SourceEffects.RenderTransform = scale > 1.001 ? new ScaleTransform(scale, scale) : Transform.Identity;
        UpdatePerspective();
        CursorLayer.RenderTransform = SourceEffects.RenderTransform;
        CursorLayer.RenderTransformOrigin = SourceEffects.RenderTransformOrigin;

        // 开场动画：fade=淡入，rise=淡入+上移，zoom=淡入+轻微放大。
        var intro = EditingRegions ? 1 : CanvasLayout.IntroProgress(_project.Canvas, _player.Time);
        FrameHost.Opacity = intro;
        FrameHost.RenderTransform = Transform.Identity;
        if (_project.Canvas.Intro == "rise" && intro < 1)
        {
            FrameHost.RenderTransform = new TranslateTransform(0, (1 - intro) * FrameHost.Height * 0.08);
        }
        else if (_project.Canvas.Intro == "zoom" && intro < 1)
        {
            var zoomIn = 0.9 + 0.1 * intro;
            FrameHost.RenderTransform = new ScaleTransform(zoomIn, zoomIn,
                FrameHost.Width / 2, FrameHost.Height / 2);
        }

        // 摄像头进出动画：fade=淡入淡出，rise=淡入+底部滑入（对齐导出端 0.5s 窗口近似）。
        UpdateCameraEntry();

        UpdateCursorLayer();
        UpdateDecorationTiming();
        RegionEditor.Width = PreviewCanvas.Width; RegionEditor.Height = PreviewCanvas.Height;
        RegionEditor.Visibility = EditingRegions ? Visibility.Visible : Visibility.Collapsed;
        RegionEditor.InvalidateVisual();
        UpdateOverlayEditor();
    }

    /// <summary>画中画进出场预览：按播放头相对片段首尾的进度做透明度/位移。</summary>
    void UpdateCameraEntry()
    {
        if (!_project.HasCamera || CameraBubble.Visibility != Visibility.Visible)
        {
            return;
        }

        var entry = _project.Camera.Entry;
        var clip = TimelineMath.Place(_project).FirstOrDefault(c => _player.Time >= c.Start && _player.Time < c.End);
        var source = clip is null ? 0 : clip.Clip.SourceStart + (_player.Time - clip.Start) * clip.Clip.Speed;
        if (clip is null || clip.Asset.Camera is null || source + clip.Asset.CameraOffset + _project.Camera.TimingAdvance < 0)
        {
            CameraBubble.Opacity = 0;
            return;
        }
        if (!_player.IsPlaying && _tool == "layout") { CameraBubble.Opacity = 1; CameraBubble.RenderTransform = Transform.Identity; return; }
        var anim = 0.5;
        double opacity = 1, rise = 0;
        if (clip is not null && entry is "fade" or "rise")
        {
            var into = Math.Clamp((_player.Time - clip.Start) / anim, 0, 1);
            var outOf = Math.Clamp((clip.End - _player.Time) / anim, 0, 1);
            opacity = Math.Min(into, outOf);
            if (entry == "rise")
            {
                rise = (1 - into) * CameraBubble.Height * 0.4;
            }
        }

        CameraBubble.Opacity = opacity;
        CameraBubble.RenderTransform = rise > 0.5 ? new TranslateTransform(0, rise) : Transform.Identity;
    }

    /// <summary>解析壁纸/贴图路径：项目相对或绝对；文件不存在返回 null。</summary>
    string? DecorationPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _directory.Length == 0)
        {
            return null;
        }

        var trimmed = path.Trim();
        var absolute = Path.IsPathRooted(trimmed) ? trimmed : ProjectFile.Media(_directory, trimmed);
        return File.Exists(absolute) ? absolute : null;
    }

    /// <summary>壁纸预览：铺满 StageRect 的 ImageBrush，可选模糊。</summary>
    void UpdateWallpaper(double viewWidth, double viewHeight)
    {
        var path = DecorationPath(_project.Canvas.Wallpaper);
        if (path is null)
        {
            WallpaperImage.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            // 路径变了才重新解码，避免每次布局都读盘。
            if (!Equals(WallpaperImage.Tag, path))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                WallpaperImage.Source = image;
                WallpaperImage.Tag = path;
            }

            WallpaperImage.Width = viewWidth;
            WallpaperImage.Height = viewHeight;
            Canvas.SetLeft(WallpaperImage, 0);
            Canvas.SetTop(WallpaperImage, 0);
            WallpaperBlur.Radius = _project.Canvas.WallpaperBlur * 22;
            WallpaperImage.Visibility = Visibility.Visible;
        }
        catch
        {
            WallpaperImage.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>贴图预览：按归一化矩形摆一张图。</summary>
    void UpdateSticker(double viewWidth, double viewHeight, int outputWidth, int outputHeight)
    {
        _stickerTrack ??= new StickerMotion.Track(_project.Canvas.Stickers);
        var viewScale = viewWidth / outputWidth;
        var (samples, backdrop) = _stickerTrack.SampleAt(_player.Time);
        var byId = samples.ToDictionary(s => s.Clip.Id);
        // 背景通道：压屏幕/虚化屏幕作用于画面矩形，压/虚化摄像头作用于气泡。
        FrameRect.Opacity = 1 - backdrop.ScreenSuppression;
        FrameRect.Effect = backdrop.ScreenBlur > 0.01
            ? new BlurEffect { Radius = backdrop.ScreenBlur * viewScale }
            : null;
        CameraBubble.Opacity = _project.Camera.Hidden ? 0 : 1 - backdrop.CameraSuppression;
        CameraBubble.Effect = backdrop.CameraBlur > 0.01
            ? new BlurEffect { Radius = backdrop.CameraBlur * viewScale }
            : null;
        foreach (var id in _stickerImages.Keys.Where(id => !_project.Canvas.Stickers.Any(x => x.Id == id)).ToArray())
        {
            StickerLayers.Children.Remove(_stickerImages[id]);
            _stickerImages.Remove(id);
        }
        foreach (var sticker in _project.Canvas.Stickers)
        {
            if (!_stickerImages.TryGetValue(sticker.Id, out var holder))
            {
                holder = new Border { Child = new Image { Stretch = Stretch.Fill } };
                _stickerImages[sticker.Id] = holder;
                StickerLayers.Children.Add(holder);
            }
            var image = (Image)holder.Child;
            Panel.SetZIndex(holder, _project.Canvas.Stickers.IndexOf(sticker));
            var path = DecorationPath(sticker.Path);
            holder.Visibility = path is null ? Visibility.Collapsed : Visibility.Visible;
            if (path is null) continue;
            if (!Equals(image.Tag, path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
                image.Source = bitmap; image.Tag = path;
            }
            var aspect = image.Source is BitmapSource bmp ? bmp.PixelWidth / (double)bmp.PixelHeight : 1;
            var box = CanvasLayout.Sticker(outputWidth, outputHeight, sticker, aspect);
            holder.Width = CanvasLayout.Even((int)Math.Round(box.Width)) * viewScale;
            holder.Height = CanvasLayout.Even((int)Math.Round(box.Height)) * viewScale;
            // Mac 外观参数：圆角 / 描边 / 投影（像素值跟随画布缩放）。
            holder.CornerRadius = new CornerRadius(Math.Max(0, sticker.CornerRadius) * viewScale);
            holder.BorderThickness = new Thickness(Math.Max(0, sticker.BorderWidth) * viewScale);
            if (sticker.BorderWidth > 0)
            {
                var bc = (Color)ColorConverter.ConvertFromString(
                    string.IsNullOrWhiteSpace(sticker.BorderColor) ? "#FFFFFF" : sticker.BorderColor);
                holder.BorderBrush = new SolidColorBrush(bc);
            }
            else holder.BorderBrush = null;
            holder.Effect = sticker.ShadowOpacity > 0.001 ? new DropShadowEffect
            {
                Opacity = sticker.ShadowOpacity,
                BlurRadius = Math.Max(0, sticker.ShadowRadius) * viewScale,
                Direction = Math.Atan2(sticker.ShadowOffsetY, sticker.ShadowOffsetX) * 180 / Math.PI,
                ShadowDepth = Math.Sqrt(sticker.ShadowOffsetX * sticker.ShadowOffsetX
                    + sticker.ShadowOffsetY * sticker.ShadowOffsetY) * viewScale,
                Color = Colors.Black,
            } : null;
            // 运动采样：透明度 + 缩放 + 归一化位移（转到画布像素）。
            var sample = byId.GetValueOrDefault(sticker.Id);
            holder.Opacity = sticker.Opacity * sample.Visibility;
            var (scale, offsetX, offsetY) = StickerMotion.Transform(
                sample.Preset, sample.Progress, sticker.X, sticker.Y, sticker.Width);
            var motion = new TransformGroup();
            motion.Children.Add(new ScaleTransform(scale, scale));
            motion.Children.Add(new RotateTransform(sticker.Rotation));
            motion.Children.Add(new TranslateTransform(offsetX * outputWidth * viewScale,
                offsetY * outputHeight * viewScale));
            holder.RenderTransformOrigin = new Point(.5, .5);
            holder.RenderTransform = motion;
            Canvas.SetLeft(holder, box.X * viewScale - holder.Width / 2);
            Canvas.SetTop(holder, box.Y * viewScale - holder.Height / 2);
        }
    }

    /// <summary>按轨迹回放光标：位置/点击圈/静止隐藏，帧内坐标映射到 FrameHost。</summary>
    void UpdateCursorLayer()
    {
        CursorShape.Visibility = CursorDot.Visibility = ClickRing.Visibility = Visibility.Collapsed;
        var fx = _project.Canvas.Cursor;
        var sample = _cursorTimeline?.At(_player.Time) ?? default;
        var crop = _project.Canvas.Crop;
        var source = CanvasLayout.Crop(_project.Width, _project.Height, crop);
        var displayScale = FrameHost.ActualWidth / source.Width;
        var size = Math.Max(8, (int)Math.Round(18 * fx.Size));
        var dot = fx.Mode == "dot";
        if (_cursorBitmapKey != (fx.Mode, fx.Size))
        {
            CursorBitmap.Source = BitmapSource.Create(size, dot ? size : size * 3 / 2, 96, 96, PixelFormats.Bgra32,
                null, MaskImage.CreateCursor(size, dot), size * 4);
            _cursorBitmapKey = (fx.Mode, fx.Size);
        }
        CursorBitmap.Visibility = sample.Pointer is null ? Visibility.Collapsed : Visibility.Visible;
        if (sample.Pointer is { } point)
        {
            CursorBitmap.Width = size * displayScale;
            CursorBitmap.Height = (dot ? size : size * 3 / 2) * displayScale;
            Canvas.SetLeft(CursorBitmap, (point.X - crop.X) / crop.Width * FrameHost.ActualWidth - (dot ? CursorBitmap.Width / 2 : 0));
            Canvas.SetTop(CursorBitmap, (point.Y - crop.Y) / crop.Height * FrameHost.ActualHeight - (dot ? CursorBitmap.Height / 2 : 0));
        }
        ClickBitmap.Visibility = sample.Click is null ? Visibility.Collapsed : Visibility.Visible;
        if (sample.Click is { } click)
        {
            var ringSize = CanvasLayout.Even((int)Math.Ceiling(44 * fx.ClickScale + 4));
            ClickBitmap.Source = BitmapSource.Create(ringSize, ringSize, 96, 96, PixelFormats.Bgra32,
                null, MaskImage.CreateClickFrame(ringSize, sample.Progress, fx), ringSize * 4);
            ClickBitmap.Width = ClickBitmap.Height = ringSize * displayScale;
            Canvas.SetLeft(ClickBitmap, (click.X - crop.X) / crop.Width * FrameHost.ActualWidth - ClickBitmap.Width / 2);
            Canvas.SetTop(ClickBitmap, (click.Y - crop.Y) / crop.Height * FrameHost.ActualHeight - ClickBitmap.Height / 2);
        }
    }
    void SyncPlayback()
    {
        Timeline.Playhead = _player.Time;
        TimeLabel.Text = Clock(_player.Time) + " / " + Clock(TimelineMath.Duration(_project));
        PlayIcon.Kind = _player.IsPlaying ? IconKind.Pause : IconKind.Play;
        PlayButton.ToolTip = _player.IsPlaying ? "暂停（Space）" : "播放（Space）";
        System.Windows.Automation.AutomationProperties.SetName(PlayButton, _player.IsPlaying ? "暂停" : "播放");
        ApplyZoomVisual();
        UpdateDecorationTiming();
    }

    void SyncSelection()
    {
        Timeline.SelectedClipId = _selectedClip;
        Timeline.SelectedZoomId = _selectedZoom;
        Timeline.SelectedDecorationId = _selectedDecoration;
        _lock = true;
        var clip = _project.Clips.FirstOrDefault(item => item.Id == _selectedClip);
        SpeedBox.IsEnabled = clip is not null;
        var label = (clip?.Speed ?? 1).ToString("0.##", CultureInfo.InvariantCulture) + "×";
        SpeedBox.SelectedItem = label;
        RestoreButton.IsEnabled = RestorableGapForSelection();
        _lock = false;
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    void Commit(bool rebuild = true)
    {
        ProjectFile.Normalize(_project);
        _cursorTimeline = new CursorTimeline(_project);
        _stickerTrack = new StickerMotion.Track(_project.Canvas.Stickers);
        _pendingSave = true;
        _saveTimer.Stop();
        _saveTimer.Start();
        Timeline.Project = _project;
        _player.UpdateProject(_project);
        SyncSelection();
        if (rebuild)
        {
            RebuildInspector();
        }

        PaintAspects();
        LayoutPreview();
        SyncPlayback();
    }

    void FlushPendingSave()
    {
        _saveTimer.Stop();
        if (!_pendingSave) return;
        ProjectFile.Save(_directory, _project);
        _pendingSave = false;
    }

    void Push(string? snapshot = null)
    {
        _undo.Push(snapshot ?? JsonSerializer.Serialize(_project, ProjectSchema.Json));
        if (_undo.Count > 40)
        {
            var kept = _undo.Take(40).Reverse().ToArray();
            _undo.Clear();
            foreach (var item in kept)
            {
                _undo.Push(item);
            }
        }

        _redo.Clear();
    }

    void Restore(string json)
    {
        CancelOverlayGesture(); CancelRegionEditing();
        var previousRegions = _project.Canvas.SoftRegions.Select(r => r.Id).Concat(_project.Canvas.Stickers.Select(r => r.Id)).ToHashSet();
        _project = JsonSerializer.Deserialize<ProjectDocument>(json, ProjectSchema.Json) ?? _project;
        ProjectFile.Normalize(_project);
        _selectedDecoration = DecorationTiming(_selectedDecoration ?? "") is not null ? _selectedDecoration : null;
        _selectedDecoration ??= _project.Canvas.SoftRegions.Select(r => r.Id).Concat(_project.Canvas.Stickers.Select(r => r.Id)).LastOrDefault(id => !previousRegions.Contains(id));
        _selectedZoom = _project.Zooms.FirstOrDefault(item => item.Id == _selectedZoom)?.Id;
        _selectedClip = _selectedDecoration is null && _selectedZoom is null
            ? _project.Clips.FirstOrDefault(item => item.Id == _selectedClip)?.Id ?? _project.Clips.FirstOrDefault()?.Id
            : null;
        Commit();
        _player.Seek(Math.Min(_player.Time, TimelineMath.Duration(_project)), play: false);
    }

    void Tool_Click(object sender, RoutedEventArgs e)
    {
        var tool = (string)((Button)sender).Tag;
        if (tool == _tool && !_cameraSelected) return;
        _cameraSelected = false;
        CancelOverlayGesture(); CancelRegionEditing();
        _tool = tool;
        if (tool == "effects") _player.Pause();
        RebuildInspector();
        ApplyZoomVisual();
        Motion.Reveal(Inspector);
    }

    void Name_LostFocus(object sender, RoutedEventArgs e)
    {
        var name = ProjectFile.SanitizeName(NameBox.Text);
        if (name == _project.Name)
        {
            return;
        }

        Push();
        _project.Name = name;
        NameBox.Text = name;
        Title = "DogSC — " + name;
        Commit(rebuild: false);
    }

    void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(JsonSerializer.Serialize(_project, ProjectSchema.Json));
        Restore(_undo.Pop());
    }

    void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(JsonSerializer.Serialize(_project, ProjectSchema.Json));
        Restore(_redo.Pop());
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        FlushPendingSave();
        new ExportWindow(_project, _directory) { Owner = this }.ShowDialog();
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        var next = SettingsStore.Current.Theme == "dark" ? "light" : "dark";
        ThemeService.Apply(next);
        SettingsStore.Save();
        UpdateThemeButton();
        PaintAspects();
        RebuildInspector();
    }

    void Split_Click(object sender, RoutedEventArgs e)
    {
        Push();
        if (!TimelineEdits.TrySplit(_project, _player.Time))
        {
            _undo.Pop();
            return;
        }

        Commit();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        Push();
        var changed = _selectedDecoration is not null ? DeleteDecoration(_selectedDecoration) : _selectedZoom is not null
            ? TimelineEdits.TryDeleteZoom(_project, _selectedZoom)
            : _selectedClip is not null && TimelineEdits.TryDeleteClip(_project, _selectedClip);
        if (!changed)
        {
            _undo.Pop();
            return;
        }

        _selectedDecoration = null;
        _selectedZoom = null;
        _selectedClip = _project.Clips.FirstOrDefault()?.Id;
        Commit();
    }

    void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClip is null)
        {
            return;
        }

        var index = _project.Clips.FindIndex(item => item.Id == _selectedClip);
        if (index < 0)
        {
            return;
        }

        var clips = _project.Clips;
        var clip = clips[index];
        var sourceDuration = _project.Asset(clip.AssetId)?.Duration ?? 0;
        Push();
        // 优先还原选中片段后面的缺口，其次前面，再片头 / 片尾。
        var restored =
            (index + 1 < clips.Count && GapRestore.RestoreGap(_project, clip.Id, clips[index + 1].Id))
            || (index > 0 && GapRestore.RestoreGap(_project, clips[index - 1].Id, clip.Id))
            || (index == 0 && GapRestore.RestoreLeading(_project))
            || (index == clips.Count - 1 && GapRestore.RestoreTrailing(_project, sourceDuration));
        if (!restored)
        {
            _undo.Pop();
            return;
        }

        Commit();
    }

    bool RestorableGapForSelection()
    {
        if (_selectedClip is null)
        {
            return false;
        }

        var index = _project.Clips.FindIndex(item => item.Id == _selectedClip);
        if (index < 0)
        {
            return false;
        }

        var clips = _project.Clips;
        var clip = clips[index];
        var sourceDuration = _project.Asset(clip.AssetId)?.Duration ?? 0;
        return (index + 1 < clips.Count && GapRestore.RestorableGap(_project, clip.Id) > 0)
            || (index > 0 && GapRestore.RestorableGap(_project, clips[index - 1].Id) > 0)
            || (index == 0 && clip.SourceStart > 0)
            || (index == clips.Count - 1 && sourceDuration - clip.SourceEnd > 0);
    }

    void Marker_Click(object sender, RoutedEventArgs e)
    {
        Push();
        if (TimelineEdits.AddMarker(_project, _player.Time, null) is null)
        {
            _undo.Pop();
            return;
        }

        Commit(rebuild: false);
    }

    // 素材效果包含项目内图片路径；独立缩放不引用媒体，可跨窗口复用。
    string? _effectClipboard;
    static ZoomSpan? _zoomClipboard;

    void CopyEffect_Click(object sender, RoutedEventArgs e)
    {
        if (CopyDecoration()) return;
        _decorationClipboard = null;
        if (_project.Zooms.FirstOrDefault(zoom => zoom.Id == _selectedZoom) is { } zoom)
        {
            _zoomClipboard = TimelineEdits.CopyZoom(zoom);
            TimeLabel.Text = "已复制缩放；Ctrl+V 粘贴完整副本，Ctrl+Shift+V 只粘属性。";
            return;
        }
        if (_selectedClip is null)
        {
            return;
        }

        var bundle = TimelineEdits.CopyEffect(_project, _selectedClip);
        _effectClipboard = bundle is null ? null : JsonSerializer.Serialize(bundle, ProjectSchema.Json);
        _zoomClipboard = null;
    }

    void PasteEffect_Click(object sender, RoutedEventArgs e) => PasteEffect(includeTiming: true);

    void PasteAttributes_Click(object sender, RoutedEventArgs e) => PasteEffect(includeTiming: false);

    void PasteEffect(bool includeTiming)
    {
        if (_decorationClipboard is not null) { PasteDecoration(includeTiming); return; }
        if (_zoomClipboard is { } zoom)
        {
            PasteZoom(zoom, includeTiming);
            return;
        }
        if (_selectedClip is null || _effectClipboard is null)
        {
            return;
        }

        var bundle = JsonSerializer.Deserialize<EffectBundle>(_effectClipboard, ProjectSchema.Json);
        if (bundle is null)
        {
            return;
        }

        Push();
        if (TimelineEdits.PasteEffect(_project, _selectedClip, bundle, includeTiming))
        {
            Commit();
        }
        else
        {
            _undo.Pop();
        }
    }

    void PasteZoom(ZoomSpan source, bool includeTiming)
    {
        // 校验成功后再记撤销，失败粘贴不会清空重做历史。
        var before = JsonSerializer.Serialize(_project, ProjectSchema.Json);
        if (includeTiming)
        {
            var copy = TimelineEdits.PasteZoom(_project, source, Timeline.PasteTime,
                Timeline.SnapThreshold, out var error);
            if (copy is null)
            {
                TimeLabel.Text = error;
                return;
            }
            _selectedZoom = copy.Id;
            _selectedDecoration = null;
            _selectedClip = null;
            _tool = "zoom";
        }
        else if (_selectedZoom is null || !TimelineEdits.PasteZoomAttributes(_project, _selectedZoom, source))
        {
            TimeLabel.Text = "请先选中一段缩放，再粘贴属性。";
            return;
        }
        Push(before);
        Commit();
    }

    void Speed_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_lock || _selectedClip is null || SpeedBox.SelectedItem is not string label)
        {
            return;
        }

        var speed = double.Parse(label.TrimEnd('×'), CultureInfo.InvariantCulture);
        Push();
        if (!TimelineEdits.TrySetSpeed(_project, _selectedClip, speed))
        {
            _undo.Pop();
            return;
        }

        Commit(rebuild: false);
    }

    void Play_Click(object sender, RoutedEventArgs e) { CancelOverlayGesture(); CancelRegionEditing(); RebuildInspector(); _player.Toggle(); }

    void SkipStart_Click(object sender, RoutedEventArgs e) => _player.Seek(0, play: false);

    void SkipEnd_Click(object sender, RoutedEventArgs e) => _player.Seek(TimelineMath.Duration(_project), play: false);

    void TimelineZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Timeline is not null)
        {
            Timeline.Zoom = e.NewValue;
        }
    }

    void Preview_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutPreview();

    void Frame_Down(object sender, MouseButtonEventArgs e)
    {
        if (_cameraSelected) { _cameraSelected = false; RebuildInspector(); ApplyZoomVisual(); }
        if (_tool == "zoom" && _selectedZoom is not null)
        {
            SetZoomFocus(e.GetPosition(FrameHost));
            return;
        }

        _dragTarget = "frame";
        _dragOrigin = e.GetPosition(PreviewCanvas);
        _dragX = _project.Canvas.CenterX;
        _dragY = _project.Canvas.CenterY;
        _dragUndo = false;
        FrameHost.CaptureMouse();
    }

    void Frame_Move(object sender, MouseEventArgs e)
    {
        if (_dragTarget != "frame" || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (!_dragUndo)
        {
            Push();
            _dragUndo = true;
        }

        var delta = e.GetPosition(PreviewCanvas) - _dragOrigin;
        _project.Canvas.CenterX = Math.Clamp(_dragX + delta.X / PreviewCanvas.Width, 0, 1);
        _project.Canvas.CenterY = Math.Clamp(_dragY + delta.Y / PreviewCanvas.Height, 0, 1);
        Commit(rebuild: false);
    }

    void Frame_Up(object sender, MouseButtonEventArgs e)
    {
        _dragTarget = null;
        FrameHost.ReleaseMouseCapture();
    }

    void SetZoomFocus(Point point)
    {
        var zoom = _project.Zooms.FirstOrDefault(item => item.Id == _selectedZoom);
        if (zoom is null || FrameHost.ActualWidth < 1)
        {
            return;
        }

        Push();
        var unprojected = PerspectiveGeometry.SourcePoint(PerspectiveGeometry.Corners(FrameHost.ActualWidth, FrameHost.ActualHeight,
            _project.Canvas.TiltX, _project.Canvas.TiltY), point.X, point.Y);
        var px = Math.Clamp(unprojected.X, 0, 1);
        var py = Math.Clamp(unprojected.Y, 0, 1);
        var divisor = 1 - 1 / zoom.Scale;
        zoom.Origin = "manual";
        zoom.FocusX = divisor < 0.02 ? 0.5 : Math.Clamp((px - 0.5 / zoom.Scale) / divisor, 0, 1);
        zoom.FocusY = divisor < 0.02 ? 0.5 : Math.Clamp((py - 0.5 / zoom.Scale) / divisor, 0, 1);
        Commit();
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
            || Keyboard.FocusedElement is PasswordBox)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (OverlayKey(e.Key, modifiers)) { e.Handled = true; return; }
        if (RegionKey(e.Key, modifiers)) { e.Handled = true; return; }
        if (modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopyEffect_Click(sender, e);
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteEffect_Click(sender, e);
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.V)
        {
            PasteAttributes_Click(sender, e);
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            Undo_Click(sender, e);
        }
        else if ((modifiers == ModifierKeys.Control && e.Key == Key.Y)
            || (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z))
        {
            Redo_Click(sender, e);
        }
        else if (modifiers != ModifierKeys.None)
        {
            return;
        }
        else if (e.Key == Key.Space)
        {
            Play_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.S)
        {
            Split_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            Delete_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.M)
        {
            Marker_Click(sender, e);
            e.Handled = true;
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    async Task LoadFramesAsync()
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg();
        if (ffmpeg is null)
        {
            return;
        }

        var frames = new List<FilmFrame>();
        foreach (var asset in _project.Assets.ToList())
        {
            try
            {
                var video = ProjectFile.Media(_directory, asset.Video);
                var folder = Path.Combine(_directory, "media", "thumbs", asset.Id);
                Directory.CreateDirectory(folder);
                var rate = (8 / Math.Max(asset.Duration, 0.4)).ToString("0.###", CultureInfo.InvariantCulture);
                var process = FfmpegProcess.Start(ffmpeg, ["-y", "-i", video, "-vf", "fps=" + rate + ",scale=160:-2", "-frames:v", "8", Path.Combine(folder, "%02d.jpg")]);
                await process.WaitAsync();
                process.Dispose();
                var files = Directory.GetFiles(folder, "*.jpg").OrderBy(path => path).ToList();
                for (var index = 0; index < files.Count; index++)
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(files[index]);
                    image.EndInit();
                    image.Freeze();
                    frames.Add(new FilmFrame(asset.Id, asset.Duration * (index + 0.5) / files.Count, image));
                }
            }
            catch (Exception ex)
            {
                AppPaths.Log("缩略图失败：" + ex.Message);
            }
        }

        Dispatcher.Invoke(() => Timeline.SetFrames(frames));
    }

    /// <summary>
    /// 异步算每个素材的简化波形（256 桶峰值），录制的麦克风/系统声是 wav
    /// 直接读；没有 wav 轨的素材先跳过（以缩略图为准）。
    /// </summary>
    async Task LoadWaveformsAsync()
    {
        foreach (var asset in _project.Assets.ToList())
        {
            try
            {
                var relative = asset.Microphone ?? asset.SystemAudio;
                if (string.IsNullOrWhiteSpace(relative))
                {
                    continue;
                }

                var path = ProjectFile.Media(_directory, relative);
                if (!File.Exists(path))
                {
                    continue;
                }

                var peaks = await Task.Run(() => WaveformPeaks(path, 256));
                if (peaks is not null)
                {
                    Dispatcher.Invoke(() => Timeline.SetWaveform(asset.Id, peaks));
                }
            }
            catch (Exception ex)
            {
                AppPaths.Log("波形失败：" + ex.Message);
            }
        }
    }

    /// <summary>读 wav 文件的等距峰值桶（0..1）。</summary>
    static float[]? WaveformPeaks(string path, int buckets)
    {
        using var reader = new NAudio.Wave.AudioFileReader(path);
        var total = reader.Length / (reader.WaveFormat.BitsPerSample / 8);
        if (total <= 0)
        {
            return null;
        }

        var peaks = new float[buckets];
        var buffer = new float[8192];
        var perBucket = Math.Max(1, total / buckets);
        var index = 0L;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var bucket = (int)Math.Min(buckets - 1, (index + i) / perBucket);
                var magnitude = Math.Abs(buffer[i]);
                if (magnitude > peaks[bucket])
                {
                    peaks[bucket] = magnitude;
                }
            }

            index += read;
        }

        return peaks;
    }

    static string Clock(double time) => TimeSpan.FromSeconds(Math.Max(0, time)).ToString(@"mm\:ss\.ff");
}
