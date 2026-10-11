using System.Diagnostics;

namespace DogSC;

public partial class SettingsWindow : Window
{
    static string _lastSection = "general";
    public static void Open(Window owner)
    {
        var settings = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (settings is null) { settings = new SettingsWindow { Owner = owner }; settings.Show(); }
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Activate();
    }

    internal void RefreshLanguage() => Paint();
    public SettingsWindow()
    {
        InitializeComponent();
        BuildSettingsPages();
        Paint();
        NavigationGrid.SizeChanged += (_, _) => { if (_section.Length > 0) FluidMotion.Select(NavigationSelection, _navigation[_section], NavigationGrid); };
        Loaded += (_, _) => { Height = 34 + Math.Min(640, Math.Max(400, WindowPlacement.WorkArea(this).Height - 100)); WindowPlacement.KeepInWorkArea(this, center: true); SelectSettingsSection(_lastSection, false); FluidMotion.Entrance(SettingsRoot); };
    }

    void Chip(Button b, bool on)
    {
        b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(0);
        b.SetResourceReference(ForegroundProperty, on ? "InkBrush" : "InkSecondaryBrush");
    }

    void Paint()
    {
        var s = SettingsStore.Current;
        Chip(Fps30, s.FrameRate < 60); Chip(Fps60, s.FrameRate >= 60);
        Chip(QualityLow, s.PreviewQuality == "low"); Chip(QualityFull, s.PreviewQuality != "low");
        var shape = s.CameraDefaultShape;
        Chip(CamCircle, shape == "circle"); Chip(CamSquare, shape == "square"); Chip(CamRounded, shape == "rounded");
        Chip(Cd0, s.RecordCountdownSeconds <= 0); Chip(Cd3, s.RecordCountdownSeconds == 3); Chip(Cd5, s.RecordCountdownSeconds >= 5);
        MicDefaultBox.IsChecked = s.RecordMicrophone;
        SysAudioDefaultBox.IsChecked = s.RecordSystemAudio;
        FfmpegLabel.Text = FfmpegTools.LocateFfmpeg() ?? L.Text("未找到");
        UpdateLabel.Text = L.Text("当前版本 ") + AppVersion.Current;
        ResetDone.Text = L.Text("恢复默认轨道显示、时间线缩放及面板大小。");
        PaintSettingsChoices();
    }

    void Set(Action apply) { apply(); SettingsStore.Save(); Paint(); }
    void Fps30_Click(object s, RoutedEventArgs e) => Set(() => SettingsStore.Current.FrameRate = 30);
    void Fps60_Click(object s, RoutedEventArgs e) => Set(() => SettingsStore.Current.FrameRate = 60);
    void SetPreviewQuality(string quality)
    {
        Set(() => SettingsStore.Current.PreviewQuality = quality);
        foreach (var editor in Application.Current.Windows.OfType<EditorWindow>()) editor.SyncPreviewQuality();
    }
    void QualityLow_Click(object s, RoutedEventArgs e) => SetPreviewQuality("low");
    void QualityFull_Click(object s, RoutedEventArgs e) => SetPreviewQuality("full");
    void CamCircle_Click(object s, RoutedEventArgs e) => Set(() => SettingsStore.Current.CameraDefaultShape = "circle");
    void CamSquare_Click(object s, RoutedEventArgs e) => Set(() => SettingsStore.Current.CameraDefaultShape = "square");
    void CamRounded_Click(object s, RoutedEventArgs e) => Set(() => SettingsStore.Current.CameraDefaultShape = "rounded");
    void Cd0_Click(object s, RoutedEventArgs e) => Set(() => SettingsStore.Current.RecordCountdownSeconds = 0);
    void Cd3_Click(object s, RoutedEventArgs e) => Set(() => SettingsStore.Current.RecordCountdownSeconds = 3);
    void Cd5_Click(object s, RoutedEventArgs e) => Set(() => SettingsStore.Current.RecordCountdownSeconds = 5);
    void MicDefault_Changed(object s, RoutedEventArgs e) => Set(() => SettingsStore.Current.RecordMicrophone = MicDefaultBox.IsChecked == true);
    void SysAudioDefault_Changed(object s, RoutedEventArgs e) => Set(() => SettingsStore.Current.RecordSystemAudio = SysAudioDefaultBox.IsChecked == true);

    void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsStore.Current;
        s.TimelineTracks = 1;
        SettingsStore.Save();
        foreach (var editor in Application.Current.Windows.OfType<EditorWindow>()) editor.ResetLayout();
        ResetDone.Text = L.Text("已恢复默认：只显示缩放轨、时间线缩放重置。");
    }

    void ReplayTour_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.TourSeen = false;
        SettingsStore.Save();
        var editor = Application.Current.Windows.OfType<EditorWindow>().LastOrDefault();
        if (editor is null) ResetDone.Text = L.Text("下次打开编辑器会重新播放引导。");
        else { Close(); editor.ReplayTour(); }
    }

    void OpenPrivacy_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:privacy") { UseShellExecute = true });
    }

    void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "ffmpeg|ffmpeg.exe", Title = L.Text("选择 ffmpeg.exe") };
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
    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
}
