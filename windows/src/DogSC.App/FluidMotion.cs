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
    public bool LimitOvershoot { get; init; }
    protected override double EaseInCore(double t)
    {
        if (t >= 1) return 1;
        var omega = 2 * Math.PI / Response;
        var damped = omega * Math.Sqrt(1 - Damping * Damping);
        var time = t * Seconds;
        var value = 1 - Math.Exp(-Damping * omega * time)
            * (Math.Cos(damped * time) + Damping * omega / damped * Math.Sin(damped * time));
        // Layout height cannot go below zero during a collapsing spring.
        return LimitOvershoot ? Math.Min(1, value) : value;
    }
    protected override Freezable CreateInstanceCore() => new FluidSpring { Response = Response, Damping = Damping, Seconds = Seconds, LimitOvershoot = LimitOvershoot };
}

public static class FluidMotion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;
    public static void Spring(Animatable target, DependencyProperty property, double value, bool interactive = false)
    {
        var from = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        if (!Enabled || Math.Abs(from - value) < .001) { target.SetValue(property, value); return; }
        // WPF can leave a new clock inactive until the next render tick. Keep
        // the current value as the base so that gap does not expose the endpoint.
        target.SetValue(property, from);
        var seconds = interactive ? .28 : .46;
        target.BeginAnimation(property, new DoubleAnimation(from, value, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = new FluidSpring { Response = interactive ? .22 : .38, Damping = interactive ? .72 : .82, Seconds = seconds, EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    public static void Fade(UIElement element, double from, double to, int milliseconds, Action? completed = null)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (!Enabled) { element.Opacity = to; completed?.Invoke(); return; }
        element.Opacity = from;
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd };
        if (completed is not null) animation.Completed += (_, _) => completed();
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    public static Border Selection(string brush = "SegmentSelectedBrush", double radius = 8)
    {
        var indicator = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(.75),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
        indicator.SetResourceReference(Border.BackgroundProperty, brush);
        indicator.SetResourceReference(Border.BorderBrushProperty, "GlassEdgeBrush");
        return indicator;
    }

    public static void Select(Border indicator, FrameworkElement item, FrameworkElement container, bool animate = true)
    {
        if (item.ActualWidth == 0 || container.ActualWidth == 0) return;
        // Press feedback scales the label/button, not the pill's layout position.
        var parent = (Visual)VisualTreeHelper.GetParent(item);
        var point = parent.TransformToVisual(container).Transform((Point)VisualTreeHelper.GetOffset(item));
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
            indicator.Width = item.ActualWidth; indicator.Height = item.ActualHeight;
        }
    }

    public static Action PrepareSwap(FrameworkElement content, Panel layer, double displacement = 10)
    {
        if (!Enabled || !content.IsLoaded || content.ActualWidth < 1 || content.ActualHeight < 1)
        {
            layer.Children.Clear();
            content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1;
            content.RenderTransform = Transform.Identity;
            return () => { };
        }
        var dpi = VisualTreeHelper.GetDpi(content);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // Render in local coordinates: rendering the element directly also includes its
        // layout offset, which shifts a fading page when it has an outer margin.
        var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
        var visualOffset = VisualTreeHelper.GetOffset(content);
        // Capture the currently presented combination before clearing any clocks
        // or the outgoing page. Rapid clicks must start from that visible frame.
        var currentOpacity = content.Opacity;
        var currentTransform = content.RenderTransform.CloneCurrentValue();
        content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1;
        content.RenderTransform = Transform.Identity;
        var drawing = new DrawingVisual();
        // The live selector continues travelling; don't leave a second, stationary
        // selection inside the outgoing page's image.
        var segments = DescendantSegments(content).ToArray();
        foreach (var segment in segments) segment.SetIndicatorVisible(false);
        try
        {
            using (var context = drawing.RenderOpen())
            {
                context.PushOpacity(currentOpacity);
                context.PushTransform(currentTransform);
                context.DrawRectangle(new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(visualOffset.X, visualOffset.Y, bounds.Width, bounds.Height), Stretch = Stretch.Fill }, null, bounds);
                context.Pop(); context.Pop();
                if (layer.Children.Count > 0)
                    context.DrawRectangle(new VisualBrush(layer) { ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect(visualOffset.X, visualOffset.Y, bounds.Width, bounds.Height), Stretch = Stretch.Fill }, null, bounds);
            }
            bitmap.Render(drawing);
        }
        finally { foreach (var segment in segments) segment.SetIndicatorVisible(true); }
        bitmap.Freeze();
        layer.Children.Clear();
        content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1;
        content.RenderTransform = Transform.Identity;
        var previous = new Image { Source = bitmap, Width = content.ActualWidth, Height = content.ActualHeight,
            Stretch = Stretch.Fill, HorizontalAlignment = content.HorizontalAlignment, VerticalAlignment = content.VerticalAlignment, IsHitTestVisible = false };
        layer.Children.Add(previous);
        return () => content.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            // A later click may already have replaced this transition.
            if (!layer.Children.Contains(previous)) return;
            var offset = new TranslateTransform(0, displacement); content.RenderTransform = offset;
            Spring(offset, TranslateTransform.YProperty, 0);
            var outgoing = new TranslateTransform(); previous.RenderTransform = outgoing;
            if (displacement != 0) Spring(outgoing, TranslateTransform.YProperty, -5);
            Fade(content, displacement == 0 ? 1 : 0, 1, 160);
            Fade(previous, 1, 0, 160, () => layer.Children.Remove(previous));
        });
    }

    static IEnumerable<FluidSegments> DescendantSegments(DependencyObject parent)
    {
        if (parent is FluidSegments segment) yield return segment;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in DescendantSegments(VisualTreeHelper.GetChild(parent, i))) yield return child;
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
        button.PreviewMouseLeftButtonDown += (_, _) => { Spring(scale, ScaleTransform.ScaleXProperty, .98, true); Spring(scale, ScaleTransform.ScaleYProperty, .98, true); };
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
            presenter.Measure(new Size(Math.Max(1, expander.ActualWidth - 2), double.PositiveInfinity));
            var height = expander.IsExpanded ? presenter.DesiredSize.Height + 1 : 0;
            var from = body.ActualHeight; body.BeginAnimation(FrameworkElement.HeightProperty, null);
            body.IsHitTestVisible = expander.IsExpanded;
            if (animate && Enabled)
            {
                // Keep the measured current height until WPF starts the clock.
                // Auto height here used to expose the whole body for one frame.
                body.Height = from;
                var animation = new DoubleAnimation(from, height, TimeSpan.FromSeconds(.38))
                { EasingFunction = new FluidSpring { Seconds = .38, LimitOvershoot = true, EasingMode = EasingMode.EaseIn }, FillBehavior = FillBehavior.HoldEnd };
                animation.Completed += (_, _) =>
                {
                    body.BeginAnimation(FrameworkElement.HeightProperty, null);
                    body.Height = expander.IsExpanded ? double.NaN : 0;
                };
                body.BeginAnimation(FrameworkElement.HeightProperty, animation);
                Fade(body, body.Opacity, expander.IsExpanded ? 1 : 0, 160);
                var scale = presenter.RenderTransform as ScaleTransform ?? new ScaleTransform(.985, .985);
                presenter.RenderTransformOrigin = new Point(.5, 0); presenter.RenderTransform = scale;
                Spring(scale, ScaleTransform.ScaleXProperty, expander.IsExpanded ? 1 : .985);
                Spring(scale, ScaleTransform.ScaleYProperty, expander.IsExpanded ? 1 : .985);
            }
            else { body.Height = expander.IsExpanded ? double.NaN : 0; body.Opacity = expander.IsExpanded ? 1 : 0; }
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
    readonly Border _selection;
    readonly UniformGrid? _tileWells;
    int _selected;
    bool _positioned;
    public IEnumerable<Button> Buttons => _buttons.Children.OfType<Button>();
    public FluidSegments(IReadOnlyList<Button> buttons, int selected, bool individualTiles = false)
    {
        _selected = selected; ClipToBounds = false;
        _selection = FluidMotion.Selection(radius: individualTiles ? 10 : 8);
        if (individualTiles)
        {
            _tileWells = new UniformGrid { Rows = 1, IsHitTestVisible = false };
            Children.Add(_tileWells);
            _selection.SetResourceReference(Border.BorderBrushProperty, "ChoiceSelectedBorderBrush");
        }
        Children.Add(_selection); Children.Add(_buttons);
        Refresh(buttons, selected);
        Loaded += (_, _) =>
        {
            Select(_selected, _positioned);
            _positioned = true;
        };
        // Reparenting / font layout can report a size change mid-flight. Keep
        // travelling from the current clock instead of cancelling it at the target.
        SizeChanged += (_, _) => { if (IsLoaded) Position(_positioned); };
    }
    public void Refresh(IReadOnlyList<Button> buttons, int selected)
    {
        _buttons.Children.Clear();
        _tileWells?.Children.Clear();
        foreach (var button in buttons)
        {
            if (_tileWells is not null)
            {
                var well = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(.75), Margin = button.Margin };
                well.SetResourceReference(Border.BackgroundProperty, "SegmentWellBrush");
                well.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
                _tileWells.Children.Add(well);
            }
            button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0);
            _buttons.Children.Add(button);
        }
        Select(selected, _positioned);
        // Newly replaced buttons have no measured bounds yet. Reparenting in the
        // same dispatcher turn need not raise Loaded again, so finish after layout.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Position(_positioned));
    }
    public void SetIndicatorVisible(bool visible) => _selection.Opacity = visible ? 1 : 0;
    public void Select(int selected, bool animate = true)
    {
        _selected = selected;
        for (var i = 0; i < _buttons.Children.Count; i++)
        {
            ((Button)_buttons.Children[i]).SetResourceReference(Control.ForegroundProperty, i == selected ? "InkBrush" : "InkSecondaryBrush");
            ((Button)_buttons.Children[i]).SetResourceReference(Control.FontFamilyProperty, i == selected ? "FontMedium" : "FontRegular");
            ((Button)_buttons.Children[i]).FontWeight = i == selected ? FontWeights.Medium : FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetItemStatus(_buttons.Children[i], i == selected ? "已选中" : "");
        }
        Position(animate);
    }
    void Position(bool animate)
    {
        if (_selected >= 0 && _selected < _buttons.Children.Count)
            FluidMotion.Select(_selection, (FrameworkElement)_buttons.Children[_selected], this, animate);
    }
}
