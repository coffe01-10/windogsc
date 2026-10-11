namespace DogSC;

// Matches EditorActionMenu.swift's titled cards and trailing selection mark.
static class EditorActionMenus
{
    public static ContextMenu Create(string title)
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("EditorActionMenu") };
        var heading = new TextBlock { Text = title, FontSize = 13 };
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "FontSemi"); heading.FontWeight = FontWeights.SemiBold;
        menu.Items.Add(new MenuItem { Header = heading, Style = (Style)Application.Current.FindResource("EditorMenuTitle"), IsEnabled = false, Focusable = false });
        return menu;
    }

    public static MenuItem Add(ContextMenu menu, string title, Action action, IconKind? symbol = null, string? detail = null, string? key = null)
    {
        var item = new MenuItem { Style = (Style)Application.Current.FindResource("EditorActionRow"), IsCheckable = key is not null, Tag = key, MinHeight = detail is null ? 36 : 52 };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (symbol is { } kind)
        {
            var icon = new Icon { Kind = kind, Width = 16, Height = 16, Margin = new Thickness(3, 0, 15, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(icon);
        }
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = title, FontSize = 13 });
        if (detail is not null)
        {
            var caption = new TextBlock { Text = detail, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "InkSecondaryBrush"); labels.Children.Add(caption);
        }
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        var check = new Icon { Kind = IconKind.Checkmark, Width = 12, Height = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        // Opacity preserves the check's column even for unselected rows.
        item.Checked += (_, _) => check.Opacity = 1;
        item.Unchecked += (_, _) => check.Opacity = 0;
        check.Opacity = 0; Grid.SetColumn(check, 2); row.Children.Add(check);
        item.Header = row;
        System.Windows.Automation.AutomationProperties.SetName(item, title);
        item.ToolTip = detail ?? title;
        item.Click += (_, _) => action(); menu.Items.Add(item);
        return item;
    }
}
