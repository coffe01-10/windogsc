using DogSC.Core;

namespace DogSC;

sealed class ScenePresetMenuPanel : Border
{
    readonly Func<IReadOnlyList<ScenePreset>> _presets;
    readonly Func<string?> _active, _default;
    readonly StackPanel _body = new();
    public event Action<ScenePreset?>? SaveRequested;
    public event Action<ScenePreset>? ApplyRequested, DeleteRequested;
    public event Action<string?>? DefaultRequested;

    public ScenePresetMenuPanel(Func<IReadOnlyList<ScenePreset>> presets, Func<string?> active, Func<string?> defaultId)
    {
        _presets = presets; _active = active; _default = defaultId;
        Width = 320; Padding = new Thickness(18); CornerRadius = new CornerRadius(16); BorderThickness = new Thickness(.75);
        SetResourceReference(BackgroundProperty, "CardBrush"); SetResourceReference(BorderBrushProperty, "ControlBorderBrush");
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = .16 };
        Child = _body; Refresh();
    }

    public void Refresh()
    {
        _body.Children.Clear();
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var add = Button(L.Text("另存为新预设"), IconKind.Plus); add.Width = add.Height = 30; add.Padding = new Thickness(7);
        Raised(add); add.Click += (_, _) => SaveRequested?.Invoke(null); DockPanel.SetDock(add, Dock.Right); heading.Children.Add(add);
        var title = new TextBlock { Text = L.Text("场景预设"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "FontSemi"); title.FontWeight = FontWeights.SemiBold; heading.Children.Add(title); _body.Children.Add(heading);
        var rows = new StackPanel();
        foreach (var preset in _presets())
        {
            var selected = preset.Id == _active();
            var content = new StackPanel();
            var choose = Button(preset.Name, IconKind.SceneStack); choose.Tag = "apply:" + preset.Id; choose.Padding = new Thickness(12); choose.MinHeight = 40;
            var label = (StackPanel)choose.Content;
            choose.Content = null;
            var line = new Grid(); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(label);
            if (selected) { var mark = new Icon { Kind = IconKind.Checkmark, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }; Grid.SetColumn(mark, 1); line.Children.Add(mark); }
            choose.Content = line; choose.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            choose.Click += (_, _) => ApplyRequested?.Invoke(preset); content.Children.Add(choose);
            var actions = new DockPanel { Margin = new Thickness(8, 0, 8, 8) };
            var remove = Button(L.Text("删除预设：") + preset.Name, IconKind.Trash, false); remove.Tag = "delete:" + preset.Id; remove.Width = 26; remove.Height = 24; remove.Padding = new Thickness(5);
            remove.Click += (_, _) => DeleteRequested?.Invoke(preset); DockPanel.SetDock(remove, Dock.Right); actions.Children.Add(remove);
            var isDefault = preset.Id == _default();
            var toggle = Button(isDefault ? L.Text("新录制默认") : L.Text("设为新录制默认"), isDefault ? IconKind.CheckCircle : IconKind.Circle); toggle.Tag = "default:" + preset.Id;
            toggle.Height = 26; toggle.FontSize = 11; toggle.Padding = new Thickness(4, 0, 4, 0); toggle.HorizontalAlignment = HorizontalAlignment.Left;
            toggle.Click += (_, _) => { DefaultRequested?.Invoke(_default() == preset.Id ? null : preset.Id); Refresh(); }; actions.Children.Add(toggle); content.Children.Add(actions);
            var well = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(.75), Child = content, Margin = new Thickness(0, 0, 0, 8) };
            well.SetResourceReference(BackgroundProperty, selected ? "PopoverSelectionBrush" : "SegmentWellBrush"); well.SetResourceReference(BorderBrushProperty, selected ? "ChoiceSelectedBorderBrush" : "LineBrush"); rows.Children.Add(well);
        }
        _body.Children.Add(new ScrollViewer { Content = rows, Height = Math.Min(_presets().Count * 84, 320), VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        if (_presets().FirstOrDefault(preset => preset.Id == _active()) is { } active)
        {
            var update = Button(L.Text("更新此预设…"), IconKind.Refresh); update.Tag = "update:" + active.Id; update.Height = 36; update.Margin = new Thickness(0, 12, 0, 0); Raised(update);
            update.Click += (_, _) => SaveRequested?.Invoke(active); _body.Children.Add(update);
        }
        var help = new TextBlock { Text = _default() is null ? L.Text("新录制沿用上次外观") : L.Text("新录制将应用已选默认场景"), FontSize = 11, Margin = new Thickness(0, 12, 0, 0) };
        help.SetResourceReference(TextBlock.ForegroundProperty, "InkSecondaryBrush"); _body.Children.Add(help);
    }

    internal static Button Button(string title, IconKind kind, bool showTitle = true)
    {
        var button = new Button { Style = (Style)Application.Current.FindResource("QuietButton"), FontSize = 13, Padding = new Thickness(8, 4, 8, 4) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon { Kind = kind, Width = showTitle ? 14 : 16, Height = showTitle ? 14 : 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, showTitle ? 6 : 0, 0) });
        if (showTitle)
        {
            var text = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 202, VerticalAlignment = VerticalAlignment.Center };
            text.SetBinding(TextBlock.FontFamilyProperty, new System.Windows.Data.Binding(nameof(Control.FontFamily)) { Source = button });
            text.SetBinding(TextBlock.FontSizeProperty, new System.Windows.Data.Binding(nameof(Control.FontSize)) { Source = button }); row.Children.Add(text);
        }
        button.Content = row; System.Windows.Automation.AutomationProperties.SetName(button, title); button.ToolTip = title; return button;
    }
    static void Raised(Button button)
    {
        button.SetResourceReference(Control.BackgroundProperty, "SegmentSelectedBrush"); button.SetResourceReference(Control.BorderBrushProperty, "LineBrush"); button.BorderThickness = new Thickness(.75);
    }
}

sealed class ScenePresetSavePanel : Border
{
    readonly TextBox _name;
    readonly Button _save, _cancel;
    readonly TextBlock _error, _conflict;
    readonly StackPanel _progress = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    readonly Func<string, bool> _hasConflict;
    readonly Func<string, Task<string?>> _commit;
    public bool IsSaving { get; private set; }
    public event Action? CancelRequested, Saved;

    public ScenePresetSavePanel(ProjectDocument project, string name, bool updating, Func<string, bool> hasConflict, Func<string, Task<string?>> commit)
    {
        _hasConflict = hasConflict; _commit = commit;
        Width = 480; Padding = new Thickness(24); CornerRadius = new CornerRadius(24); BorderThickness = new Thickness(.75);
        SetResourceReference(BackgroundProperty, "PanelBrush"); SetResourceReference(BorderBrushProperty, "LineBrush");
        var body = new StackPanel(); Child = body;
        var title = new TextBlock { Text = updating ? L.Text("更新场景预设") : L.Text("保存场景预设"), FontSize = 18 };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "FontSemi"); title.FontWeight = FontWeights.SemiBold; body.Children.Add(title);
        body.Children.Add(Secondary(L.Text("把这套配置留给下一次录制。"), 13, new Thickness(0, 6, 0, 20)));
        var nameLabel = new TextBlock { Text = L.Text("预设名称"), FontSize = 12 }; nameLabel.SetResourceReference(TextBlock.FontFamilyProperty, "FontMedium"); nameLabel.FontWeight = FontWeights.Medium; body.Children.Add(nameLabel);
        _name = new TextBox { Text = name, FontSize = 13, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 7, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(_name, L.Text("场景预设名称")); body.Children.Add(_name);
        _conflict = Secondary(L.Text("这个名称已存在，请换一个名称；更新已有预设请使用“更新此预设”。"), 11, new Thickness(0, 7, 0, 0)); _conflict.SetResourceReference(TextBlock.ForegroundProperty, "RecordBrush"); _conflict.Visibility = Visibility.Collapsed; body.Children.Add(_conflict);
        var rows = new StackPanel();
        rows.Children.Add(Configuration(IconKind.InsetRectangleFilled, L.Text("画布与屏幕"), L.Text("比例、大小、位置、裁切、样机、描边与阴影")));
        var crop = new DockPanel { Margin = new Thickness(0, 13, 0, 0) };
        var cropValue = new TextBlock { Text = ScenePresetCropPreviewPanel.CropSummary(project.Canvas.Crop, project.Width, project.Height), FontSize = 11, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(cropValue, Dock.Right); crop.Children.Add(cropValue); crop.Children.Add(Secondary(L.Text("裁切"), 11, new Thickness())); rows.Children.Add(crop);
        foreach (var (icon, rowTitle, detail) in new[] { (IconKind.PersonCropRectangle, L.Text("摄像头"), L.Text("显隐、布局、形状、外观与蒙版取景")), (IconKind.CursorMotion, L.Text("光标与运镜"), L.Text("光标效果、开场与新动画默认值")), (IconKind.Speaker, L.Text("声音与背景"), L.Text("全片音量、静音和背景资源副本")) })
        { var row = Configuration(icon, rowTitle, detail); row.Margin = new Thickness(0, 13, 0, 0); rows.Children.Add(row); }
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 20, 0, 20), BorderThickness = new Thickness(.75), Child = rows };
        card.SetResourceReference(BackgroundProperty, "SegmentWellBrush"); card.SetResourceReference(BorderBrushProperty, "LineBrush"); body.Children.Add(card);
        body.Children.Add(Secondary(L.Text("保留当前项目的素材、剪辑、分段声音和时间线动画。新录制默认场景可在预设菜单中单独选择。"), 11, new Thickness()));
        _error = Secondary("", 11, new Thickness(0, 12, 0, 0)); _error.SetResourceReference(TextBlock.ForegroundProperty, "RecordBrush"); _error.Visibility = Visibility.Collapsed; body.Children.Add(_error);
        var actions = new DockPanel { Margin = new Thickness(0, 20, 0, 0) };
        _save = new Button { Content = updating ? L.Text("更新预设") : L.Text("保存预设"), FontSize = 13, MinHeight = 32, Padding = new Thickness(12, 4, 12, 4), Style = (Style)FindResource("PrimaryButton"), IsDefault = true, Tag = "save" };
        _save.Click += async (_, _) => await SaveAsync(); DockPanel.SetDock(_save, Dock.Right); actions.Children.Add(_save);
        _cancel = new Button { Content = L.Text("取消"), FontSize = 13, MinHeight = 32, Padding = new Thickness(12, 4, 12, 4), Style = (Style)FindResource("QuietButton"), Margin = new Thickness(0, 0, 10, 0), IsCancel = true, Tag = "cancel" };
        _cancel.SetResourceReference(Control.BackgroundProperty, "SegmentWellBrush"); _cancel.SetResourceReference(Control.BorderBrushProperty, "LineBrush"); _cancel.BorderThickness = new Thickness(.75);
        _cancel.Click += (_, _) => CancelRequested?.Invoke(); DockPanel.SetDock(_cancel, Dock.Right); actions.Children.Add(_cancel);
        _progress.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 20, Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }); _progress.Children.Add(Secondary(L.Text("正在保存背景与配置…"), 11, new Thickness())); actions.Children.Add(_progress); body.Children.Add(actions);
        _name.TextChanged += (_, _) => RefreshValidity();
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SaveAsync(); } else if (e.Key == Key.Escape && !IsSaving) { e.Handled = true; CancelRequested?.Invoke(); } };
        RefreshValidity();
    }

    static TextBlock Secondary(string text, double size, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = size, Margin = margin, TextWrapping = TextWrapping.Wrap }; block.SetResourceReference(TextBlock.ForegroundProperty, "InkSecondaryBrush"); return block;
    }
    static FrameworkElement Configuration(IconKind icon, string title, string detail)
    {
        var row = new DockPanel(); var glyph = new Icon { Kind = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top }; glyph.SetResourceReference(Icon.ForegroundProperty, "InkSecondaryBrush"); DockPanel.SetDock(glyph, Dock.Left); row.Children.Add(glyph);
        var text = new StackPanel(); var label = new TextBlock { Text = title, FontSize = 12 }; label.SetResourceReference(TextBlock.FontFamilyProperty, "FontMedium"); label.FontWeight = FontWeights.Medium; text.Children.Add(label); text.Children.Add(Secondary(detail, 11, new Thickness(0, 3, 0, 0))); row.Children.Add(text); return row;
    }
    void RefreshValidity()
    {
        var conflict = _hasConflict(_name.Text.Trim()); _conflict.Visibility = conflict ? Visibility.Visible : Visibility.Collapsed;
        _save.IsEnabled = !IsSaving && !conflict && !string.IsNullOrWhiteSpace(_name.Text);
    }
    async Task SaveAsync()
    {
        if (!_save.IsEnabled || IsSaving) return;
        IsSaving = true; _name.IsEnabled = _cancel.IsEnabled = false; _error.Visibility = Visibility.Collapsed; _progress.Visibility = Visibility.Visible; RefreshValidity();
        try
        {
            var error = await _commit(_name.Text.Trim());
            if (error is null) { IsSaving = false; Saved?.Invoke(); } else { _error.Text = error; _error.Visibility = Visibility.Visible; }
        }
        catch (Exception error) { _error.Text = L.Text("预设未保存：") + error.Message; _error.Visibility = Visibility.Visible; }
        finally { IsSaving = false; _name.IsEnabled = _cancel.IsEnabled = true; _progress.Visibility = Visibility.Collapsed; RefreshValidity(); }
    }
}
