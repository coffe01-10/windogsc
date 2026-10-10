namespace DogSC;

public partial class MemoWindow : Window
{
    static MemoWindow? _current;
    bool _ready;
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public MemoWindow()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SettingsStore.Save(); };
        Body.Text = SettingsStore.Current.MemoText;
        SizeSlider.Value = SettingsStore.Current.MemoSize;
        OpacitySlider.Value = SettingsStore.Current.MemoOpacity;
        Body.FontSize = SizeSlider.Value;
        BackgroundLayer.Opacity = OpacitySlider.Value;
        if (!double.IsNaN(SettingsStore.Current.MemoLeft))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SettingsStore.Current.MemoLeft;
            Top = SettingsStore.Current.MemoTop;
        }

        _ready = true;
        PaintMemo();
        Loaded += (_, _) => WindowPlacement.KeepInWorkArea(this);
    }

    public static void Toggle()
    {
        if (_current is { IsVisible: true })
        {
            _current.Close();
            return;
        }

        _current = new MemoWindow();
        _current.Owner = Application.Current.Windows.OfType<RecorderBarWindow>().FirstOrDefault();
        _current.Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowPlacement.Exclude(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _saveTimer.Stop();
        SettingsStore.Current.MemoLeft = Left;
        SettingsStore.Current.MemoTop = Top;
        SettingsStore.Save();
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
        base.OnClosed(e);
    }

    void Drag(object sender, MouseButtonEventArgs e)
    {
        WindowPlacement.Drag(this, e);
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void EditMode_Click(object sender, RoutedEventArgs e) => SetReadMode(false);
    void ReadMode_Click(object sender, RoutedEventArgs e) => SetReadMode(true);
    void Smaller_Click(object sender, RoutedEventArgs e) => SizeSlider.Value = Math.Max(SizeSlider.Minimum, SizeSlider.Value - 2);
    void Larger_Click(object sender, RoutedEventArgs e) => SizeSlider.Value = Math.Min(SizeSlider.Maximum, SizeSlider.Value + 2);
    void SetReadMode(bool read)
    {
        SettingsStore.Current.MemoReadMode = read; PaintMemo(); QueueSave();
        if (!read) Body.Focus();
    }
    void PaintMemo()
    {
        Body.IsReadOnly = SettingsStore.Current.MemoReadMode;
        Body.CaretBrush = Body.IsReadOnly ? Brushes.Transparent : (Brush)FindResource("InkBrush");
        ControlAppearance.SelectChip(EditModeButton, !Body.IsReadOnly);
        ControlAppearance.SelectChip(ReadModeButton, Body.IsReadOnly);
        SizeLabel.Text = Math.Round(SizeSlider.Value).ToString();
        OpacityLabel.Text = Math.Round(OpacitySlider.Value * 100) + "%";
    }

    void Body_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        SettingsStore.Current.MemoText = Body.Text;
        QueueSave();
    }

    void Size_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready)
        {
            return;
        }

        Body.FontSize = e.NewValue;
        SettingsStore.Current.MemoSize = e.NewValue;
        PaintMemo();
        QueueSave();
    }

    void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || Shell is null)
        {
            return;
        }

        BackgroundLayer.Opacity = e.NewValue;
        SettingsStore.Current.MemoOpacity = e.NewValue;
        PaintMemo();
        QueueSave();
    }

    void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
