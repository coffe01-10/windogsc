namespace DogSC;

public partial class CountdownWindow : Window
{
    readonly MonitorDesc _monitor;

    public CountdownWindow(MonitorDesc monitor)
    {
        _monitor = monitor;
        InitializeComponent();
    }

    public static async Task RunAsync(MonitorDesc monitor)
    {
        var window = new CountdownWindow(monitor);
        window.Show();
        for (var value = 3; value >= 1; value--)
        {
            window.Number.Text = value.ToString();
            await Task.Delay(650);
        }

        window.Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowPlacement.Exclude(this);
        WindowPlacement.Fit(this, _monitor);
    }
}
