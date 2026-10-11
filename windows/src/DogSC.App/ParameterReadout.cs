using DogSC.Core;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace DogSC;

sealed class ParameterReadout : Border
{
    readonly TextBlock _label;
    readonly Button _button;
    readonly TextBox _input;
    readonly Func<double, string> _format;
    readonly double _min, _max;
    readonly bool _percent;
    double _value;
    bool _valid = true, _validationFailed;

    public event Action? EditingStarted;
    public event Action<double>? ValuePreviewed;
    public event Action<bool>? EditingFinished;
    public bool IsEditing { get; private set; }
    public string DraftText { get => _input.Text; set => _input.Text = value; }
    public double Value
    {
        get => _value;
        set { _value = value; _label.Text = _format(value); System.Windows.Automation.AutomationProperties.SetItemStatus(_button, _label.Text); }
    }

    public ParameterReadout(string title, double min, double max, double value, Func<double, string> format)
    {
        _min = min; _max = max; _format = format; _percent = format(1).Contains('%') || format(1).Contains('％');
        MinWidth = 56; Height = 30; CornerRadius = new CornerRadius(15); BorderThickness = new Thickness(.75);
        VerticalAlignment = VerticalAlignment.Center;
        _label = new TextBlock { FontSize = 13, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        _label.SetResourceReference(TextBlock.FontFamilyProperty, "FontMedium");
        _button = new Button { Content = _label, Padding = new Thickness(0), Margin = new Thickness(0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Style = (Style)Application.Current.FindResource("QuietButton") };
        FluidMotion.SetPressEnabled(_button, false);
        _input = new TextBox { Visibility = Visibility.Collapsed, FontSize = 13, FontWeight = FontWeights.Medium,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 0, 8, 0),
            TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Height = 28,
            FocusVisualStyle = null, AcceptsReturn = false };
        _input.SetResourceReference(Control.FontFamilyProperty, "FontMedium");
        var contentHost = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        contentHost.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 8, 0));
        contentHost.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        _input.Template = new ControlTemplate(typeof(TextBox)) { VisualTree = contentHost };
        Typography.SetNumeralAlignment(_label, FontNumeralAlignment.Tabular);
        Typography.SetNumeralAlignment(_input, FontNumeralAlignment.Tabular);
        var grid = new Grid(); grid.Children.Add(_button);
        var overlay = new Canvas { IsHitTestVisible = true }; overlay.Children.Add(_input); grid.Children.Add(overlay); Child = grid;
        grid.SizeChanged += (_, e) => _input.Width = Math.Max(0, e.NewSize.Width);
        System.Windows.Automation.AutomationProperties.SetName(_button, "编辑" + title);
        System.Windows.Automation.AutomationProperties.SetName(_input, "输入" + title);
        _button.ToolTip = "点击输入" + title;
        _button.Click += (_, _) => BeginEditing();
        _input.TextChanged += (_, _) =>
        {
            if (!IsEditing) return;
            _validationFailed = false;
            _valid = ParameterNumber.TryParse(_input.Text, _percent, _min, _max, out var parsed);
            if (_valid) { Value = parsed; ValuePreviewed?.Invoke(parsed); }
            Paint();
        };
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CancelEditing(); _button.Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter)
            {
                if (_valid) { FinishEditing(); _button.Focus(); }
                else { _validationFailed = true; Paint(); _input.Focus(); }
                e.Handled = true;
            }
        };
        _input.LostKeyboardFocus += (_, _) => { if (IsEditing) FinishEditing(); };
        _button.MouseEnter += (_, _) => Paint(); _button.MouseLeave += (_, _) => Paint();
        Unloaded += (_, _) => { if (IsEditing) CancelEditing(); };
        IsEnabledChanged += (_, _) => Paint(); Value = value; Paint();
    }

    public void BeginEditing()
    {
        if (IsEditing || !IsEnabled) return;
        EditingStarted?.Invoke();
        _input.Text = ParameterNumber.StripUnits(_format(Value)); _valid = true; _validationFailed = false;
        IsEditing = true; _label.Opacity = 0; _input.Visibility = Visibility.Visible; Paint();
        Dispatcher.BeginInvoke(() => { if (IsEditing) { _input.Focus(); _input.SelectAll(); } }, System.Windows.Threading.DispatcherPriority.Input);
    }

    public bool FinishEditing()
    {
        if (!IsEditing) return true;
        var commits = _valid; End(commits); return commits;
    }

    public void CancelEditing() { if (IsEditing) End(false); }

    void End(bool commits)
    {
        IsEditing = false; _input.Visibility = Visibility.Collapsed; _label.Opacity = 1; _validationFailed = false;
        Paint(); EditingFinished?.Invoke(commits);
    }

    void Paint()
    {
        var active = IsEditing || _button.IsMouseOver;
        SetResourceReference(BackgroundProperty, active ? "HoverBrush" : "ValueWellBrush");
        SetResourceReference(BorderBrushProperty, active ? "SelectionOutlineBrush" : "LineBrush");
        _label.SetResourceReference(TextBlock.ForegroundProperty, active ? "AccentBrush" : "InkSecondaryBrush");
        _input.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        if (_validationFailed) { BorderBrush = Brushes.IndianRed; _input.Foreground = Brushes.IndianRed; }
        Opacity = IsEnabled ? 1 : .4;
    }
}
