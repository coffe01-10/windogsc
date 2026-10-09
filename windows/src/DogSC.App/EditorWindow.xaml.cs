using System.Globalization;
using System.Text.Json;
using System.Windows.Media.Effects;
using DogSC.Core;

namespace DogSC;

public partial class EditorWindow : Window
{
    readonly PreviewPlayer _player = new();
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
    (int W, int H, int X, int Y, int MW, int MH)? _softMaskKey;
    CursorTimeline? _cursorTimeline;
    (string Mode, double Size)? _cursorBitmapKey;

    public EditorWindow()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => FlushPendingSave();
        Loaded += (_, _) => WindowPlacement.KeepInWorkArea(this, center: true);
        Closing += (_, _) => FlushPendingSave();
        SetToolContent(ToolLayout, IconKind.Layout, "画面");
        SetToolContent(ToolZoom, IconKind.Zoom, "缩放");
        SetToolContent(ToolAudio, IconKind.Speaker, "声音");
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIconMaster.png"));
        FrameRect.Fill = new DrawingBrush { Drawing = _player.ScreenDrawing, Stretch = Stretch.Fill };
        CameraBubble.Fill = new DrawingBrush { Drawing = _player.CameraDrawing, Stretch = Stretch.UniformToFill };
        CameraBubble.Stroke = Brushes.White;
        foreach (var speed in new[] { 0.5, 0.75, 1d, 1.25, 1.5, 2d, 4, 8, 16, 32, 64, 100 })
        {
            SpeedBox.Items.Add(speed.ToString("0.##", CultureInfo.InvariantCulture) + "×");
        }

        _player.Changed += () => Dispatcher.Invoke(SyncPlayback);
        _player.Failed += message => Dispatcher.Invoke(() => TimeLabel.Text = "预览失败：" + message);
        Timeline.SeekRequested += (_, time) => _player.Seek(time, _player.IsPlaying);
        Timeline.InteractionStarted += (_, _) => { if (_player.IsPlaying) _player.Pause(); };
        Timeline.ClipSelected += (_, id) => { _selectedClip = id; _selectedZoom = null; RebuildInspector(); SyncSelection(); };
        Timeline.ZoomSelected += (_, id) => { _selectedZoom = id; _selectedClip = null; _tool = "zoom"; RebuildInspector(); SyncSelection(); ApplyZoomVisual(); };
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
            var timing = move.ZoomId == "decoration.sticker" ? _project.Canvas.Sticker?.Timing : _project.Canvas.SoftRegion.Timing;
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
        foreach (var button in new[] { ToolLayout, ToolZoom, ToolAudio })
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

        Inspector.Children.Add(Caption("贴图"));
        var sticker = _project.Canvas.Sticker;
        var stickRow = new StackPanel { Orientation = Orientation.Horizontal };
        var stickPick = new Button { Content = sticker is null ? "选图…" : "换图…", Style = (Style)FindResource("QuietButton"), Margin = new Thickness(0, 0, 6, 0) };
        stickPick.Click += (_, _) => PickDecoration(path =>
        {
            _project.Canvas.Sticker ??= new StickerImage();
            _project.Canvas.Sticker.Path = path;
        });
        stickRow.Children.Add(stickPick);
        if (sticker is not null)
        {
            var clear = new Button { Content = "清除", Style = (Style)FindResource("QuietButton") };
            clear.Click += (_, _) => { Push(); _project.Canvas.Sticker = null; Commit(); };
            stickRow.Children.Add(clear);
            Inspector.Children.Add(stickRow);
            Inspector.Children.Add(Caption("贴图大小"));
            Inspector.Children.Add(Slider(0.05, 0.9, sticker.Width, value => sticker.Width = value));
            Inspector.Children.Add(Caption("贴图不透明度"));
            Inspector.Children.Add(Slider(0.1, 1, sticker.Opacity, value => sticker.Opacity = value));
            Inspector.Children.Add(Caption("贴图位置（点击画面选择）"));
            Inspector.Children.Add(StickerPad(sticker));
            BuildEffectTiming("贴图", sticker.Timing);
        }
        else
        {
            Inspector.Children.Add(stickRow);
        }

        var soft = _project.Canvas.SoftRegion;
        var softOn = new CheckBox
        {
            Content = "局部柔化",
            IsChecked = soft.Enabled,
            Foreground = (Brush)FindResource("InkBrush"),
            Margin = new Thickness(0, 10, 0, 0),
        };
        softOn.Click += (_, _) => { Push(); soft.Enabled = softOn.IsChecked == true; Commit(); };
        Inspector.Children.Add(softOn);
        if (soft.Enabled)
        {
            Inspector.Children.Add(Caption("柔化强度"));
            Inspector.Children.Add(Slider(0, 1, soft.Strength, value => soft.Strength = value));
            Inspector.Children.Add(Caption("柔化区域（点击画面选择中心，宽高用默认矩形）"));
            Inspector.Children.Add(SoftPad(soft));
            BuildEffectTiming("柔化", soft.Timing);
        }

        // —— 摄像头画中画 ——
        if (_project.HasCamera)
        {
            Inspector.Children.Add(Heading("画中画"));
            BuildCameraDetails();
            Inspector.Children.Add(Caption("形状"));
            var shapes = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (name, key) in new[] { ("圆形", "circle"), ("圆角", "square"), ("无边框", "none") })
            {
                var chip = new Button { Content = name, Tag = key, Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 0) };
                chip.Click += (_, _) => { Push(); _project.Camera.Shape = (string)chip.Tag; Commit(); };
                shapes.Children.Add(chip);
            }

            Inspector.Children.Add(shapes);
            Inspector.Children.Add(Caption("大小"));
            Inspector.Children.Add(Slider(0.08, 0.46, _project.Camera.Size, value => _project.Camera.Size = value));
            if (_project.Camera.Shape == "square")
            {
                Inspector.Children.Add(Caption("圆角"));
                Inspector.Children.Add(Slider(0, 0.5, _project.Camera.Corner, value => _project.Camera.Corner = value));
            }

            Inspector.Children.Add(Caption("位置预设"));
            var grid = new WrapPanel();
            foreach (var (cx, cy) in new[] { (0.12, 0.15), (0.5, 0.15), (0.88, 0.15), (0.12, 0.5), (0.5, 0.5), (0.88, 0.5), (0.12, 0.85), (0.5, 0.85), (0.88, 0.85) })
            {
                var cell = new Button { Content = "·", Width = 26, Height = 26, Tag = (cx, cy), Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 4) };
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
                entries.Children.Add(chip);
            }

            Inspector.Children.Add(entries);
        }
    }

    /// <summary>选图后拷进项目 media/decor/ 并写入相对路径，保证项目可搬移。</summary>
    void PickDecoration(Action<string> assign)
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
            var file = Path.Combine(folder, Path.GetFileName(dialog.FileName));
            File.Copy(dialog.FileName, file, overwrite: true);
            Push();
            assign((target + "/" + Path.GetFileName(dialog.FileName)).Replace('\\', '/'));
            Commit();
        }
        catch (Exception ex)
        {
            AppPaths.Log("装饰图导入失败：" + ex.Message);
        }
    }

    /// <summary>贴图位置选择板：点击归一化坐标。</summary>
    UIElement StickerPad(StickerImage sticker)
    {
        var pad = new Canvas { Width = 200, Height = 120, Background = (Brush)FindResource("WellBrush") };
        var dot = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = (Brush)FindResource("AccentBrush") };
        pad.Children.Add(dot);
        void Place()
        {
            Canvas.SetLeft(dot, sticker.X * (pad.Width - 12));
            Canvas.SetTop(dot, sticker.Y * (pad.Height - 12));
        }

        Place();
        pad.MouseLeftButtonDown += (_, args) =>
        {
            Push();
            sticker.X = Math.Clamp(args.GetPosition(pad).X / pad.Width, 0, 1);
            sticker.Y = Math.Clamp(args.GetPosition(pad).Y / pad.Height, 0, 1);
            Place();
            Commit(rebuild: false);
        };
        return pad;
    }

    /// <summary>柔化区域选择板：点击定中心，宽高用当前矩形。</summary>
    UIElement SoftPad(SoftRegion soft)
    {
        var pad = new Canvas { Width = 200, Height = 120, Background = (Brush)FindResource("WellBrush") };
        var box = new System.Windows.Shapes.Rectangle { Fill = (Brush)FindResource("WellBrush"), Stroke = (Brush)FindResource("AccentBrush"), StrokeThickness = 1.5 };
        pad.Children.Add(box);
        void Place()
        {
            box.Width = soft.Width * pad.Width;
            box.Height = soft.Height * pad.Height;
            Canvas.SetLeft(box, soft.X * pad.Width);
            Canvas.SetTop(box, soft.Y * pad.Height);
        }

        Place();
        pad.MouseLeftButtonDown += (_, args) =>
        {
            Push();
            var p = args.GetPosition(pad);
            soft.X = Math.Clamp(p.X / pad.Width - soft.Width / 2, 0, 1 - soft.Width);
            soft.Y = Math.Clamp(p.Y / pad.Height - soft.Height / 2, 0, 1 - soft.Height);
            Place();
            Commit(rebuild: false);
        };
        return pad;
    }

    void BuildZoomInspector()
    {
        Inspector.Children.Add(Heading("缩放"));
        var auto = new Button { Content = "按点击自动运镜", Style = (Style)FindResource("QuietButton"), HorizontalAlignment = HorizontalAlignment.Left };
        auto.Click += (_, _) =>
        {
            Push();
            var added = TimelineEdits.AutoZoom(_project);
            if (added == 0)
            {
                _undo.Pop();
                Inspector.Children.Add(Caption("录制里没有可用的点击点（旧项目或没有光标轨迹）。"));
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

        Inspector.Children.Add(Caption("倍数"));
        Inspector.Children.Add(Slider(1, 3, zoom.Scale, value => zoom.Scale = value));
        Inspector.Children.Add(Caption("焦点 X"));
        Inspector.Children.Add(Slider(0, 1, zoom.FocusX, value => zoom.FocusX = value));
        Inspector.Children.Add(Caption("焦点 Y"));
        Inspector.Children.Add(Slider(0, 1, zoom.FocusY, value => zoom.FocusY = value));
        Inspector.Children.Add(Caption("在「缩放」工具下点击画面，也会把焦点放到点击的位置。"));
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
        UpdateSoftLayer(viewWidth, viewHeight, outputWidth, outputHeight);
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
            var bubble = CanvasLayout.Bubble(outputWidth, outputHeight, _project.Camera);
            var size = bubble.Size * frameScale;
            CameraBubble.Visibility = Visibility.Visible;
            CameraBubble.Width = size;
            CameraBubble.Height = size;
            Canvas.SetLeft(CameraBubble, bubble.X * frameScale);
            Canvas.SetTop(CameraBubble, bubble.Y * frameScale);
            // 形状：circle=圆，square=圆角矩形，none=直角。
            var camRadius = _project.Camera.Shape switch
            {
                "square" => size * Math.Clamp(_project.Camera.Corner, 0, 0.5),
                "none" => 0,
                _ => size / 2,
            };
            CameraBubble.RadiusX = camRadius;
            CameraBubble.RadiusY = camRadius;
            if (CameraBubble.Fill is DrawingBrush cameraBrush)
                cameraBrush.RelativeTransform = _project.Camera.Mirrored ? new ScaleTransform(-1, 1, 0.5, 0.5) : Transform.Identity;
        }
        else
        {
            CameraBubble.Visibility = Visibility.Collapsed;
        }

        ApplyZoomVisual();
    }

    void ApplyZoomVisual()
    {
        var zoom = _project.Zooms.FirstOrDefault(item => _player.Time >= item.Start && _player.Time < item.Start + item.Duration);
        var editingTarget = !_player.IsPlaying && _tool == "zoom" ? _project.Zooms.FirstOrDefault(item => item.Id == _selectedZoom) : null;
        if (editingTarget is not null) zoom = editingTarget;
        var scale = zoom is { Scale: > 1.01 } ? CanvasLayout.ZoomScaleAt(zoom, _player.Time) : 1;
        var (fx, fy) = zoom is { Scale: > 1.01 } ? CanvasLayout.ZoomFocusAt(zoom, _player.Time) : (0.5, 0.5);
        if (editingTarget is not null) { scale = editingTarget.Scale; fx = editingTarget.FocusX; fy = editingTarget.FocusY; }
        FrameRect.RenderTransformOrigin = new Point(fx, fy);

        FrameRect.RenderTransform = scale > 1.001 ? new ScaleTransform(scale, scale) : Transform.Identity;
        UpdatePerspective();
        CursorLayer.RenderTransform = FrameRect.RenderTransform;
        CursorLayer.RenderTransformOrigin = FrameRect.RenderTransformOrigin;
        FocusBlurLayer.RenderTransform = FrameRect.RenderTransform;
        FocusBlurLayer.RenderTransformOrigin = FrameRect.RenderTransformOrigin;

        // 开场动画：fade=淡入，rise=淡入+上移，zoom=淡入+轻微放大。
        var intro = CanvasLayout.IntroProgress(_project.Canvas, _player.Time);
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

        // 聚焦虚化预览：边缘一层同画面的模糊副本盖径向透明蒙板。
        UpdateFocusLayer();
        UpdateCursorLayer();
        UpdateDecorationTiming();
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
        var sticker = _project.Canvas.Sticker;
        var path = sticker is null ? null : DecorationPath(sticker.Path);
        if (sticker is null || path is null)
        {
            StickerImage.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            if (!Equals(StickerImage.Tag, path))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                StickerImage.Source = image;
                StickerImage.Tag = path;
            }

            var scale = viewWidth / outputWidth;
            var w = sticker.Width * outputWidth * scale;
            var aspect = StickerImage.Source is BitmapSource bmp && bmp.PixelHeight > 0
                ? bmp.PixelWidth / (double)bmp.PixelHeight : 1;
            var h = w / Math.Max(0.1, aspect);
            StickerImage.Width = w;
            StickerImage.Height = h;
            StickerImage.Opacity = sticker.Opacity;
            Canvas.SetLeft(StickerImage, sticker.X * outputWidth * scale - w / 2);
            Canvas.SetTop(StickerImage, sticker.Y * outputHeight * scale - h / 2);
            StickerImage.Visibility = Visibility.Visible;
        }
        catch
        {
            StickerImage.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 局部柔化预览：全屏模糊副本 SoftLayer，OpacityMask 是运行时生成的
    /// 矩形灰度图（与导出端 MaskImage.CreateRectMask 同一算法）。
    /// </summary>
    void UpdateSoftLayer(double viewWidth, double viewHeight, int outputWidth, int outputHeight)
    {
        var soft = _project.Canvas.SoftRegion;
        if (!soft.Enabled || soft.Width < 0.02 || soft.Height < 0.02 || soft.Strength < 0.02)
        {
            SoftLayer.Visibility = Visibility.Collapsed;
            return;
        }

        // SoftLayer 叠在 FrameHost 之上，显示被柔化的是背景+画面整体。
        SoftLayer.Width = viewWidth;
        SoftLayer.Height = viewHeight;
        Canvas.SetLeft(SoftLayer, 0);
        Canvas.SetTop(SoftLayer, 0);
        SoftBlur.Radius = (4 + soft.Strength * 24) * viewWidth / outputWidth;
        // 生成矩形 mask 位图（画布像素坐标）做 OpacityMask。
        var width = Math.Max(2, (int)Math.Round(viewWidth));
        var height = Math.Max(2, (int)Math.Round(viewHeight));
        var mw = Math.Max(2, (int)Math.Round(soft.Width * width));
        var mh = Math.Max(2, (int)Math.Round(soft.Height * height));
        var mx = (int)Math.Round(soft.X * width);
        var my = (int)Math.Round(soft.Y * height);
        var key = (width, height, mx, my, mw, mh);
        if (_softMaskKey != key)
        {
            var pixels = MaskImage.CreateRectMask(width, height, mx, my, mw, mh, Math.Max(4, mw / 12));
            var mask = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            mask.Freeze();
            SoftLayer.OpacityMask = new ImageBrush(mask) { Stretch = Stretch.Fill };
            _softMaskKey = key;
        }
        // 用整屏 VisualBrush 做模糊副本，对齐画布坐标，柔化的是背景+画面整体。
        if (SoftLayer.Fill is not VisualBrush)
        {
            // 只引用底层场景，避免把柔化层自身再次采样进来。
            SoftLayer.Fill = new VisualBrush(SceneCanvas) { Stretch = Stretch.Fill };
        }

        SoftLayer.Visibility = Visibility.Visible;
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
    /// <summary>FrameRect 上的径向虚化层：中心透明边缘显示模糊副本。</summary>
    void UpdateFocusLayer()
    {
        var blur = _project.Canvas.FocusBlur;
        if (blur <= 0.02 || FocusBlurLayer is null)
        {
            if (FocusBlurLayer is not null)
            {
                FocusBlurLayer.Visibility = Visibility.Collapsed;
            }

            return;
        }

        FocusBlurLayer.Visibility = Visibility.Visible;
        if (FocusBlurLayer.Effect is BlurEffect effect)
        {
            effect.Radius = 4 + blur * 26;
        }

        if (FocusBlurLayer.Fill is not DrawingBrush)
        {
            FocusBlurLayer.Fill = new DrawingBrush { Drawing = _player.ScreenDrawing, Stretch = Stretch.Fill };
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
        _lock = true;
        var clip = _project.Clips.FirstOrDefault(item => item.Id == _selectedClip);
        SpeedBox.IsEnabled = clip is not null;
        var label = (clip?.Speed ?? 1).ToString("0.##", CultureInfo.InvariantCulture) + "×";
        SpeedBox.SelectedItem = label;
        _lock = false;
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    void Commit(bool rebuild = true)
    {
        ProjectFile.Normalize(_project);
        _cursorTimeline = new CursorTimeline(_project);
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
        _project = JsonSerializer.Deserialize<ProjectDocument>(json, ProjectSchema.Json) ?? _project;
        ProjectFile.Normalize(_project);
        _selectedZoom = _project.Zooms.FirstOrDefault(item => item.Id == _selectedZoom)?.Id;
        _selectedClip = _selectedZoom is null
            ? _project.Clips.FirstOrDefault(item => item.Id == _selectedClip)?.Id ?? _project.Clips.FirstOrDefault()?.Id
            : null;
        Commit();
        _player.Seek(Math.Min(_player.Time, TimelineMath.Duration(_project)), play: false);
    }

    void Tool_Click(object sender, RoutedEventArgs e)
    {
        var tool = (string)((Button)sender).Tag;
        if (tool == _tool) return;
        _tool = tool;
        RebuildInspector();
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
        var changed = _selectedZoom is not null
            ? TimelineEdits.TryDeleteZoom(_project, _selectedZoom)
            : _selectedClip is not null && TimelineEdits.TryDeleteClip(_project, _selectedClip);
        if (!changed)
        {
            _undo.Pop();
            return;
        }

        _selectedZoom = null;
        _selectedClip = _project.Clips.FirstOrDefault()?.Id;
        Commit();
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

    void Play_Click(object sender, RoutedEventArgs e) => _player.Toggle();

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

    void Camera_Down(object sender, MouseButtonEventArgs e)
    {
        _dragTarget = "camera";
        _dragOrigin = e.GetPosition(PreviewCanvas);
        _dragX = _project.Camera.CenterX;
        _dragY = _project.Camera.CenterY;
        _dragUndo = false;
        CameraBubble.CaptureMouse();
        e.Handled = true;
    }

    void Camera_Move(object sender, MouseEventArgs e)
    {
        if (_dragTarget != "camera" || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (!_dragUndo)
        {
            Push();
            _dragUndo = true;
        }

        var delta = e.GetPosition(PreviewCanvas) - _dragOrigin;
        _project.Camera.CenterX = Math.Clamp(_dragX + delta.X / PreviewCanvas.Width, 0, 1);
        _project.Camera.CenterY = Math.Clamp(_dragY + delta.Y / PreviewCanvas.Height, 0, 1);
        Commit(rebuild: false);
    }

    void Camera_Up(object sender, MouseButtonEventArgs e)
    {
        _dragTarget = null;
        CameraBubble.ReleaseMouseCapture();
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
            _player.Toggle();
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
