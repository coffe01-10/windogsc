using System.Diagnostics;

namespace DogSC;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Paint();
    }

    void Paint()
    {
        var fps60 = SettingsStore.Current.FrameRate >= 60;
        Fps30.Background = fps60 ? (Brush)FindResource("WellBrush") : (Brush)FindResource("AccentBrush");
        Fps30.Foreground = fps60 ? (Brush)FindResource("InkBrush") : (Brush)FindResource("OnAccentBrush");
        Fps60.Background = fps60 ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("WellBrush");
        Fps60.Foreground = fps60 ? (Brush)FindResource("OnAccentBrush") : (Brush)FindResource("InkBrush");
        FfmpegLabel.Text = FfmpegTools.LocateFfmpeg() ?? "未找到";
        UpdatesCheck.IsChecked = SettingsStore.Current.CheckUpdates;
        UpdateLabel.Text = "当前版本 " + UpdateService.CurrentVersion;
    }

    void UpdatesCheck_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.CheckUpdates = UpdatesCheck.IsChecked == true;
        SettingsStore.Save();
    }

    async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateLabel.Text = "检查中…";
        var info = await UpdateService.CheckAsync();
        if (info.HasUpdate)
        {
            UpdateLabel.Text = $"发现新版本 {info.Latest}";
            try
            {
                if (info.Url.Length > 0)
                {
                    Process.Start(new ProcessStartInfo(info.Url) { UseShellExecute = true });
                }
            }
            catch
            {
                // 打不开浏览器就只留文字。
            }
        }
        else if (info.Error is not null)
        {
            UpdateLabel.Text = info.Error;
        }
        else
        {
            UpdateLabel.Text = $"已是最新（{UpdateService.CurrentVersion}）";
        }
    }

    void Fps30_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.FrameRate = 30;
        SettingsStore.Save();
        Paint();
    }

    void Fps60_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.FrameRate = 60;
        SettingsStore.Save();
        Paint();
    }

    void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "ffmpeg|ffmpeg.exe", Title = "选择 ffmpeg.exe" };
        if (dialog.ShowDialog(this) == true)
        {
            SettingsStore.Current.FfmpegPath = dialog.FileName;
            SettingsStore.Save();
            Paint();
        }
    }

    void Link_Click(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
