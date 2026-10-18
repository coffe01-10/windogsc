using System.Diagnostics;
using DogSC.Core;

namespace DogSC;

public partial class ExportWindow : Window
{
    internal void RefreshLanguage()
    {
        if (!_running && _completedOutput is null && LogButton.Visibility != Visibility.Visible) Paint();
    }
    readonly ProjectDocument _project;
    readonly string _directory;
    CancellationTokenSource? _cancel;
    bool _audioOnly;
    string _resolution;
    int _fps;
    bool _running;
    string? _completedOutput;
    string? _nameBeforeEditing;
    readonly double? _rangeStart, _rangeDuration;
    FluidSegments _outputChoices = null!, _fpsChoices = null!, _resolutionChoices = null!;

    public ExportWindow(ProjectDocument project, string directory, ImageSource? preview = null, double? rangeStart = null, double? rangeDuration = null)
    {
        _project = project;
        _rangeStart = rangeStart; _rangeDuration = rangeDuration;
        _directory = directory;
        _resolution = project.ExportResolution;
        _fps = project.ExportFps is 60 ? 60 : 30;
        InitializeComponent();
        PreviewImage.Source = preview;
        ProjectName.Text = project.Name;
        FileName.Text = project.Name + "-" + _fps + "fps";
        if (_rangeDuration is not null)
        {
            ProjectName.Text += " · " + L.Text("选中片段");
            FileName.Text = project.Name + "-" + L.Text("选中片段") + "-" + _fps + "fps";
        }
        FileNameDisplay.Text = FileName.Text;
        FolderPath.Text = SettingsStore.Current.ExportDirectory;
        foreach (var option in new[] { (L.Text("原始素材"), "source"), ("1080p", "1080"), ("1440p", "1440"), ("4K", "2160") })
        {
            var button = new Button
            {
                Content = option.Item1,
                Tag = option.Item2,
                Style = (Style)FindResource("SegmentButton"),
                MinHeight = 38, Margin = new Thickness(0, 0, 8, 0),
            };
            button.Click += Resolution_Click;
            ResolutionRow.Children.Add(button);
        }

        _outputChoices = WrapChoices(OutputChoicesRow, [VideoChoice, AudioChoice], 0);
        _fpsChoices = WrapChoices(FrameRateChoicesRow, [Fps30, Fps60], _fps == 60 ? 1 : 0);
        var resolutions = ResolutionRow.Children.OfType<Button>().ToArray();
        _resolutionChoices = WrapChoices(ResolutionRow, resolutions, Array.FindIndex(resolutions, b => (string)b.Tag == _resolution));
        Paint();
        Closing += (_, _) => _cancel?.Cancel();
        Loaded += (_, _) => WindowPlacement.KeepInWorkArea(this, center: true);
    }

    void Video_Click(object sender, RoutedEventArgs e)
    {
        var transition = FluidMotion.PrepareSwap(ExportSettings, ExportTransitionLayer);
        _audioOnly = false;
        Paint();
        transition();
    }

    void Audio_Click(object sender, RoutedEventArgs e)
    {
        var transition = FluidMotion.PrepareSwap(ExportSettings, ExportTransitionLayer);
        _audioOnly = true;
        Paint();
        transition();
    }

    void Resolution_Click(object sender, RoutedEventArgs e)
    {
        _resolution = (string)((Button)sender).Tag;
        Paint();
    }

    void Fps_Click(object sender, RoutedEventArgs e)
    {
        _fps = (string)((Button)sender).Tag == "60" ? 60 : 30;
        Paint();
    }

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = SettingsStore.Current.ExportDirectory };
        if (dialog.ShowDialog(this) == true)
        {
            SettingsStore.Current.ExportDirectory = dialog.FolderName;
            SettingsStore.Save();
            FolderPath.Text = dialog.FolderName;
        }
    }

    async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_completedOutput is not null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _completedOutput + "\"") { UseShellExecute = true });
            return;
        }
        if (_running)
        {
            return;
        }

        var ffmpeg = FfmpegTools.LocateFfmpeg();
        Status.Visibility = Visibility.Visible;
        if (ffmpeg is null)
        {
            Status.Text = L.Text("没有找到 ffmpeg。");
            return;
        }

        var extension = _audioOnly ? ".m4a" : ".mp4";
        var name = ProjectFile.SanitizeName(FileName.Text) + extension;
        var output = Path.Combine(SettingsStore.Current.ExportDirectory, name);
        SettingsStore.Current.ExportResolution = _resolution;
        SettingsStore.Current.ExportFps = _fps;
        _project.ExportResolution = _resolution;
        _project.ExportFps = _fps;
        _running = true;
        Height = 635;
        ExportSettings.IsEnabled = false;
        ExportButton.IsEnabled = false;
        _cancel = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        Bar.Visibility = Visibility.Visible;
        Bar.Value = 0;
        LogButton.Visibility = Visibility.Collapsed;
        Status.Text = L.Text("正在导出…");
        try
        {
            ProjectFile.Save(_directory, _project);
            SettingsStore.SaveOrThrow();
            var service = new ExportService(ffmpeg);
            service.Stage += stage => Dispatcher.Invoke(() => { clock.Restart(); Status.Text = stage; });
            service.Progress += value => Dispatcher.Invoke(() =>
            {
                Bar.Value = value;
                var remaining = value > 0.03 && clock.Elapsed.TotalSeconds > 3
                    ? L.Text(" · 预计剩余 ") + TimeSpan.FromSeconds(clock.Elapsed.TotalSeconds * (1 - value) / value).ToString(@"m\:ss") : "";
                Status.Text = L.Text("正在导出… ") + (int)(value * 100) + L.Text("% · 已用 ") + clock.Elapsed.ToString(@"m\:ss") + remaining;
            });
            await service.ExportAsync(_project, _directory, new ExportOptions
            {
                AudioOnly = _audioOnly,
                Resolution = _resolution,
                Fps = _fps,
                RangeStart = _rangeStart,
                RangeDuration = _rangeDuration,
            }, output, _cancel.Token);
            _completedOutput = output;
            if (SettingsStore.Current.ExportCompletionSoundEnabled) System.Media.SystemSounds.Asterisk.Play();
            Status.Text = L.Text("导出完成 · ") + output;
            Bar.Value = 1;
            ExportButton.Content = L.Text("在文件夹中显示");
            CancelButton.Content = L.Text("完成");
        }
        catch (OperationCanceledException)
        {
            Status.Text = L.Text("已取消。");
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex.ToString());
            L.Bind(Status, TextBlock.TextProperty, "导出失败，请重试。详细信息已保存到日志。");
            L.Bind(ExportButton, ContentControl.ContentProperty, "重试导出");
            LogButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _running = false;
            ExportSettings.IsEnabled = _completedOutput is null;
            ExportButton.IsEnabled = true;
            _cancel?.Dispose();
            _cancel = null;
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            _cancel?.Cancel();
            return;
        }

        Close();
    }

    void Log_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true });

    void Header_Down(object sender, MouseButtonEventArgs e)
    {
        WindowPlacement.Drag(this, e);
    }

    void FileNameRow_Down(object sender, MouseButtonEventArgs e)
    {
        if (_nameBeforeEditing is not null) return;
        _nameBeforeEditing = FileName.Text;
        FileNameDisplay.Visibility = FileNamePencil.Visibility = Visibility.Collapsed;
        FileName.Visibility = Visibility.Visible; FileName.Focus(); FileName.SelectAll();
        e.Handled = true;
    }

    void FinishFileName(bool cancel)
    {
        if (_nameBeforeEditing is null) return;
        FileName.Text = cancel || string.IsNullOrWhiteSpace(FileName.Text) ? _nameBeforeEditing : FileName.Text.Trim();
        _nameBeforeEditing = null; FileNameDisplay.Text = FileName.Text;
        FileName.Visibility = Visibility.Collapsed;
        FileNameDisplay.Visibility = FileNamePencil.Visibility = Visibility.Visible;
    }

    void FileName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        FinishFileName(e.Key == Key.Escape); e.Handled = true;
    }

    void FileName_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => FinishFileName(false);

    void Paint()
    {
        VideoSettings.Visibility = VideoPreview.Visibility = _audioOnly ? Visibility.Collapsed : Visibility.Visible;
        AudioPreview.Visibility = _audioOnly ? Visibility.Visible : Visibility.Collapsed;
        Height = _audioOnly ? 531 : 635;
        AudioDescription.Visibility = _audioOnly ? Visibility.Visible : Visibility.Collapsed;
        PreviewHeading.Text = _audioOnly ? L.Text("音频导出") : L.Text("当前画面");
        VideoMetadata.Visibility = _audioOnly ? Visibility.Collapsed : Visibility.Visible;
        ExtensionLabel.Text = _audioOnly ? ".m4a" : ".mp4";
        PaintChoice(VideoChoice, !_audioOnly);
        PaintChoice(AudioChoice, _audioOnly);
        PaintChoice(Fps30, _fps == 30);
        PaintChoice(Fps60, _fps == 60);
        var resolutionButtons = _resolutionChoices.Buttons.ToArray();
        foreach (var child in resolutionButtons)
        {
            var selected = (string)child.Tag == _resolution;
            PaintChoice(child, selected);
        }
        _outputChoices.Select(_audioOnly ? 1 : 0); _fpsChoices.Select(_fps == 60 ? 1 : 0);
        _resolutionChoices.Select(Array.FindIndex(resolutionButtons, b => (string)b.Tag == _resolution));

        var (width, height) = CanvasLayout.OutputSize(_project.Width, _project.Height, _project.Canvas.Aspect, _audioOnly ? "source" : _resolution);
        var duration = _rangeDuration ?? TimelineMath.Duration(_project);
        FormatValue.Text = _audioOnly ? "M4A" : "MP4";
        ResolutionValue.Text = width + " × " + height; FpsValue.Text = _fps + " fps";
        DurationValue.Text = TimeSpan.FromSeconds(duration).ToString(@"m\:ss\.ff");
        var bytes = CanvasLayout.EstimateBytes(width, height, _fps, duration, _audioOnly);
        Estimate.Text = L.Text("预计大小 · 约 ") + (bytes / 1024d / 1024d).ToString("0.0") + " MB";
        Summary.Text = _audioOnly
            ? "M4A · " + duration.ToString("0.00") + L.Text(" 秒")
            : "MP4 · " + width + " × " + height + " · " + _fps + " fps · " + duration.ToString("0.00") + L.Text(" 秒");
        var exportLabel = new StackPanel { Orientation = Orientation.Horizontal };
        exportLabel.Children.Add(new Icon { Kind = IconKind.Share, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) });
        var exportText = new TextBlock { Text = _audioOnly ? L.Text("导出音频") : L.Text("导出视频") };
        exportText.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        exportLabel.Children.Add(exportText);
        ExportButton.Content = exportLabel;
        FolderPath.Text = SettingsStore.Current.ExportDirectory;
    }

    void PaintChoice(Button button, bool selected)
    {
        var title = button == VideoChoice ? L.Text("视频") : button == AudioChoice ? L.Text("仅音频") : button == Fps30 ? "30 FPS" : button == Fps60 ? "60 FPS"
            : (string)button.Tag switch { "source" => L.Text("原始素材"), "1080" => "1080p", "1440" => "1440p", _ => "4K" };
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = button });
        label.SetBinding(TextBlock.FontFamilyProperty, new System.Windows.Data.Binding(nameof(Button.FontFamily)) { Source = button });
        content.Children.Add(label);
        if (selected) content.Children.Add(new Icon { Kind = IconKind.Checkmark, Width = 11, Height = 11, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0);
    }

    FluidSegments WrapChoices(System.Windows.Controls.Primitives.UniformGrid host, Button[] buttons, int selected)
    {
        host.Children.Clear(); host.Columns = 1;
        foreach (var button in buttons) { button.FontSize = 13; button.Margin = new Thickness(5, 0, 5, 0); button.MinHeight = 38; }
        var segments = new FluidSegments(buttons, selected, individualTiles: true) { Margin = new Thickness(-5, 0, -5, 0) }; host.Children.Add(segments);
        return segments;
    }
}
