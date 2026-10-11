using DogSC.Core;
using System.Windows.Controls.Primitives;

namespace DogSC;

public partial class EditorWindow
{
    UIElement ClickEffectSegments(IEnumerable<(string Title, string Key)> choices, string selected, Action<string> choose)
    {
        var items = choices.ToArray(); var buttons = new List<Button>();
        var groupKey = string.Join("|", items.Select(x => x.Key)); var selectedIndex = Array.FindIndex(items, x => x.Key == selected);
        foreach (var (title, key) in items)
        {
            var tone = (SolidColorBrush)FindResource(selected == key ? "InkBrush" : "InkSecondaryBrush");
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            if (key is "none" or "flash")
                content.Children.Add(new Icon { Kind = key == "none" ? IconKind.Ban : IconKind.Sparkles, Width = 20, Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Center, Foreground = tone });
            else
            {
                var color = $"#{tone.Color.R:X2}{tone.Color.G:X2}{tone.Color.B:X2}";
                var glyph = CursorCatalog.RenderClick(28, key, .34, CursorCatalog.ParseColor(color), 1, 10);
                content.Children.Add(new Image { Width = 28, Height = 20, Source = BitmapSource.Create(28, 28, 96, 96, PixelFormats.Bgra32, null, glyph, 28 * 4) });
            }
            content.Children.Add(new TextBlock { Text = title, FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            var button = new Button { Content = content, Height = 50, Style = (Style)FindResource("SegmentButton"), Padding = new Thickness(3) };
            System.Windows.Automation.AutomationProperties.SetName(button, L.Text("点击动画：") + title);
            ControlAppearance.SelectSegment(button, selected == key); button.Click += (_, _) => choose(key); buttons.Add(button);
        }
        var row = ReuseSegments(groupKey, buttons, selectedIndex);
        return new Border { Child = row, CornerRadius = new CornerRadius(12), Padding = new Thickness(4), Background = (Brush)FindResource("SegmentWellBrush"),
            BorderThickness = new Thickness(.75), BorderBrush = (Brush)FindResource("LineBrush") };
    }

    TextBlock SectionTitle(string text) => new()
    {
        Text = text, FontSize = 14, FontFamily = (FontFamily)FindResource("FontSemi"), FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 22, 0, 8),
    };

    StackPanel CoordinateValue(string axis, ParameterReadout value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock { Text = axis, FontSize = 11, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(value); return row;
    }

    DockPanel PadHeading(string title, string action, Action reset, IconKind icon = IconKind.Scope)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 2) };
        var button = DecorationButton(action, reset); button.Style = (Style)FindResource("PadGhostButton");
        button.Content = IconLabel(icon, action);
        System.Windows.Automation.AutomationProperties.SetName(button, action + (title.Length > 0 ? title : ""));
        button.Margin = new Thickness(0);
        DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
        var caption = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        caption.SetResourceReference(TextBlock.FontFamilyProperty, "FontSemi"); caption.SetResourceReference(TextBlock.ForegroundProperty, "PadHeadingBrush");
        row.Children.Add(caption);
        return row;
    }

    UIElement InlineValueSlider(string title, double min, double max, double value, Action<double> set, Func<double, string>? format = null)
    {
        format ??= v => v.ToString("0.##");
        var row = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        Slider? slider = null; var synchronizing = false;
        var pill = Parameter(title, min, max, value, v => { set(v); SyncParameterSlider(slider!, v, ref synchronizing); }, format);
        pill.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(pill, 2); row.Children.Add(pill);
        slider = Slider(min, max, value, v => { set(v); pill.Value = v; }, suppress: () => synchronizing);
        slider.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(slider, 1); row.Children.Add(slider);
        System.Windows.Automation.AutomationProperties.SetName(slider, title); return row;
    }
}
