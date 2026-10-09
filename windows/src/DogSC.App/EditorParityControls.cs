using System.Globalization;
using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    void UpdateDecorationTiming()
    {
        foreach (var sticker in _project.Canvas.Stickers)
            if (_stickerImages.TryGetValue(sticker.Id, out var image))
                image.Opacity = sticker.Opacity * (EditingRegions && sticker.Id == _selectedDecoration ? 1 : EffectTiming.OpacityAt(sticker.Timing, _player.Time));
        var drawing = EditingRegions && _regionDrawingStyle is not null;
        _sourceRegions.Update(_player.Time, EditingRegions ? _selectedDecoration : null, drawing);
        _canvasRegions.Update(_player.Time, EditingRegions ? _selectedDecoration : null, drawing);
    }
    readonly HashSet<string> _expandedDetails = ["贴图、柔化与聚光"];
    StackPanel Disclosure(string title)
    {
        var content = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
        var section = new Expander { Header = title, Content = content, IsExpanded = _expandedDetails.Contains(title),
            Margin = new Thickness(0, 10, 0, 4), Foreground = (Brush)FindResource("InkBrush") };
        section.Expanded += (_, _) => _expandedDetails.Add(title);
        section.Collapsed += (_, _) => _expandedDetails.Remove(title);
        Inspector.Children.Add(section);
        return content;
    }

    void BuildCropControls()
    {
        var panel = Disclosure("裁切与自定义比例");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var ratio = _project.Canvas.Aspect.Split(':');
        var width = new TextBox { Text = ratio.Length == 2 ? ratio[0] : "16", Width = 58 };
        var height = new TextBox { Text = ratio.Length == 2 ? ratio[1] : "9", Width = 58 };
        System.Windows.Automation.AutomationProperties.SetName(width, "自定义比例宽");
        System.Windows.Automation.AutomationProperties.SetName(height, "自定义比例高");
        row.Children.Add(width);
        row.Children.Add(new TextBlock { Text = " : ", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(height);
        var apply = new Button { Content = "应用", Style = (Style)FindResource("QuietButton"), Margin = new Thickness(6, 0, 0, 0) };
        apply.Click += (_, _) =>
        {
            if (!double.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                || !double.TryParse(height.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                || !double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0 || w / h < 0.1 || w / h > 10)
            { TimeLabel.Text = "请输入有效比例，宽高比范围为 1:10 至 10:1。"; return; }
            Push();
            _project.Canvas.Aspect = w.ToString(CultureInfo.InvariantCulture) + ":" + h.ToString(CultureInfo.InvariantCulture);
            Commit();
        };
        row.Children.Add(apply);
        panel.Children.Add(row);
        var crop = _project.Canvas.Crop;
        CropSlider("左边", crop.X, 0, 1 - crop.Width, value => crop.X = value);
        CropSlider("顶部", crop.Y, 0, 1 - crop.Height, value => crop.Y = value);
        CropSlider("保留宽度", crop.Width, 0.02, 1 - crop.X, value => crop.Width = value);
        CropSlider("保留高度", crop.Height, 0.02, 1 - crop.Y, value => crop.Height = value);
        var reset = new Button { Content = "恢复完整画面", Style = (Style)FindResource("QuietButton") };
        reset.Click += (_, _) => { Push(); _project.Canvas.Crop = new(); Commit(); };
        panel.Children.Add(reset);
        void CropSlider(string name, double value, double min, double max, Action<double> set)
        {
            panel.Children.Add(Caption(name));
            var slider = Slider(min, max, value, set, refresh: true);
            slider.ToolTip = name + "（百分比）";
            System.Windows.Automation.AutomationProperties.SetName(slider, name);
            panel.Children.Add(slider);
        }
    }

    string? _selectedScenePreset;
    void BuildScenePresets()
    {
        var panel = Disclosure("我的场景预设");
        var settings = SettingsStore.Current;
        var items = settings.ScenePresets;
        var list = new ComboBox { ItemsSource = items, DisplayMemberPath = nameof(ScenePreset.Name), Margin = new Thickness(0, 0, 0, 6) };
        list.SelectedItem = items.FirstOrDefault(p => p.Id == _selectedScenePreset) ?? items.FirstOrDefault();
        var name = new TextBox { Text = (list.SelectedItem as ScenePreset)?.Name ?? "我的场景", Margin = new Thickness(0, 0, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetName(name, "场景预设名称");
        System.Windows.Automation.AutomationProperties.SetName(list, "已保存的场景预设");
        list.SelectionChanged += (_, _) =>
        {
            _selectedScenePreset = (list.SelectedItem as ScenePreset)?.Id;
            name.Text = (list.SelectedItem as ScenePreset)?.Name ?? "我的场景";
        };
        panel.Children.Add(list);
        panel.Children.Add(name);
        var row = new WrapPanel();
        Add("另存场景", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) return;
            var preset = ScenePresets.Capture(_project, _directory, name.Text);
            items.Add(preset);
            _selectedScenePreset = preset.Id;
            SaveAndRefresh();
        });
        Add("更新", () =>
        {
            if (list.SelectedItem is not ScenePreset old || string.IsNullOrWhiteSpace(name.Text)) return;
            var preset = ScenePresets.Capture(_project, _directory, name.Text);
            items[items.IndexOf(old)] = preset;
            if (settings.DefaultScenePresetId == old.Id) settings.DefaultScenePresetId = preset.Id;
            ScenePresets.RemoveImages(old);
            _selectedScenePreset = preset.Id;
            SaveAndRefresh();
        });
        Add("应用", () =>
        {
            if (list.SelectedItem is not ScenePreset preset) return;
            Push();
            ScenePresets.Apply(preset, _project, _directory);
            Commit();
        });
        Add("删除", () =>
        {
            if (list.SelectedItem is not ScenePreset preset) return;
            items.Remove(preset);
            if (settings.DefaultScenePresetId == preset.Id) settings.DefaultScenePresetId = null;
            ScenePresets.RemoveImages(preset);
            SaveAndRefresh();
        });
        panel.Children.Add(row);
        var useDefault = new CheckBox { Content = "将选中场景用于新录制", IsChecked = list.SelectedItem is ScenePreset selected && settings.DefaultScenePresetId == selected.Id, Margin = new Thickness(0, 5, 0, 4) };
        list.SelectionChanged += (_, _) => useDefault.IsChecked = list.SelectedItem is ScenePreset current && settings.DefaultScenePresetId == current.Id;
        useDefault.Click += (_, _) =>
        {
            settings.DefaultScenePresetId = useDefault.IsChecked == true ? (list.SelectedItem as ScenePreset)?.Id : null;
            SettingsStore.Save();
        };
        panel.Children.Add(useDefault);
        panel.Children.Add(Caption("保存画布、装饰和摄像头外观，图片随预设保留。"));
        void SaveAndRefresh() { SettingsStore.Save(); RebuildInspector(); }
        void Add(string title, Action action)
        {
            var button = new Button { Content = title, Style = (Style)FindResource("QuietButton"), Margin = new Thickness(0, 0, 4, 4) };
            button.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception ex) { TimeLabel.Text = "场景操作失败：" + ex.Message; }
            };
            row.Children.Add(button);
        }
    }

    void BuildCursorDetails()
    {
        var panel = Disclosure("光标细节与时间校准");
        var fx = _project.Canvas.Cursor;
        panel.Children.Add(Caption("光标大小"));
        panel.Children.Add(Slider(0.5, 4, fx.Size, value => fx.Size = value));
        panel.Children.Add(Caption("静止隐藏延迟（秒）"));
        panel.Children.Add(Slider(0.1, 10, fx.IdleDelay, value => fx.IdleDelay = value));
        panel.Children.Add(Caption("点击反馈大小"));
        panel.Children.Add(Slider(0.5, 4, fx.ClickScale, value => fx.ClickScale = value));
        panel.Children.Add(Caption("点击反馈不透明度"));
        panel.Children.Add(Slider(0, 1, fx.ClickOpacity, value => fx.ClickOpacity = value));
        var colors = new WrapPanel();
        foreach (var hex in new[] { "#292929", "#FFFFFF", "#36A87E", "#E56060", "#528AE8", "#F3BC48" })
        {
            var swatch = new Button { Width = 28, Height = 28, Background = Brush(hex), ToolTip = hex, Margin = new Thickness(0, 4, 8, 4) };
            swatch.Click += (_, _) => { Push(); fx.ClickColor = hex; Commit(rebuild: false); };
            colors.Children.Add(swatch);
        }
        panel.Children.Add(colors);
        AddTimingControls(panel, () => fx.TimingAdvance, value => fx.TimingAdvance = value, 1);
        panel.Children.Add(Caption("正值让光标和点击提前，负值延后，不改变移动速度。"));
    }

    void BuildCameraDetails()
    {
        var panel = Disclosure("摄像头显示与音画同步");
        var camera = _project.Camera;
        AddToggle(panel, "隐藏摄像头", camera.Hidden, value => camera.Hidden = value);
        AddToggle(panel, "镜像摄像头", camera.Mirrored, value => camera.Mirrored = value);
        AddTimingControls(panel, () => camera.TimingAdvance, value => camera.TimingAdvance = value, 5);
        panel.Children.Add(Caption("声音先于嘴型时提前画面；每次调整保留当前播放位置。"));
    }

    void BuildEffectTiming(string title, EffectWindow timing)
    {
        var panel = Disclosure(title + "时间与过渡");
        var total = TimelineMath.Duration(_project);
        var limited = timing.Duration > 0;
        var toggle = new CheckBox { Content = "仅在指定时间段显示", IsChecked = limited, Margin = new Thickness(0, 4, 0, 6) };
        toggle.Click += (_, _) =>
        {
            Push();
            timing.Start = Math.Clamp(_player.Time, 0, Math.Max(0, total - 0.12));
            timing.Duration = toggle.IsChecked == true ? Math.Max(0.12, Math.Min(3, total - timing.Start)) : 0;
            Commit();
        };
        panel.Children.Add(toggle);
        if (!limited) return;
        panel.Children.Add(Caption("开始（秒）"));
        panel.Children.Add(Slider(0, Math.Max(0, total - timing.Duration), timing.Start, value => timing.Start = value, refresh: true));
        panel.Children.Add(Caption("持续（秒）"));
        panel.Children.Add(Slider(0.12, Math.Max(0.12, total - timing.Start), timing.Duration, value => timing.Duration = value, refresh: true));
        var transitions = new ComboBox { Margin = new Thickness(0, 8, 0, 4) };
        foreach (var (transitionTitle, key) in new[] { ("无过渡", "none"), ("线性过渡", "linear"), ("平滑过渡", "smooth") })
            transitions.Items.Add(new ComboBoxItem { Content = transitionTitle, Tag = key });
        transitions.SelectedItem = transitions.Items.Cast<ComboBoxItem>().First(x => Equals(x.Tag, timing.Transition));
        transitions.SelectionChanged += (_, _) => { Push(); timing.Transition = (string)((ComboBoxItem)transitions.SelectedItem).Tag; Commit(); };
        panel.Children.Add(transitions);
        if (timing.Transition != "none")
        {
            panel.Children.Add(Caption("淡入（秒）"));
            panel.Children.Add(Slider(0, 5, timing.FadeIn ?? Math.Min(timing.Fade, timing.Duration / 2), value => timing.FadeIn = value));
            panel.Children.Add(Caption("淡出（秒）"));
            panel.Children.Add(Slider(0, 5, timing.FadeOut ?? Math.Min(timing.Fade, timing.Duration / 2), value => timing.FadeOut = value));
        }
        var row = new WrapPanel();
        Add("从播放头开始", () => { timing.Start = Math.Clamp(_player.Time, 0, Math.Max(0, total - 0.12)); timing.Duration = Math.Min(timing.Duration, total - timing.Start); });
        Add("结束设为播放头", () => timing.Duration = Math.Max(0.12, _player.Time - timing.Start));
        panel.Children.Add(row);
        panel.Children.Add(Caption("也可在下方时间线上拖动色块或两端。"));
        void Add(string text, Action action)
        {
            var button = new Button { Content = text, Style = (Style)FindResource("QuietButton"), Margin = new Thickness(0, 0, 4, 4) };
            button.Click += (_, _) => { Push(); action(); Commit(); };
            row.Children.Add(button);
        }
    }

    void AddToggle(Panel panel, string title, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = title, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        box.Click += (_, _) => { Push(); set(box.IsChecked == true); Commit(rebuild: false); };
        panel.Children.Add(box);
    }

    void AddTimingControls(Panel panel, Func<double> get, Action<double> set, double limit)
    {
        var label = Caption("");
        panel.Children.Add(label);
        void Paint() => label.Text = "时间偏移 " + (get() * 1000).ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms";
        Paint();
        var row = new WrapPanel();
        foreach (var (title, delta) in new[] { ("−100 ms", -0.1), ("−10 ms", -0.01), ("+10 ms", 0.01), ("+100 ms", 0.1), ("归零", double.NaN) })
        {
            var button = new Button { Content = title, Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 4) };
            button.Click += (_, _) =>
            {
                Push();
                set(double.IsNaN(delta) ? 0 : Math.Clamp(Math.Round(get() + delta, 3), -limit, limit));
                Commit(rebuild: false);
                _player.Seek(_player.Time, _player.IsPlaying);
                Paint();
            };
            row.Children.Add(button);
        }
        panel.Children.Add(row);
    }
}
