using System.Windows.Interop;
using System.Windows.Threading;
using DogSC.Core;

namespace DogSC;

public partial class RecorderBarWindow : Window
{
    internal void RefreshLanguage()
    {
        CollapseButton.ToolTip = _collapsed ? L.Text("展开工具条") : L.Text("收起工具条");
        System.Windows.Automation.AutomationProperties.SetName(CollapseButton, (string)CollapseButton.ToolTip);
        PaintToggles();
        PaintPauseState();
        PaintRecordingMicrophone();
    }
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly DispatcherTimer _barSettingsTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly HwndSourceHook _hook;
    RecordingSession? _session;
    string? _directory;
    CaptureTarget? _lastTarget;
    bool _paused;
    bool _busy;
    bool _closing;
    IReadOnlyList<string> _cameras = [];
    HwndSource? _source;
    bool _barLayoutReady;
    bool _collapsed;
    BarAnchor _anchor;
    BarBounds _work;
    bool _positionReady;
    bool _dragging;
    bool _placing;
    bool _changingLayout;
    readonly RotateTransform _chevronTurn = new();
    BarBounds? _dockOrigin;
    static readonly DependencyProperty DockProgressProperty = DependencyProperty.Register(
        "DockProgress", typeof(double), typeof(RecorderBarWindow),
        new PropertyMetadata(1d, (owner, _) => ((RecorderBarWindow)owner).PositionBar()));
    double DockProgress => (double)GetValue(DockProgressProperty);

    bool VerticalBar => _anchor.Edge is BarDockEdge.Left or BarDockEdge.Right;

    public RecorderBarWindow()
    {
        InitializeComponent();
        CollapseIcon.RenderTransformOrigin = new Point(0.5, 0.5);
        CollapseIcon.RenderTransform = _chevronTurn;
        _collapsed = SettingsStore.Current.BarCollapsed;
        _anchor = new(SettingsStore.Current.BarDockEdge, SettingsStore.Current.BarAnchorRight, SettingsStore.Current.BarAnchorBottom, 0, 0);
        BarSizeSlider.Value = SettingsStore.Current.BarScale;
        SizePopup.Opened += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => BarSizeSlider.Focus()));
        SizePopup.Closed += (_, _) => (SizePopup.PlacementTarget as UIElement)?.Focus();
        BarSizeSlider.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            SizePopup.IsOpen = false;
            args.Handled = true;
        };
        _barSettingsTimer.Tick += (_, _) => { _barSettingsTimer.Stop(); SettingsStore.Save(); };
        KeyboardNavigation.SetTabNavigation(ListItems, KeyboardNavigationMode.Cycle);
        ListPopup.Opened += (_, _) =>
        {
            Motion.Reveal(ListItems);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ListItems.Children.OfType<Button>().FirstOrDefault()?.Focus()));
        };
        ListPopup.Closed += (_, _) => (ListPopup.PlacementTarget as UIElement)?.Focus();
        ListItems.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                ListPopup.IsOpen = false;
                args.Handled = true;
            }
            else if (args.Key is Key.Up or Key.Down)
            {
                var buttons = ListItems.Children.OfType<Button>().ToList();
                var index = buttons.FindIndex(button => button.IsKeyboardFocusWithin);
                if (buttons.Count > 0)
                {
                    var next = index < 0 ? 0 : (index + (args.Key == Key.Down ? 1 : buttons.Count - 1)) % buttons.Count;
                    buttons[next].Focus();
                }
                args.Handled = true;
            }
            else if (args.Key == Key.Enter && ListItems.Children.OfType<Button>().FirstOrDefault(b => b.IsKeyboardFocusWithin) is { } focused)
            {
                focused.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                args.Handled = true;
            }
        };
        _hook = WndProc;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIconMaster.png"));
        FolderLabel.Text = Path.GetFileName(SettingsStore.Current.ProjectsDirectory);
        PaintToggles();
        _timer.Tick += (_, _) => PaintElapsedTime();
        Loaded += async (_, _) => await LoadDevicesAsync();
        Loaded += async (_, _) => await RecoverPartialAsync();
        MouseLeftButtonDown += DragBar;
        SizeChanged += (_, _) => PositionBar();
        Loaded += (_, _) =>
        {
            _barLayoutReady = true;
            ApplyBarLayout(false);
            if (!SettingsStore.Current.RecorderTourSeen)
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => ShowRecorderTour(0)));
        };
        if (!double.IsNaN(SettingsStore.Current.BarLeft))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SettingsStore.Current.BarLeft;
            Top = SettingsStore.Current.BarTop;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    void DragBar(object sender, MouseButtonEventArgs args)
    {
        try
        {
            if (!WindowPlacement.Drag(this, args, () =>
            {
                _dragging = true;
                ListPopup.IsOpen = SizePopup.IsOpen = false;
                Motion.Freeze(BarRoot, WidthProperty);
                Motion.Freeze(BarRoot, HeightProperty);
                BeginAnimation(DockProgressProperty, null);
                SetValue(DockProgressProperty, 1d);
                _dockOrigin = null;
                foreach (var panel in new FrameworkElement[] { SetupPanel, RecordingPanel, CompactPanel })
                {
                    panel.BeginAnimation(OpacityProperty, null);
                    panel.Opacity = 1;
                }
            })) return;
        }
        finally
        {
            _dragging = false;
        }
        var work = WindowPlacement.WorkArea(this);
        _work = new(work.Left, work.Top, work.Width, work.Height);
        var bounds = new BarBounds(Left, Top, ActualWidth, ActualHeight);
        _anchor = BarAnchor.Capture(bounds, _work, BarDockLayout.SnapEdge(bounds, _work));
        SizeToContent = SizeToContent.WidthAndHeight;
        ApplyBarLayout(true, attach: true);
        SettingsStore.Current.BarDockEdge = _anchor.Edge;
        SettingsStore.Current.BarAnchorRight = _anchor.RightAligned;
        SettingsStore.Current.BarAnchorBottom = _anchor.BottomAligned;
        QueueBarSettings();
    }

    void PositionBar()
    {
        if (!_positionReady || _dragging || _placing || _changingLayout) return;
        _placing = true;
        try
        {
            var bounds = _anchor.Place(_work, ActualWidth, ActualHeight, _dockOrigin, DockProgress);
            Left = bounds.Left;
            Top = bounds.Top;
        }
        finally { _placing = false; }
    }

    void ArrangeAlongEdge(bool animate)
    {
        var vertical = VerticalBar;
        BarShell.Margin = vertical ? new Thickness(12, 16, 8, 16) : new Thickness(16, 12, 16, 8);
        BarShell.Padding = vertical ? new Thickness(10, 14, 10, 14) : new Thickness(14, 10, 14, 10);
        ShellGrid.ColumnDefinitions[0].Width = vertical ? new GridLength(1, GridUnitType.Star) : new GridLength(24);
        ShellGrid.ColumnDefinitions[1].Width = vertical ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ShellGrid.ColumnDefinitions[2].Width = new GridLength(vertical ? 0 : 36);
        ShellGrid.RowDefinitions[0].Height = vertical ? new GridLength(24) : new GridLength(1, GridUnitType.Star);
        ShellGrid.RowDefinitions[1].Height = vertical ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ShellGrid.RowDefinitions[2].Height = new GridLength(vertical ? 36 : 0);
        Grid.SetColumn(ContentHost, vertical ? 0 : 1);
        Grid.SetRow(ContentHost, vertical ? 1 : 0);
        Grid.SetColumn(CollapseButton, vertical ? 0 : 2);
        Grid.SetRow(CollapseButton, vertical ? 2 : 0);
        CollapseButton.HorizontalAlignment = HorizontalAlignment.Center;
        CollapseButton.VerticalAlignment = VerticalAlignment.Center;
        GripIcon.LayoutTransform = new RotateTransform(vertical ? 90 : 0);
        SetupPanel.Orientation = CompactPanel.Orientation = CompactRecording.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        SetupPanel.HorizontalAlignment = CompactPanel.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var button in SetupPanel.Children.OfType<Button>())
            button.Margin = vertical ? new Thickness(0, 2, 0, 2) : new Thickness(2, 0, 2, 0);
        foreach (var separator in SetupPanel.Children.OfType<System.Windows.Shapes.Rectangle>())
        {
            separator.Width = vertical ? 36 : 1;
            separator.Height = vertical ? 1 : 36;
            separator.Margin = vertical ? new Thickness(0, 8, 0, 8) : new Thickness(8, 0, 8, 0);
        }
        FolderButton.Width = vertical ? 64 : 112;
        FolderContent.Width = vertical ? 48 : 96;
        MainMenuButton.Width = MainCloseButton.Width = vertical ? 64 : 48;
        if (!vertical) MainMenuButton.Margin = new Thickness(6, 0, 2, 0);
        CompactRecord.Height = vertical ? 56 : 36;
        CompactRecord.Padding = vertical ? new Thickness(6) : new Thickness(14, 8, 14, 8);
        CompactRecordContent.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        CompactRecordIcon.Margin = vertical ? new Thickness(0, 0, 0, 4) : new Thickness(0, 0, 6, 0);
        CompactMenu.Margin = vertical ? new Thickness(0, 4, 0, 0) : new Thickness(4, 0, 0, 0);
        RecordingHeader.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        DockPanel.SetDock(RecordingHeader, vertical ? Dock.Top : Dock.Left);
        RecordingLabel.MaxWidth = vertical ? 84 : 140;
        RecordingLabel.Margin = vertical ? new Thickness(0, 6, 0, 6) : new Thickness(14, 0, 0, 0);
        foreach (var button in RecordingPanel.Children.OfType<Button>())
        {
            DockPanel.SetDock(button, vertical ? Dock.Bottom : Dock.Right);
            button.Margin = vertical ? new Thickness(0, 4, 0, 4) : new Thickness(0);
        }
        if (!vertical) ((Button)RecordingPanel.Children[1]).Margin = new Thickness(8, 0, 4, 0);
        var popupSide = _anchor.Edge == BarDockEdge.Right ? System.Windows.Controls.Primitives.PlacementMode.Left
            : _anchor.Edge == BarDockEdge.Left ? System.Windows.Controls.Primitives.PlacementMode.Right
            : _anchor.BottomAligned ? System.Windows.Controls.Primitives.PlacementMode.Top : System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ListPopup.Placement = SizePopup.Placement = popupSide;
        var towardEnd = vertical ? !_anchor.BottomAligned : !_anchor.RightAligned;
        if (!_collapsed) towardEnd = !towardEnd;
        var angle = vertical ? towardEnd ? 90d : 270d : towardEnd ? 0d : 180d;
        CollapseIcon.Kind = IconKind.ChevronRight;
        var from = _chevronTurn.Angle;
        var turn = ((angle - from) % 360 + 540) % 360 - 180;
        Motion.To(_chevronTurn, RotateTransform.AngleProperty, from, from + turn, animate ? 240 : 0);
    }

    void ApplyBarLayout(bool animate, bool attach = false)
    {
        if (!_barLayoutReady) return;
        var origin = new BarBounds(Left, Top, ActualWidth, ActualHeight);
        var recording = _session is not null;
        SetupPanel.Visibility = !_collapsed && !recording ? Visibility.Visible : Visibility.Collapsed;
        RecordingPanel.Visibility = !_collapsed && recording ? Visibility.Visible : Visibility.Collapsed;
        CompactPanel.Visibility = _collapsed ? Visibility.Visible : Visibility.Collapsed;
        CompactRecord.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
        CompactRecording.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        CollapseButton.ToolTip = _collapsed ? L.Text("展开工具条") : L.Text("收起工具条");
        System.Windows.Automation.AutomationProperties.SetName(CollapseButton, (string)CollapseButton.ToolTip);

        var length = _collapsed ? recording ? 380d : 280d : recording ? VerticalBar ? 440d : 620d : 860d;
        var designWidth = VerticalBar ? _collapsed ? 104d : 132d : length;
        var designHeight = VerticalBar ? length : _collapsed ? 88d : recording ? 100d : 124d;
        var work = WindowPlacement.WorkArea(this);
        _work = new(work.Left, work.Top, work.Width, work.Height);
        var scale = Math.Min(SettingsStore.Current.BarScale, Math.Min((work.Width - 24) / designWidth, (work.Height - 24) / designHeight));
        var width = designWidth * scale;
        var height = designHeight * scale;
        if (!_positionReady)
        {
            var right = _anchor.Edge == BarDockEdge.Right || _anchor.RightAligned;
            var bottom = _anchor.Edge == BarDockEdge.Bottom || _anchor.BottomAligned;
            _anchor = new(_anchor.Edge, right, bottom, right ? Left + width : Left, bottom ? Top + height : Top);
            if (double.IsNaN(SettingsStore.Current.BarLeft)) _anchor = BarAnchor.Capture(new(Left, Top, width, height), _work, _anchor.Edge);
            _positionReady = true;
        }
        var duration = animate ? attach ? 320 : 280 : 0;
        var attaching = animate && Motion.Enabled && (attach || _dockOrigin is not null && DockProgress < 1);
        var fromWidth = ActualWidth / scale;
        var fromHeight = ActualHeight / scale;
        _changingLayout = true;
        try
        {
            _dockOrigin = attaching ? origin : null;
            Motion.To(this, DockProgressProperty, attaching ? 0 : 1, 1, duration);
            ArrangeAlongEdge(animate);
            BarScale.ScaleX = BarScale.ScaleY = scale;
            Motion.To(BarRoot, WidthProperty, fromWidth, designWidth, duration);
            Motion.To(BarRoot, HeightProperty, fromHeight, designHeight, duration);
        }
        finally { _changingLayout = false; }
        PositionBar();
        BarSizeLabel.Text = SettingsStore.Current.BarScale.ToString("P0");
        if (animate)
        {
            var delay = _collapsed ? 45 : (int)(duration * 0.65);
            Motion.Reveal(_collapsed ? CompactPanel : recording ? RecordingPanel : SetupPanel, milliseconds: 150, delay: delay,
                x: VerticalBar ? 0 : _anchor.RightAligned ? -4 : 4,
                y: VerticalBar ? _anchor.BottomAligned ? -4 : 4 : 0);
        }
    }

    void Collapse_Click(object sender, RoutedEventArgs e)
    {
        ListPopup.IsOpen = SizePopup.IsOpen = false;
        _collapsed = !_collapsed;
        SettingsStore.Current.BarCollapsed = _collapsed;
        ApplyBarLayout(true);
        CollapseButton.Focus();
        QueueBarSettings();
    }

    void BarSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_barLayoutReady) return;
        SettingsStore.Current.BarScale = e.NewValue;
        ApplyBarLayout(false);
        QueueBarSettings();
    }

    void ResetBarSize_Click(object sender, RoutedEventArgs e) => BarSizeSlider.Value = 0.85;

    void Bar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        BarSizeSlider.Value = Math.Clamp(Math.Round((BarSizeSlider.Value + Math.Sign(e.Delta) * 0.05) * 20) / 20, 0.7, 1.1);
        e.Handled = true;
    }

    void QueueBarSettings()
    {
        _barSettingsTimer.Stop();
        _barSettingsTimer.Start();
    }

    void PaintPauseState()
    {
        PauseLabel.Text = _paused ? L.Text("继续") : L.Text("暂停");
        PauseIcon.Kind = CompactPauseIcon.Kind = _paused ? IconKind.Play : IconKind.Pause;
        CompactPause.ToolTip = _paused ? L.Text("继续录制") : L.Text("暂停录制");
        System.Windows.Automation.AutomationProperties.SetName(CompactPause, (string)CompactPause.ToolTip);
    }

    void PaintElapsedTime()
    {
        if (_session is null) return;
        var elapsed = TimeSpan.FromSeconds(_session.Elapsed);
        TimerLabel.Text = CompactTimer.Text = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        PaintRecordingMicrophone();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = (HwndSource)PresentationSource.FromVisual(this)!;
        WindowPlacement.Exclude(this);
        _source.AddHook(_hook);
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _barSettingsTimer.Stop();
        SettingsStore.Current.BarLeft = Left;
        SettingsStore.Current.BarTop = Top;
        SettingsStore.Save();
        if (_session is not null && !_closing)
        {
            e.Cancel = true;
            _closing = true;
            await StopAsync();
            Close();
        }
    }

    async Task LoadDevicesAsync()
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg();
        if (ffmpeg is null)
        {
            ShowError(L.Text("没有找到 ffmpeg。安装后把它加入 PATH，或到设置里指定 ffmpeg.exe。"));
            return;
        }

        _cameras = await Task.Run(() => CameraCatalog.Devices(ffmpeg));
        if (string.IsNullOrWhiteSpace(SettingsStore.Current.CameraName))
        {
            SettingsStore.Current.CameraName = _cameras.FirstOrDefault();
        }
    }

    void Display_Click(object sender, RoutedEventArgs e)
    {
        var monitors = Native.Monitors();
        ShowList(sender as FrameworkElement ?? DisplayButton, monitors.Select(monitor => (monitor.Name, monitor.Width + " × " + monitor.Height, (Action)(() =>
        {
            var region = Native.EvenRegion(monitor.Left, monitor.Top, monitor.Width, monitor.Height);
            _ = BeginAsync(new CaptureTarget(CaptureKind.Display, monitor.Name, region.X, region.Y, region.Width, region.Height, null));
        }))).ToList());
    }

    void Window_Click(object sender, RoutedEventArgs e)
    {
        var windows = Native.Windows();
        if (windows.Count == 0)
        {
            ShowError(L.Text("没有找到可录制的窗口。"));
            return;
        }

        ShowList(sender as FrameworkElement ?? this, windows.Select(window => (window.Title, window.ProcessName, (Action)(() =>
        {
            var region = Native.EvenRegion(window.Left, window.Top, window.Width, window.Height);
            _ = BeginAsync(new CaptureTarget(CaptureKind.Window, window.Title, region.X, region.Y, region.Width, region.Height, null)
            {
                WindowHandle = window.Handle,
            });
        }))).ToList());
    }

    async void Region_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        var monitor = Native.MonitorAtCursor();
        var picked = RegionOverlay.Pick(monitor);
        Show();
        Activate();
        if (picked is { } target)
        {
            await BeginAsync(target);
        }
    }

    void Device_Click(object sender, RoutedEventArgs e)
    {
        if (_cameras.Count == 0)
        {
            ShowError(L.Text("没有找到采集设备。摄像头、采集卡会显示在这里。"));
            return;
        }

        ShowList(sender as FrameworkElement ?? this, _cameras.Select(name => (name, L.Text("作为主画面"), (Action)(() =>
            _ = BeginAsync(new CaptureTarget(CaptureKind.Device, name, 0, 0, 0, 0, name))))).ToList());
    }

    void Mic_Click(object sender, RoutedEventArgs e)
    {
        var devices = AudioCatalog.Microphones();
        if (!SettingsStore.Current.RecordMicrophone && devices.Count > 0)
        {
            SettingsStore.Current.RecordMicrophone = true;
            SettingsStore.Current.MicrophoneId ??= devices[0].Id;
            PaintToggles();
            SettingsStore.Save();
            ShowList(MicButton, devices.Select(device => (device.Name, L.Text("麦克风"), (Action)(() =>
            {
                SettingsStore.Current.MicrophoneId = device.Id;
                SettingsStore.Current.RecordMicrophone = true;
                PaintToggles();
                SettingsStore.Save();
            }))).ToList());
            return;
        }

        SettingsStore.Current.RecordMicrophone = !SettingsStore.Current.RecordMicrophone;
        PaintToggles();
        SettingsStore.Save();
    }

    void Camera_Click(object sender, RoutedEventArgs e)
    {
        if (!SettingsStore.Current.RecordCamera && _cameras.Count > 0)
        {
            SettingsStore.Current.RecordCamera = true;
            SettingsStore.Current.CameraName ??= _cameras[0];
            PaintToggles();
            SettingsStore.Save();
            ShowList(CameraButton, _cameras.Select(name => (name, L.Text("画中画"), (Action)(() =>
            {
                SettingsStore.Current.CameraName = name;
                SettingsStore.Current.RecordCamera = true;
                PaintToggles();
                SettingsStore.Save();
            }))).ToList());
            return;
        }

        SettingsStore.Current.RecordCamera = !SettingsStore.Current.RecordCamera;
        PaintToggles();
        SettingsStore.Save();
    }

    void SystemAudio_Click(object sender, RoutedEventArgs e)
    {
        var devices = AudioCatalog.Speakers();
        if (!SettingsStore.Current.RecordSystemAudio && devices.Count > 0)
        {
            SettingsStore.Current.RecordSystemAudio = true;
            SettingsStore.Current.PlaybackDeviceId ??= devices[0].Id;
            PaintToggles();
            SettingsStore.Save();
            ShowList(AudioButton, devices.Select(device => (device.Name, L.Text("系统声音"), (Action)(() =>
            {
                SettingsStore.Current.PlaybackDeviceId = device.Id;
                SettingsStore.Current.RecordSystemAudio = true;
                PaintToggles();
                SettingsStore.Save();
            }))).ToList());
            return;
        }

        SettingsStore.Current.RecordSystemAudio = !SettingsStore.Current.RecordSystemAudio;
        PaintToggles();
        SettingsStore.Save();
    }

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = L.Text("项目保存位置"),
            InitialDirectory = SettingsStore.Current.ProjectsDirectory,
        };
        if (dialog.ShowDialog(this) == true)
        {
            SettingsStore.Current.ProjectsDirectory = dialog.FolderName;
            SettingsStore.Save();
            FolderLabel.Text = Path.GetFileName(dialog.FolderName);
        }
    }

    void Menu_Click(object sender, RoutedEventArgs e)
    {
        var items = new List<(string Title, string Detail, Action Run)>
        {
            (L.Text("导入视频"), L.Text("做成可剪辑的项目"), () => _ = ImportAsync()),
            (L.Text("导入 macOS 项目"), L.Text("打开 Mac 版 .dogscproject 包"), () => _ = ImportMacAsync()),
            (L.Text("打开项目"), L.Text("打开 .dogscproject 文件夹"), () => OpenProject()),
            (L.Text("备忘录"), L.Text("不进入录制画面"), () => MemoWindow.Toggle()),
            (L.Text("使用引导"), L.Text("逐步介绍录制条"), () => ShowRecorderTour(0)),
            (L.Text("工具条大小"), L.Text("70%–110% · Ctrl + 滚轮"), () =>
            {
                SizePopup.PlacementTarget = _collapsed ? CompactMenu : CollapseButton;
                SizePopup.IsOpen = true;
                BarSizeSlider.Focus();
            }),
            (L.Text("设置"), L.Text("录制、编辑器、关于"), () =>
            {
                SettingsWindow.Open(this);
                PaintToggles();
            }),
        };
        foreach (var recent in SettingsStore.Current.RecentProjects.Where(Directory.Exists).Take(5))
        {
            var path = recent;
            items.Add((Path.GetFileName(path), L.Text("最近"), () => EditorWindow.Open(path)));
        }

        items.Add((L.Text("退出"), "", Close));
        ShowList(sender as FrameworkElement ?? this, items);
    }

    async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null)
        {
            await StopAsync();
        }

        Close();
    }

    async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            if (_paused)
            {
                await _session.ResumeAsync();
                _paused = false;
            }
            else
            {
                _timer.Stop();
                await _session.PauseAsync();
                _paused = true;
            }
            PaintElapsedTime();
            PaintPauseState();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
            if (!_paused && _session is not null) _timer.Start();
        }
    }

    void Marker_Click(object sender, RoutedEventArgs e)
    {
        _session?.Mark(null);
        MarkerCount.Text = (_session?.MarkerCount ?? 0).ToString();
    }
    void Memo_Click(object sender, RoutedEventArgs e) => MemoWindow.Toggle();
    void RecordingMic_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.HasMicrophone != true) return;
        _session.SetMicrophoneMuted(!_session.MicrophoneMuted); PaintRecordingMicrophone();
    }
    void PaintRecordingMicrophone()
    {
        foreach (var button in new[] { RecordingMicButton, CompactRecordingMic })
        {
            button.IsEnabled = _session?.HasMicrophone == true;
            button.Opacity = _session?.MicrophoneMuted == true ? .45 : 1;
            button.ToolTip = !button.IsEnabled ? L.Text("此轮录制未启用麦克风") : _session!.MicrophoneMuted ? L.Text("恢复麦克风") : L.Text("静音麦克风");
            System.Windows.Automation.AutomationProperties.SetName(button, (string)button.ToolTip);
        }
    }

    async void Stop_Click(object sender, RoutedEventArgs e) => await StopAsync();

    async Task BeginAsync(CaptureTarget target)
    {
        if (_session is not null || _busy)
        {
            return;
        }

        ListPopup.IsOpen = false;
        var ffmpeg = FfmpegTools.LocateFfmpeg();
        var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg);
        if (ffmpeg is null || ffprobe is null)
        {
            ShowError(L.Text("没有找到 ffmpeg。安装后把它加入 PATH，或到设置里指定 ffmpeg.exe。"));
            return;
        }

        _busy = true;
        _lastTarget = target;
        string? directory = null;
        try
        {
            await CountdownWindow.RunAsync(Native.MonitorAtCursor(), SettingsStore.Current.RecordCountdownSeconds);
            directory = ProjectFactory.CreateDirectory(L.Text("录制 ") + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
            var options = new RecordingOptions
            {
                FrameRate = SettingsStore.Current.FrameRate >= 60 ? 60 : 30,
                Microphone = SettingsStore.Current.RecordMicrophone,
                MicrophoneId = SettingsStore.Current.MicrophoneId,
                SystemAudio = SettingsStore.Current.RecordSystemAudio,
                PlaybackDeviceId = SettingsStore.Current.PlaybackDeviceId,
                Camera = SettingsStore.Current.RecordCamera,
                CameraName = SettingsStore.Current.CameraName,
            };
            var session = new RecordingSession(ffmpeg, ffprobe, target, options, directory);
            await session.StartAsync();
            _session = session;
            _directory = directory;
            _paused = false;
            PaintPauseState();
            TimerLabel.Text = CompactTimer.Text = "00:00";
            MarkerCount.Text = "0";
            PaintRecordingMicrophone();
            RecordingLabel.Text = target.Label;
            ApplyBarLayout(true);
            ErrorText.Text = "";
            RegisterHotkeys();
            _timer.Start();
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex.ToString());
            ShowError(FirstLine(ex.Message));
            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session = null;
            }
            else if (directory is not null)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch
                {
                    // An empty failed recording folder can be removed later.
                }
            }
        }
        finally
        {
            _busy = false;
        }
    }

    async Task StopAsync()
    {
        var session = _session;
        var directory = _directory;
        if (session is null || directory is null)
        {
            return;
        }

        _busy = true;
        UnregisterHotkeys();
        _timer.Stop();
        ContentHost.IsEnabled = false;
        try
        {
            var project = await session.FinishAsync();
            SettingsStore.Remember(directory);
            new RecordingCompleteWindow(directory).Show();
            _ = project;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // The partial recording can stay on disk for recovery.
            }
        }
        finally
        {
            await session.DisposeAsync();
            _session = null;
            _directory = null;
            _busy = false;
            ContentHost.IsEnabled = true;
            ApplyBarLayout(true);
        }
    }

    async Task ImportAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = L.Text("视频|*.mp4;*.mov;*.mkv;*.webm;*.avi|所有文件|*.*"),
            Title = L.Text("导入视频"),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var ffmpeg = FfmpegTools.LocateFfmpeg();
        var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg);
        if (ffmpeg is null || ffprobe is null)
        {
            ShowError(L.Text("没有找到 ffmpeg，暂时不能导入。"));
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var directory = await ProjectFactory.ImportAsync(ffmpeg, ffprobe, dialog.FileName);
            EditorWindow.Open(directory);
        }
        catch (Exception ex)
        {
            ShowError(FirstLine(ex.Message));
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    void OpenProject()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.Text("打开 DogSC 项目") };
        if (dialog.ShowDialog(this) == true)
        {
            EditorWindow.Open(dialog.FolderName);
        }
    }

    /// <summary>选一个 macOS .dogscproject 包，转成 Windows 项目并打开。</summary>
    async Task ImportMacAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.Text("选择 macOS 的 .dogscproject 包") };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var ffmpeg = FfmpegTools.LocateFfmpeg();
        var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg);
        if (ffprobe is null)
        {
            ShowError(L.Text("需要 ffprobe 才能补全导入项目的媒体信息。"));
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var (directory, result) = await ProjectFactory.ImportMacAsync(ffmpeg, ffprobe, dialog.FolderName);
            EditorWindow.Open(directory);
            if (result.DroppedFields.Count > 0)
            {
                AppPaths.Log(L.Text("macOS 导入丢弃字段：") + string.Join("；", result.DroppedFields));
                MessageBox.Show(this, L.Text("项目已导入。以下设置暂未迁移：\n\n") + string.Join("\n", result.DroppedFields.Distinct()), L.Text("Mac 项目导入说明"));
            }

            var videos = result.Project.Assets.SelectMany(a => new[] { a.Video, a.Camera });
            if (ffmpeg is null && videos.Any(v => v?.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) == true))
            {
                ShowError(L.Text("已导入，但摄像头/屏幕是 .mov：预览可能黑屏，导出不受影响。装 ffmpeg 可自动转码。"));
            }
        }
        catch (Exception ex)
        {
            ShowError(FirstLine(ex.Message));
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    void ShowList(FrameworkElement target, IReadOnlyList<(string Title, string Detail, Action Run)> items)
    {
        ListItems.Children.Clear();
        foreach (var item in items)
        {
            var button = new Button { Style = (Style)FindResource("QuietButton"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 1, 0, 1) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = item.Title, FontFamily = (FontFamily)FindResource("FontMedium"), FontWeight = FontWeights.Medium });
            if (!string.IsNullOrWhiteSpace(item.Detail))
            {
                stack.Children.Add(new TextBlock { Text = item.Detail, FontSize = 11, Foreground = (Brush)FindResource("InkSecondaryBrush") });
            }

            button.Content = stack;
            System.Windows.Automation.AutomationProperties.SetName(button, item.Title);
            var run = item.Run;
            button.Click += (_, _) =>
            {
                ListPopup.IsOpen = false;
                run();
            };
            ListItems.Children.Add(button);
        }

        ListPopup.PlacementTarget = target;
        ListPopup.IsOpen = true;
    }

    void PaintToggles()
    {
        MicDot.Visibility = SettingsStore.Current.RecordMicrophone ? Visibility.Visible : Visibility.Collapsed;
        CameraDot.Visibility = SettingsStore.Current.RecordCamera ? Visibility.Visible : Visibility.Collapsed;
        AudioDot.Visibility = SettingsStore.Current.RecordSystemAudio ? Visibility.Visible : Visibility.Collapsed;
        MicButton.ToolTip = SettingsStore.Current.RecordMicrophone ? L.Text("麦克风已开启，点击关闭") : L.Text("麦克风已关闭，点击选择并开启");
        CameraButton.ToolTip = SettingsStore.Current.RecordCamera ? L.Text("摄像头画中画已开启，点击关闭") : L.Text("摄像头画中画已关闭，点击选择并开启");
        AudioButton.ToolTip = SettingsStore.Current.RecordSystemAudio ? L.Text("系统声音已开启，点击关闭") : L.Text("系统声音已关闭，点击选择并开启");
    }

    /// <summary>Mac 录制恢复：启动时扫描项目目录，给只有 media/ 的半成品目录重建 project.json。</summary>
    async Task RecoverPartialAsync()
    {
        try
        {
            var ffprobe = FfmpegTools.LocateFfprobe(FfmpegTools.LocateFfmpeg());
            if (ffprobe is null)
            {
                return;
            }

            var count = await ProjectFactory.RecoverPartialAsync(ffprobe);
            if (count > 0)
            {
                ShowError(L.Text("已恢复 ") + count + L.Text(" 个中断的录制，可在最近项目里打开。"));

            }
        }
        catch (Exception ex)
        {
            AppPaths.Log(L.Text("录制恢复扫描失败：") + ex.Message);
        }
    }

    void ShowError(string message)
    {
        ErrorText.Text = message.ReplaceLineEndings(" ");
    }

    void RegisterHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.RegisterHotKey(hwnd, 1, 0x0001 | 0x0002 | 0x4000, 0x4D);
        Native.RegisterHotKey(hwnd, 2, 0x0001 | 0x0002 | 0x4000, 0x53);
    }

    void UnregisterHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.UnregisterHotKey(hwnd, 1);
        Native.UnregisterHotKey(hwnd, 2);
    }

    IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0046 && _positionReady && !_dragging && !_changingLayout)
            WindowPlacement.AnchorPosition(this, lParam, _anchor, _work, _dockOrigin, DockProgress);
        if (message == 0x0216 && _dragging)
        {
            WindowPlacement.ConstrainMove(lParam);
            handled = true;
            return new IntPtr(1);
        }
        if (!_dragging && (message is 0x007E or 0x001A or 0x02E0))
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ApplyBarLayout(false)));
        if (message == 0x0312 && _session is not null)
        {
            if (wParam.ToInt32() == 1)
            {
                _session.Mark(null);
                MarkerCount.Text = _session.MarkerCount.ToString();
            }
            else if (wParam.ToInt32() == 2)
            {
                _ = StopAsync();
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    static string FirstLine(string message)
    {
        var line = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? message;
        return line.Length > 180 ? line[..180] : line;
    }
}
