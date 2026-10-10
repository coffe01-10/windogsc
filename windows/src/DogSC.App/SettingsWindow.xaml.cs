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
        var lowQuality = SettingsStore.Current.PreviewQuality == "low";
        QualityLow.Background = lowQuality ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("WellBrush");
        QualityLow.Foreground = lowQuality ? (Brush)FindResource("OnAccentBrush") : (Brush)FindResource("InkBrush");
        QualityFull.Background = lowQuality ? (Brush)FindResource("WellBrush") : (Brush)FindResource("AccentBrush");
        QualityFull.Foreground = lowQuality ? (Brush)FindResource("InkBrush") : (Brush)FindResource("OnAccentBrush");
        FfmpegLabel.Text = FfmpegTools.LocateFfmpeg() ?? "未找到";
        UpdateLabel.Text = "当前版本 " + AppVersion.Current;
    }

    void Fps30_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.FrameRate = 30;
        SettingsStore.Save();
        Paint();
    }

    void QualityLow_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.PreviewQuality = "low";
        SettingsStore.Save();
        Paint();
    }

    void QualityFull_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.PreviewQuality = "full";
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
