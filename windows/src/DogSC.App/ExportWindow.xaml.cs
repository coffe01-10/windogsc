using System.Diagnostics;
using DogSC.Core;

namespace DogSC;

public partial class ExportWindow : Window
{
    readonly ProjectDocument _project;
    readonly string _directory;
    CancellationTokenSource? _cancel;
    bool _audioOnly;
    string _resolution;
    int _fps;
    bool _running;

    public ExportWindow(ProjectDocument project, string directory)
    {
        _project = project;
        _directory = directory;
        _resolution = SettingsStore.Current.ExportResolution;
        _fps = SettingsStore.Current.ExportFps is 60 ? 60 : 30;
        InitializeComponent();
        ProjectName.Text = project.Name;
        FileName.Text = project.Name + "-" + _fps + "fps";
        FolderButton.Content = SettingsStore.Current.ExportDirectory;
        foreach (var option in new[] { ("原始素材", "source"), ("1080p", "1080"), ("1440p", "1440"), ("4K", "2160") })
        {
            var button = new Button
            {
                Content = option.Item1,
                Tag = option.Item2,
                Style = (Style)FindResource("ChipButton"),
            };
            button.Click += Resolution_Click;
            ResolutionRow.Children.Add(button);
        }

        Paint();
    }

    void Video_Click(object sender, RoutedEventArgs e)
    {
        _audioOnly = false;
        Paint();
    }

    void Audio_Click(object sender, RoutedEventArgs e)
    {
        _audioOnly = true;
        Paint();
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
            FolderButton.Content = dialog.FolderName;
        }
    }

    async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            return;
        }

        var ffmpeg = FfmpegTools.LocateFfmpeg();
        if (ffmpeg is null)
        {
            Status.Text = "没有找到 ffmpeg。";
            return;
        }

        var extension = _audioOnly ? ".m4a" : ".mp4";
        var name = ProjectFile.SanitizeName(FileName.Text) + extension;
        var output = Path.Combine(SettingsStore.Current.ExportDirectory, name);
        SettingsStore.Current.ExportResolution = _resolution;
        SettingsStore.Current.ExportFps = _fps;
        SettingsStore.Save();
        _running = true;
        ExportButton.IsEnabled = false;
        _cancel = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        Status.Text = "正在导出…";
        try
        {
            var service = new ExportService(ffmpeg);
            service.Progress += value => Dispatcher.Invoke(() =>
            {
                Bar.Value = value;
                Status.Text = "正在导出… " + (int)(value * 100) + "% · 已用 " + clock.Elapsed.ToString(@"m\:ss");
            });
            await service.ExportAsync(_project, _directory, new ExportOptions
            {
                AudioOnly = _audioOnly,
                Resolution = _resolution,
                Fps = _fps,
            }, output, _cancel.Token);
            Status.Text = "已导出到 " + output;
            Bar.Value = 1;
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + output + "\"") { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            Status.Text = "已取消。";
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex.ToString());
            Status.Text = ex.Message.Split('\n').LastOrDefault()?.Trim() ?? "导出失败。";
        }
        finally
        {
            _running = false;
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

    void Paint()
    {
        VideoChoice.Background = _audioOnly ? (Brush)FindResource("WellBrush") : (Brush)FindResource("AccentBrush");
        VideoChoice.Foreground = _audioOnly ? (Brush)FindResource("InkBrush") : (Brush)FindResource("OnAccentBrush");
        AudioChoice.Background = _audioOnly ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("WellBrush");
        AudioChoice.Foreground = _audioOnly ? (Brush)FindResource("OnAccentBrush") : (Brush)FindResource("InkBrush");
        Fps30.Background = _fps == 30 ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("WellBrush");
        Fps30.Foreground = _fps == 30 ? (Brush)FindResource("OnAccentBrush") : (Brush)FindResource("InkBrush");
        Fps60.Background = _fps == 60 ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("WellBrush");
        Fps60.Foreground = _fps == 60 ? (Brush)FindResource("OnAccentBrush") : (Brush)FindResource("InkBrush");
        foreach (var child in ResolutionRow.Children.OfType<Button>())
        {
            var selected = (string)child.Tag == _resolution;
            child.Background = selected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("WellBrush");
            child.Foreground = selected ? (Brush)FindResource("OnAccentBrush") : (Brush)FindResource("InkBrush");
        }

        var (width, height) = CanvasLayout.OutputSize(_project.Width, _project.Height, _project.Canvas.Aspect, _audioOnly ? "source" : _resolution);
        var duration = TimelineMath.Duration(_project);
        var bytes = CanvasLayout.EstimateBytes(width, height, _fps, duration, _audioOnly);
        Estimate.Text = "预计大小 · 约 " + (bytes / 1024d / 1024d).ToString("0.0") + " MB";
        Summary.Text = _audioOnly
            ? "M4A · " + duration.ToString("0.00") + " 秒"
            : "MP4 · " + width + " × " + height + " · " + _fps + " fps · " + duration.ToString("0.00") + " 秒";
        ExportButton.Content = _audioOnly ? "开始导出音频" : "开始导出视频";
        FolderButton.Content = SettingsStore.Current.ExportDirectory;
    }
}
