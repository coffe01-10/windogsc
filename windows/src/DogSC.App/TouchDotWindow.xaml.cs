using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DogSC;

/// <summary>录制时的系统级触点圆点（对齐 Mac 的触点指示）：跟随光标，按下时脉冲一圈。</summary>
public partial class TouchDotWindow : Window
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    bool _wasDown;

    public TouchDotWindow()
    {
        InitializeComponent();
        _timer.Tick += Tick;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetClickThrough(hwnd);
        Native.ExcludeFromCapture(hwnd);
        WindowPlacement.Exclude(this);
        _timer.Start();
    }

    void Tick(object? sender, EventArgs e)
    {
        var (x, y) = Native.CursorPos();
        Left = x - Width / 2;
        Top = y - Height / 2;
        var down = Native.LeftButtonDown();
        if (down && !_wasDown)
        {
            Pulse.BeginAnimation(UIElement.OpacityProperty, null);
            PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            Pulse.Opacity = 0.9;
            PulseScale.ScaleX = PulseScale.ScaleY = 0.6;
            var scale = new DoubleAnimation(1.5, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(280));
            PulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            PulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.5, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            Pulse.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        _wasDown = down;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
