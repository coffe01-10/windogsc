namespace DogSC;

sealed class LayoutPresetSavePanel : Border
{
    public event Action? CancelRequested, Saved;
    public LayoutPresetSavePanel(Func<string, bool> exists, Func<string, string?> save)
    {
        Width = 400; Padding = new Thickness(24); CornerRadius = new CornerRadius(24); BorderThickness = new Thickness(.75);
        SetResourceReference(BackgroundProperty, "PanelBrush"); SetResourceReference(BorderBrushProperty, "LineBrush");
        var body = new StackPanel(); Child = body;
        var title = new TextBlock { Text = L.Text("保存摄像头布局预设"), FontSize = 18, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "FontSemi"); body.Children.Add(title);
        var detail = new TextBlock { Text = L.Text("保存摄像头形状、位置、大小与圆角，以及屏幕位置和大小。应用时在播放头创建联动动画。"),
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 16) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "InkSecondaryBrush"); body.Children.Add(detail);
        body.Children.Add(new TextBlock { Text = L.Text("预设名称"), FontSize = 12 });
        var name = new TextBox { FontSize = 13, Margin = new Thickness(0, 7, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(name, L.Text("布局预设名称")); body.Children.Add(name);
        var hint = new TextBlock { Text = L.Text("同名预设将被替换。"), FontSize = 11, Margin = new Thickness(0, 7, 0, 0), Visibility = Visibility.Collapsed };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "InkSecondaryBrush"); body.Children.Add(hint);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        error.SetResourceReference(TextBlock.ForegroundProperty, "RecordBrush"); body.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = L.Text("取消"), Style = (Style)FindResource("QuietButton"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => CancelRequested?.Invoke(); actions.Children.Add(cancel);
        var commit = new Button { Content = L.Text("保存预设"), Style = (Style)FindResource("PrimaryButton"), IsDefault = true, IsEnabled = false };
        void Commit()
        {
            if (!commit.IsEnabled) return;
            var message = save(name.Text.Trim());
            if (message is null) Saved?.Invoke();
            else { error.Text = message; error.Visibility = Visibility.Visible; }
        }
        commit.Click += (_, _) => Commit(); actions.Children.Add(commit); body.Children.Add(actions);
        name.TextChanged += (_, _) =>
        {
            commit.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
            hint.Visibility = exists(name.Text.Trim()) ? Visibility.Visible : Visibility.Collapsed;
        };
        Loaded += (_, _) => name.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Commit(); }
            else if (e.Key == Key.Escape) { e.Handled = true; CancelRequested?.Invoke(); }
        };
    }
}
