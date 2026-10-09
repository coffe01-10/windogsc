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
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        }, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Freeze(DependencyObject target, DependencyProperty property)
    {
        var current = target.GetValue(property);
        ((IAnimatable)target).BeginAnimation(property, null);
        target.SetValue(property, current);
    }

    public static void Reveal(FrameworkElement element, int milliseconds = 160, int delay = 0, double x = 0, double y = 5)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (!Enabled) return;
        var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + milliseconds)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        var offset = new TranslateTransform();
        element.RenderTransform = offset;
        Slide(TranslateTransform.XProperty, x);
        Slide(TranslateTransform.YProperty, y);

        void Slide(DependencyProperty property, double from)
        {
            if (from == 0) return;
            offset.BeginAnimation(property, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(milliseconds))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
        }
    }
}
