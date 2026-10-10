using DogSC.Core;
using System.Windows.Controls.Primitives;

namespace DogSC;

public partial class EditorWindow
{
    UIElement ClickEffectSegments(IEnumerable<(string Title, string Key)> choices, string selected, Action<string> choose)
    {
        var items = choices.ToArray(); var buttons = new List<Button>();
        var groupKey = string.Join("|", items.Select(x => x.Key)); var selectedIndex = Array.FindIndex(items, x => x.Key == selected);
        var previous = _segmentSelections.GetValueOrDefault(groupKey, selectedIndex); _segmentSelections[groupKey] = selectedIndex;
        foreach (var (title, key) in items)
        {
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            if (key is "none" or "flash")
                content.Children.Add(new Icon { Kind = key == "none" ? IconKind.Ban : IconKind.Sparkles, Width = 20, Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Center, Foreground = selected == key ? Brushes.DimGray : Brushes.DarkGray });
            else
            {
                var glyph = CursorCatalog.RenderClick(28, key, .34, CursorCatalog.ParseColor(selected == key ? "#444444" : "#999999"), 1, 10);
                content.Children.Add(new Image { Width = 28, Height = 20, Source = BitmapSource.Create(28, 28, 96, 96, PixelFormats.Bgra32, null, glyph, 28 * 4) });
            }
            content.Children.Add(new TextBlock { Text = title, FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            var button = new Button { Content = content, Height = 50, Style = (Style)FindResource("SegmentButton"), Padding = new Thickness(3) };
            System.Windows.Automation.AutomationProperties.SetName(button, "点击动画：" + title);
            ControlAppearance.SelectSegment(button, selected == key); button.Click += (_, _) => choose(key); buttons.Add(button);
        }
        var row = new FluidSegments(buttons, selectedIndex, previous);
        return new Border { Child = row, CornerRadius = new CornerRadius(12), Padding = new Thickness(4), Background = (Brush)FindResource("SegmentWellBrush"),
            BorderThickness = new Thickness(.75), BorderBrush = (Brush)FindResource("LineBrush") };
    }

    TextBlock SectionTitle(string text) => new()
    {
        Text = text, FontSize = 14, FontFamily = (FontFamily)FindResource("FontSemi"),
        Margin = new Thickness(0, 22, 0, 8),
    };

    Border ValuePill(TextBlock value)
    {
        value.FontSize = 12; value.TextAlignment = TextAlignment.Center;
        value.FontFamily = (FontFamily)FindResource("FontMedium");
        var pill = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(10, 5, 10, 5),
            MinWidth = 46, Child = value, VerticalAlignment = VerticalAlignment.Center };
        pill.SetResourceReference(Border.BackgroundProperty, "ValueWellBrush"); return pill;
    }

    StackPanel CoordinateValue(string axis, TextBlock value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock { Text = axis, FontSize = 11, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(ValuePill(value)); return row;
    }

    DockPanel PadHeading(string title, string action, Action reset)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 2) };
        var button = DecorationButton(action, reset); button.Style = (Style)FindResource("QuietButton");
        button.FontSize = 11; button.Padding = new Thickness(6, 3, 0, 3); button.Margin = new Thickness(0);
        DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
        row.Children.Add(new TextBlock { Text = title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
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
        var text = new TextBlock { Text = format(value) }; var pill = ValuePill(text); pill.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(pill, 2); row.Children.Add(pill);
        var slider = Slider(min, max, value, v => { set(v); text.Text = format(v); });
        slider.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(slider, 1); row.Children.Add(slider);
        System.Windows.Automation.AutomationProperties.SetName(slider, title); return row;
    }
}
