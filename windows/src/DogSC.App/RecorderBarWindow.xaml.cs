using System.Windows.Interop;
using System.Windows.Threading;
using DogSC.Core;

namespace DogSC;

public partial class RecorderBarWindow : Window
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly HwndSourceHook _hook;
    RecordingSession? _session;
    string? _directory;
    CaptureTarget? _lastTarget;
    bool _paused;
    bool _busy;
    bool _closing;
    IReadOnlyList<string> _cameras = [];
    HwndSource? _source;

    public RecorderBarWindow()
    {
        InitializeComponent();
        KeyboardNavigation.SetTabNavigation(ListItems, KeyboardNavigationMode.Cycle);
        ListPopup.Opened += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input,
            new Action(() => ListItems.Children.OfType<Button>().FirstOrDefault()?.Focus()));
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
        };
        _hook = WndProc;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIconMaster.png"));
        FolderLabel.Text = Path.GetFileName(SettingsStore.Current.ProjectsDirectory);
        PaintToggles();
        _timer.Tick += (_, _) =>
        {
            if (_session is null)
            {
                return;
            }

            var elapsed = TimeSpan.FromSeconds(_session.Elapsed);
            TimerLabel.Text = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        };
        Loaded += async (_, _) => await LoadDevicesAsync();
        MouseLeftButtonDown += DragBar;
        Loaded += (_, _) => WindowPlacement.KeepInWorkArea(this);
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

    void DragBar(object sender, MouseButtonEventArgs args) => WindowPlacement.Drag(this, args);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = (HwndSource)PresentationSource.FromVisual(this)!;
        WindowPlacement.Exclude(this);
        _source.AddHook(_hook);
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
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
            ShowError("没有找到 ffmpeg。安装后把它加入 PATH，或到设置里指定 ffmpeg.exe。");
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
        ShowList(DisplayButton, monitors.Select(monitor => (monitor.Name, monitor.Width + " × " + monitor.Height, (Action)(() =>
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
            ShowError("没有找到可录制的窗口。");
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
            ShowError("没有找到采集设备。摄像头、采集卡会显示在这里。");
            return;
        }

        ShowList(sender as FrameworkElement ?? this, _cameras.Select(name => (name, "作为主画面", (Action)(() =>
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
            ShowList(MicButton, devices.Select(device => (device.Name, "麦克风", (Action)(() =>
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
            ShowList(CameraButton, _cameras.Select(name => (name, "画中画", (Action)(() =>
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
            ShowList(AudioButton, devices.Select(device => (device.Name, "系统声音", (Action)(() =>
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
            Title = "项目保存位置",
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
            ("导入视频", "做成可剪辑的项目", () => _ = ImportAsync()),
            ("导入 macOS 项目", "打开 Mac 版 .dogscproject 包", () => _ = ImportMacAsync()),
            ("打开项目", "打开 .dogscproject 文件夹", () => OpenProject()),
            ("备忘录", "不进入录制画面", () => MemoWindow.Toggle()),
            ("设置", "帧率、ffmpeg、关于", () => new SettingsWindow { Owner = this }.ShowDialog()),
        };
        foreach (var recent in SettingsStore.Current.RecentProjects.Where(Directory.Exists).Take(5))
        {
            var path = recent;
            items.Add((Path.GetFileName(path), "最近", () => EditorWindow.Open(path)));
        }

        items.Add(("退出", "", Close));
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
                PauseLabel.Text = "暂停";
                PauseIcon.Kind = IconKind.Pause;
                _timer.Start();
            }
            else
            {
                await _session.PauseAsync();
                _paused = true;
                PauseLabel.Text = "继续";
                PauseIcon.Kind = IconKind.Play;
                _timer.Stop();
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    void Marker_Click(object sender, RoutedEventArgs e) => _session?.Mark(null);

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
            ShowError("没有找到 ffmpeg。安装后把它加入 PATH，或到设置里指定 ffmpeg.exe。");
            return;
        }

        _busy = true;
        _lastTarget = target;
        string? directory = null;
        try
        {
            await CountdownWindow.RunAsync(Native.MonitorAtCursor());
            directory = ProjectFactory.CreateDirectory("录制 " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
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
            PauseLabel.Text = "暂停";
            PauseIcon.Kind = IconKind.Pause;
            RecordingLabel.Text = target.Label;
            SetupPanel.Visibility = Visibility.Collapsed;
            RecordingPanel.Visibility = Visibility.Visible;
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
        SetupPanel.Visibility = Visibility.Visible;
        RecordingPanel.Visibility = Visibility.Collapsed;
        try
        {
            var project = await session.FinishAsync();
            SettingsStore.Remember(directory);
            EditorWindow.Open(directory);
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
        }
    }

    async Task ImportAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "视频|*.mp4;*.mov;*.mkv;*.webm;*.avi|所有文件|*.*",
            Title = "导入视频",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var ffmpeg = FfmpegTools.LocateFfmpeg();
        var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg);
        if (ffmpeg is null || ffprobe is null)
        {
            ShowError("没有找到 ffmpeg，暂时不能导入。");
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
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "打开 DogSC 项目" };
        if (dialog.ShowDialog(this) == true)
        {
            EditorWindow.Open(dialog.FolderName);
        }
    }

    /// <summary>选一个 macOS .dogscproject 包，转成 Windows 项目并打开。</summary>
    async Task ImportMacAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择 macOS 的 .dogscproject 包" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var ffmpeg = FfmpegTools.LocateFfmpeg();
        var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg);
        if (ffprobe is null)
        {
            ShowError("需要 ffprobe 才能补全导入项目的媒体信息。");
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var (directory, result) = await ProjectFactory.ImportMacAsync(ffmpeg, ffprobe, dialog.FolderName);
            EditorWindow.Open(directory);
            if (result.DroppedFields.Count > 0)
            {
                AppPaths.Log("macOS 导入丢弃字段：" + string.Join("；", result.DroppedFields));
            }

            var videos = result.Project.Assets.SelectMany(a => new[] { a.Video, a.Camera });
            if (ffmpeg is null && videos.Any(v => v?.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) == true))
            {
                ShowError("已导入，但摄像头/屏幕是 .mov：预览可能黑屏，导出不受影响。装 ffmpeg 可自动转码。");
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
            stack.Children.Add(new TextBlock { Text = item.Title, FontFamily = (FontFamily)FindResource("FontMedium") });
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
        MicButton.ToolTip = SettingsStore.Current.RecordMicrophone ? "麦克风已开启，点击关闭" : "麦克风已关闭，点击选择并开启";
        CameraButton.ToolTip = SettingsStore.Current.RecordCamera ? "摄像头画中画已开启，点击关闭" : "摄像头画中画已关闭，点击选择并开启";
        AudioButton.ToolTip = SettingsStore.Current.RecordSystemAudio ? "系统声音已开启，点击关闭" : "系统声音已关闭，点击选择并开启";
    }

    void ShowError(string message)
    {
        UpdateText.Visibility = Visibility.Collapsed;
        ErrorText.Text = message.ReplaceLineEndings(" ");
    }

    /// <summary>录制条角落提示新版本；点击用默认浏览器打开 release 页。
    /// 用独立的 UpdateText，不改 ErrorText 的样式/事件，避免污染错误文本。</summary>
    public void ShowUpdateNotice(UpdateInfo info)
    {
        if (!info.HasUpdate || info.Url.Length == 0)
        {
            return;
        }

        ErrorText.Text = "";
        UpdateText.Text = $"发现新版本 {info.Latest} — 点击查看";
        UpdateText.Visibility = Visibility.Visible;
        UpdateText.MouseLeftButtonDown += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.Url) { UseShellExecute = true });
            }
            catch
            {
                // 打不开浏览器就算了，提示还在。
            }
        };
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
        if (message == 0x0312 && _session is not null)
        {
            if (wParam.ToInt32() == 1)
            {
                _session.Mark(null);
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
