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
        Shell.Opacity = OpacitySlider.Value;
        if (!double.IsNaN(SettingsStore.Current.MemoLeft))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SettingsStore.Current.MemoLeft;
            Top = SettingsStore.Current.MemoTop;
        }

        _ready = true;
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
        QueueSave();
    }

    void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || Shell is null)
        {
            return;
        }

        Shell.Opacity = e.NewValue;
        SettingsStore.Current.MemoOpacity = e.NewValue;
        QueueSave();
    }

    void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
