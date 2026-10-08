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

    public EditorWindow()
    {
        InitializeComponent();
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIconMaster.png"));
        FrameRect.Fill = new DrawingBrush { Drawing = _player.ScreenDrawing, Stretch = Stretch.Fill };
        CameraBubble.Fill = new DrawingBrush { Drawing = _player.CameraDrawing, Stretch = Stretch.Fill };
        CameraBubble.Stroke = Brushes.White;
        foreach (var speed in new[] { 0.5, 0.75, 1d, 1.25, 1.5, 2d })
        {
            SpeedBox.Items.Add(speed.ToString("0.##", CultureInfo.InvariantCulture) + "×");
        }

        _player.Changed += () => Dispatcher.Invoke(SyncPlayback);
        _player.Failed += message => Dispatcher.Invoke(() => TimeLabel.Text = "预览失败：" + message);
        Timeline.SeekRequested += (_, time) => _player.Seek(time, _player.IsPlaying);
        Timeline.ClipSelected += (_, id) => { _selectedClip = id; _selectedZoom = null; SyncSelection(); };
        Timeline.ZoomSelected += (_, id) => { _selectedZoom = id; _tool = "zoom"; RebuildInspector(); SyncSelection(); };
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
        Closed += (_, _) => _player.Dispose();
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
        _selectedClip = _project.Clips.FirstOrDefault()?.Id;
        NameBox.Text = _project.Name;
        Title = "DogSC — " + _project.Name;
        ThemeButton.Content = SettingsStore.Current.Theme == "dark" ? "浅色" : "深色";
        FillAspects();
        Timeline.Project = _project;
        _player.Open(_project, directory);
        _player.SetGains(_project.MicrophoneGain, _project.SystemGain);
        SyncSelection();
        RebuildInspector();
        LayoutPreview();
        _ = LoadFramesAsync();
    }

    void FillAspects()
    {
        AspectRow.Children.Clear();
        foreach (var aspect in new[] { ("适应", "source"), ("16:9", "16:9"), ("4:3", "4:3"), ("1:1", "1:1"), ("9:16", "9:16") })
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
        Inspector.Children.Add(Caption("边距"));
        Inspector.Children.Add(Slider(0, 180, _project.Canvas.Padding, value => _project.Canvas.Padding = value));
        Inspector.Children.Add(Caption("素材缩放"));
        Inspector.Children.Add(Slider(0.6, 1.8, _project.Canvas.Scale, value => _project.Canvas.Scale = value));
        Inspector.Children.Add(Caption("圆角"));
        Inspector.Children.Add(Slider(0, 90, _project.Canvas.Corner, value => _project.Canvas.Corner = value));
        Inspector.Children.Add(Caption("阴影"));
        Inspector.Children.Add(Slider(0, 100, _project.Canvas.Shadow, value => _project.Canvas.Shadow = value));
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
    }

    void BuildZoomInspector()
    {
        Inspector.Children.Add(Heading("缩放"));
        var add = new Button { Content = "在播放头添加缩放", Style = (Style)FindResource("QuietButton"), HorizontalAlignment = HorizontalAlignment.Left };
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
        Inspector.Children.Add(Slider(0, 2, _project.MicrophoneGain, value =>
        {
            _project.MicrophoneGain = value;
            _player.SetGains(_project.MicrophoneGain, _project.SystemGain);
        }));
        Inspector.Children.Add(Caption("系统声音"));
        Inspector.Children.Add(Slider(0, 2, _project.SystemGain, value =>
        {
            _project.SystemGain = value;
            _player.SetGains(_project.MicrophoneGain, _project.SystemGain);
        }));
        var recorded = _project.Assets.Any(asset => asset.Microphone is not null || asset.SystemAudio is not null);
        Inspector.Children.Add(Caption(recorded ? "两条声音会在导出时混合。这里的音量只影响预览和导出。" : "这个项目没有单独的声音轨。"));
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

    Slider Slider(double min, double max, double value, Action<double> apply)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Margin = new Thickness(0, 0, 0, 4) };
        slider.PreviewMouseLeftButtonDown += (_, _) => Push();
        slider.ValueChanged += (_, args) =>
        {
            if (_lock)
            {
                return;
            }

            apply(args.NewValue);
            Commit(rebuild: false);
        };
        return slider;
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
        StageRect.Width = viewWidth;
        StageRect.Height = viewHeight;
        StageRect.Fill = Brush(_project.Canvas.Background);
        var frame = CanvasLayout.Place(outputWidth, outputHeight, _project.Width, _project.Height, _project.Canvas);
        var frameScale = viewWidth / outputWidth;
        var frameWidth = Math.Max(2, frame.Width * frameScale);
        var frameHeight = Math.Max(2, frame.Height * frameScale);
        Canvas.SetLeft(FrameHost, frame.X * frameScale);
        Canvas.SetTop(FrameHost, frame.Y * frameScale);
        FrameHost.Width = frameWidth;
        FrameHost.Height = frameHeight;
        var radius = frame.Radius * frameScale;
        FrameHost.Clip = new RectangleGeometry(new Rect(0, 0, frameWidth, frameHeight), radius, radius);
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

        if (_project.HasCamera)
        {
            var bubble = CanvasLayout.Bubble(outputWidth, outputHeight, _project.Camera);
            CameraBubble.Visibility = Visibility.Visible;
            CameraBubble.Width = bubble.Size * frameScale;
            CameraBubble.Height = bubble.Size * frameScale;
            Canvas.SetLeft(CameraBubble, bubble.X * frameScale);
            Canvas.SetTop(CameraBubble, bubble.Y * frameScale);
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
        if (zoom is null || zoom.Scale <= 1.01)
        {
            FrameRect.RenderTransform = Transform.Identity;
            return;
        }

        var x = (1 - 1 / zoom.Scale) * zoom.FocusX + 0.5 / zoom.Scale;
        var y = (1 - 1 / zoom.Scale) * zoom.FocusY + 0.5 / zoom.Scale;
        FrameRect.RenderTransformOrigin = new Point(x, y);
        FrameRect.RenderTransform = new ScaleTransform(zoom.Scale, zoom.Scale);
    }

    void SyncPlayback()
    {
        Timeline.Playhead = _player.Time;
        TimeLabel.Text = Clock(_player.Time) + " / " + Clock(TimelineMath.Duration(_project));
        PlayButton.Content = _player.IsPlaying ? "暂停" : "播放";
        ApplyZoomVisual();
    }

    void SyncSelection()
    {
        Timeline.SelectedClipId = _selectedClip;
        Timeline.SelectedZoomId = _selectedZoom;
        _lock = true;
        var clip = _project.Clips.FirstOrDefault(item => item.Id == _selectedClip);
        var label = (clip?.Speed ?? 1).ToString("0.##", CultureInfo.InvariantCulture) + "×";
        SpeedBox.SelectedItem = label;
        _lock = false;
        LayoutPreview();
    }

    void Commit(bool rebuild = true)
    {
        ProjectFile.Normalize(_project);
        ProjectFile.Save(_directory, _project);
        Timeline.Project = _project;
        if (rebuild)
        {
            RebuildInspector();
        }

        PaintAspects();
        LayoutPreview();
        SyncPlayback();
    }

    void Push()
    {
        _undo.Push(JsonSerializer.Serialize(_project, ProjectSchema.Json));
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
        _selectedClip = _project.Clips.FirstOrDefault(item => item.Id == _selectedClip)?.Id ?? _project.Clips.FirstOrDefault()?.Id;
        _selectedZoom = _project.Zooms.FirstOrDefault(item => item.Id == _selectedZoom)?.Id;
        Commit();
        _player.Seek(Math.Min(_player.Time, TimelineMath.Duration(_project)), play: false);
    }

    void Tool_Click(object sender, RoutedEventArgs e)
    {
        _tool = (string)((Button)sender).Tag;
        RebuildInspector();
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

    void Export_Click(object sender, RoutedEventArgs e) =>
        new ExportWindow(_project, _directory) { Owner = this }.ShowDialog();

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        var next = SettingsStore.Current.Theme == "dark" ? "light" : "dark";
        ThemeService.Apply(next);
        SettingsStore.Save();
        ThemeButton.Content = next == "dark" ? "浅色" : "深色";
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
        var px = Math.Clamp(point.X / FrameHost.ActualWidth, 0, 1);
        var py = Math.Clamp(point.Y / FrameHost.ActualHeight, 0, 1);
        var divisor = 1 - 1 / zoom.Scale;
        zoom.FocusX = divisor < 0.02 ? 0.5 : Math.Clamp((px - 0.5 / zoom.Scale) / divisor, 0, 1);
        zoom.FocusY = divisor < 0.02 ? 0.5 : Math.Clamp((py - 0.5 / zoom.Scale) / divisor, 0, 1);
        Commit();
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        if (e.Key == Key.Space)
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
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Undo_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Redo_Click(sender, e);
            e.Handled = true;
        }
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

    static string Clock(double time) => TimeSpan.FromSeconds(Math.Max(0, time)).ToString(@"mm\:ss\.ff");
}
