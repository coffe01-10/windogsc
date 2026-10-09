using System.Windows.Media.Animation;

namespace DogSC;

static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static void To(DependencyObject target, DependencyProperty property, double from, double to, int milliseconds = 180)
    {
        var animatable = (IAnimatable)target;
        animatable.BeginAnimation(property, null);
        target.SetValue(property, to);
        if (!Enabled || milliseconds == 0 || Math.Abs(from - to) < 0.01) return;
        animatable.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        }, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Reveal(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (!Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
        var offset = new TranslateTransform();
        element.RenderTransform = offset;
        To(offset, TranslateTransform.YProperty, 5, 0, 160);
    }
}
