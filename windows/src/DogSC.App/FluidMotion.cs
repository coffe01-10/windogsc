using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DogSC;

// The original uses response 0.38 / damping 0.82 for panels, 0.22 / 0.72 for controls.
sealed class FluidSpring : EasingFunctionBase
{
    public double Response { get; init; } = .38;
    public double Damping { get; init; } = .82;
    public double Seconds { get; init; } = .46;
    protected override double EaseInCore(double t)
    {
        var omega = 2 * Math.PI / Response;
        var damped = omega * Math.Sqrt(1 - Damping * Damping);
        var time = t * Seconds;
        return 1 - Math.Exp(-Damping * omega * time)
            * (Math.Cos(damped * time) + Damping * omega / damped * Math.Sin(damped * time));
    }
    protected override Freezable CreateInstanceCore() => new FluidSpring { Response = Response, Damping = Damping, Seconds = Seconds };
}

public static class FluidMotion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;
    public static void Spring(Animatable target, DependencyProperty property, double value, bool interactive = false)
    {
        var from = (double)target.GetValue(property);
        target.BeginAnimation(property, null); target.SetValue(property, value);
        if (!Enabled || Math.Abs(from - value) < .001) return;
        var seconds = interactive ? .28 : .46;
        target.BeginAnimation(property, new DoubleAnimation(from, value, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = new FluidSpring { Response = interactive ? .22 : .38, Damping = interactive ? .72 : .82, Seconds = seconds, EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop,
        });
    }

    public static void Fade(UIElement element, double from, double to, int milliseconds, Action? completed = null)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = to;
        if (!Enabled) { completed?.Invoke(); return; }
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        if (completed is not null) animation.Completed += (_, _) => completed();
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    public static Border Selection(string brush = "SegmentSelectedBrush", double radius = 9)
    {
        var indicator = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(.75),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
        indicator.SetResourceReference(Border.BackgroundProperty, brush);
        indicator.SetResourceReference(Border.BorderBrushProperty, "GlassEdgeBrush");
        indicator.Child = new Border { CornerRadius = indicator.CornerRadius, Background = new LinearGradientBrush(
            Color.FromArgb(45, 255, 255, 255), Colors.Transparent, 90), IsHitTestVisible = false };
        return indicator;
    }

    public static void Select(Border indicator, FrameworkElement item, FrameworkElement container, bool animate = true)
    {
        if (item.ActualWidth == 0 || container.ActualWidth == 0) return;
        var point = item.TranslatePoint(new Point(), container);
        var transform = (TranslateTransform)indicator.RenderTransform;
        if (double.IsNaN(indicator.Width) || !animate || !Enabled)
        {
            indicator.Width = item.ActualWidth; indicator.Height = item.ActualHeight;
            transform.BeginAnimation(TranslateTransform.XProperty, null); transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.X = point.X; transform.Y = point.Y;
        }
        else
        {
            Spring(transform, TranslateTransform.XProperty, point.X);
            Spring(transform, TranslateTransform.YProperty, point.Y);
            // A small elastic stretch follows the travelling selection without moving the labels.
            if (indicator.Child is FrameworkElement glass)
            {
                var scale = glass.RenderTransform as ScaleTransform ?? new ScaleTransform();
                glass.RenderTransformOrigin = new Point(.5, .5); glass.RenderTransform = scale;
                scale.ScaleY = 1.04; Spring(scale, ScaleTransform.ScaleYProperty, 1);
            }
            indicator.Width = item.ActualWidth; indicator.Height = item.ActualHeight;
        }
    }

    public static Action PrepareSwap(FrameworkElement content, Panel layer, double displacement = 5)
    {
        layer.Children.Clear();
        content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1;
        content.RenderTransform = Transform.Identity;
        if (!Enabled || !content.IsLoaded || content.ActualWidth < 1 || content.ActualHeight < 1) return () => { };
        var dpi = VisualTreeHelper.GetDpi(content);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // Render in local coordinates: rendering the element directly also includes its
        // layout offset, which shifts a fading page when it has an outer margin.
        var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = bounds, Stretch = Stretch.Fill }, null, bounds);
        bitmap.Render(drawing); bitmap.Freeze();
        var previous = new Image { Source = bitmap, Width = content.ActualWidth, Height = content.ActualHeight,
            Stretch = Stretch.Fill, HorizontalAlignment = content.HorizontalAlignment, VerticalAlignment = content.VerticalAlignment, IsHitTestVisible = false };
        layer.Children.Add(previous);
        return () => content.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var offset = new TranslateTransform(0, displacement); content.RenderTransform = offset;
            Spring(offset, TranslateTransform.YProperty, 0);
            Fade(previous, 1, 0, 160, () => layer.Children.Remove(previous));
        });
    }

    public static void Entrance(FrameworkElement content)
    {
        if (!Enabled) return;
        var scale = new ScaleTransform(.985, .985); content.RenderTransformOrigin = new Point(.5, .5); content.RenderTransform = scale;
        Spring(scale, ScaleTransform.ScaleXProperty, 1); Spring(scale, ScaleTransform.ScaleYProperty, 1);
        Fade(content, .7, 1, 180);
    }

    public static readonly DependencyProperty PopupEnabledProperty = DependencyProperty.RegisterAttached("PopupEnabled", typeof(bool), typeof(FluidMotion), new PropertyMetadata(false, PopupChanged));
    public static bool GetPopupEnabled(DependencyObject value) => (bool)value.GetValue(PopupEnabledProperty);
    public static void SetPopupEnabled(DependencyObject value, bool enabled) => value.SetValue(PopupEnabledProperty, enabled);
    static void PopupChanged(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is ContextMenu menu && args.NewValue is true)
            menu.Opened += (_, _) => { menu.ApplyTemplate(); if (menu.Template.FindName("MenuSurface", menu) is FrameworkElement surface) Entrance(surface); };
    }

    public static readonly DependencyProperty SwitchEnabledProperty = DependencyProperty.RegisterAttached("SwitchEnabled", typeof(bool), typeof(FluidMotion), new PropertyMetadata(false, SwitchChanged));
    public static bool GetSwitchEnabled(DependencyObject value) => (bool)value.GetValue(SwitchEnabledProperty);
    public static void SetSwitchEnabled(DependencyObject value, bool enabled) => value.SetValue(SwitchEnabledProperty, enabled);
    static void SwitchChanged(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not CheckBox toggle || args.NewValue is not true) return;
        toggle.Loaded += (_, _) => Update(false); toggle.Checked += (_, _) => Update(true); toggle.Unchecked += (_, _) => Update(true);
        void Update(bool animate)
        {
            toggle.ApplyTemplate();
            if (toggle.Template.FindName("Knob", toggle) is not Border knob) return;
            var offset = knob.RenderTransform as TranslateTransform ?? new TranslateTransform(); knob.RenderTransform = offset;
            if (animate) Spring(offset, TranslateTransform.XProperty, toggle.IsChecked == true ? 12 : 0, true);
            else offset.X = toggle.IsChecked == true ? 12 : 0;
        }
    }

    public static readonly DependencyProperty PressEnabledProperty = DependencyProperty.RegisterAttached("PressEnabled", typeof(bool), typeof(FluidMotion), new PropertyMetadata(false, PressChanged));
    public static bool GetPressEnabled(DependencyObject value) => (bool)value.GetValue(PressEnabledProperty);
    public static void SetPressEnabled(DependencyObject value, bool enabled) => value.SetValue(PressEnabledProperty, enabled);
    static void PressChanged(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not ButtonBase button || args.NewValue is not true) return;
        button.RenderTransformOrigin = new Point(.5, .5); var scale = new ScaleTransform(); button.RenderTransform = scale;
        button.PreviewMouseLeftButtonDown += (_, _) => { Spring(scale, ScaleTransform.ScaleXProperty, .965, true); Spring(scale, ScaleTransform.ScaleYProperty, .965, true); };
        button.PreviewMouseLeftButtonUp += (_, _) => Restore(); button.MouseLeave += (_, _) => Restore();
        void Restore() { Spring(scale, ScaleTransform.ScaleXProperty, 1, true); Spring(scale, ScaleTransform.ScaleYProperty, 1, true); }
    }

    public static readonly DependencyProperty DisclosureEnabledProperty = DependencyProperty.RegisterAttached("DisclosureEnabled", typeof(bool), typeof(FluidMotion), new PropertyMetadata(false, DisclosureChanged));
    public static bool GetDisclosureEnabled(DependencyObject value) => (bool)value.GetValue(DisclosureEnabledProperty);
    public static void SetDisclosureEnabled(DependencyObject value, bool enabled) => value.SetValue(DisclosureEnabledProperty, enabled);
    static void DisclosureChanged(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not Expander expander || args.NewValue is not true) return;
        expander.Loaded += (_, _) => Layout(false);
        expander.Expanded += (_, _) => Layout(true); expander.Collapsed += (_, _) => Layout(true);
        void Layout(bool animate)
        {
            expander.ApplyTemplate();
            if (expander.Template.FindName("DisclosureBody", expander) is not Border body
                || expander.Template.FindName("DisclosureContent", expander) is not ContentPresenter presenter) return;
            presenter.Measure(new Size(Math.Max(1, expander.ActualWidth - 22), double.PositiveInfinity));
            var height = expander.IsExpanded ? presenter.DesiredSize.Height : 0;
            var from = body.ActualHeight; body.BeginAnimation(FrameworkElement.HeightProperty, null);
            body.Height = expander.IsExpanded ? double.NaN : 0;
            body.IsHitTestVisible = expander.IsExpanded;
            if (animate && Enabled)
            {
                var animation = new DoubleAnimation(from, height, TimeSpan.FromSeconds(.38))
                { EasingFunction = new FluidSpring { Seconds = .38, EasingMode = EasingMode.EaseIn }, FillBehavior = FillBehavior.Stop };
                body.BeginAnimation(FrameworkElement.HeightProperty, animation);
                Fade(body, body.Opacity, expander.IsExpanded ? 1 : 0, 160);
            }
            else body.Opacity = expander.IsExpanded ? 1 : 0;
            if (expander.Template.FindName("DisclosureArrow", expander) is System.Windows.Shapes.Path arrow)
            {
                var rotation = (RotateTransform)arrow.RenderTransform;
                // Transforms from a WPF template can be shared and frozen.
                if (rotation.IsFrozen) { rotation = rotation.Clone(); arrow.RenderTransform = rotation; }
                if (animate) Spring(rotation, RotateTransform.AngleProperty, expander.IsExpanded ? 90 : 0, true);
                else rotation.Angle = expander.IsExpanded ? 90 : 0;
            }
        }
    }
}

sealed class FluidSegments : Grid
{
    readonly UniformGrid _buttons = new() { Rows = 1 };
    readonly Border _selection = FluidMotion.Selection();
    int _selected;
    public IEnumerable<Button> Buttons => _buttons.Children.OfType<Button>();
    public FluidSegments(IReadOnlyList<Button> buttons, int selected, int previous = -1)
    {
        _selected = selected; ClipToBounds = false;
        Children.Add(_selection); Children.Add(_buttons);
        foreach (var button in buttons)
        {
            button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0);
            _buttons.Children.Add(button);
        }
        Loaded += (_, _) =>
        {
            if (previous >= 0 && previous < _buttons.Children.Count && previous != selected)
                FluidMotion.Select(_selection, (FrameworkElement)_buttons.Children[previous], this, false);
            Select(_selected, previous >= 0);
        };
        SizeChanged += (_, _) => { if (IsLoaded) Position(false); };
    }
    public void Select(int selected, bool animate = true)
    {
        _selected = selected;
        for (var i = 0; i < _buttons.Children.Count; i++)
            ((Button)_buttons.Children[i]).SetResourceReference(Control.ForegroundProperty, i == selected ? "InkBrush" : "InkSecondaryBrush");
        Position(animate);
    }
    void Position(bool animate)
    {
        if (_selected >= 0 && _selected < _buttons.Children.Count)
            FluidMotion.Select(_selection, (FrameworkElement)_buttons.Children[_selected], this, animate);
    }
}
