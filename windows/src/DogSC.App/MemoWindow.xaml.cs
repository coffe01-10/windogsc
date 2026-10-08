namespace DogSC;

public partial class MemoWindow : Window
{
    static MemoWindow? _current;
    bool _ready;

    public MemoWindow()
    {
        InitializeComponent();
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
        SettingsStore.Current.MemoLeft = Left;
        SettingsStore.Current.MemoTop = Top;
        SettingsStore.Save();
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
    }

    void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // The pointer already left the window.
            }
        }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Body_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        SettingsStore.Current.MemoText = Body.Text;
        SettingsStore.Save();
    }

    void Size_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready)
        {
            return;
        }

        Body.FontSize = e.NewValue;
        SettingsStore.Current.MemoSize = e.NewValue;
        SettingsStore.Save();
    }

    void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || Shell is null)
        {
            return;
        }

        Shell.Opacity = e.NewValue;
        SettingsStore.Current.MemoOpacity = e.NewValue;
        SettingsStore.Save();
    }
}
