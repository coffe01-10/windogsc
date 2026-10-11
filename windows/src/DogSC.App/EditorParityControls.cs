using System.Globalization;
using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    void UpdateDecorationTiming()
    {
        var drawing = EditingRegions && _regionDrawingStyle is not null;
        _sourceRegions.Update(_player.Time, EditingRegions ? _selectedDecoration : null, drawing);
        _canvasRegions.Update(_player.Time, EditingRegions ? _selectedDecoration : null, drawing);
    }
    readonly HashSet<string> _expandedDetails = ["贴图、柔化与聚光"];
    StackPanel Disclosure(string title, Func<string>? summary = null)
    {
        var content = new StackPanel();
        var section = new Expander { Header = title, Content = content, IsExpanded = _expandedDetails.Contains(title),
            Margin = new Thickness(0, 10, 0, 4), Foreground = (Brush)FindResource("InkBrush") };
        if (summary is not null)
        {
            var header = new StackPanel(); header.Children.Add(new TextBlock { Text = title });
            section.MinHeight = 48;
            var detail = new TextBlock { Text = summary(), FontSize = 11, Foreground = (Brush)FindResource("InkSecondaryBrush"), Margin = new Thickness(0, 2, 0, 0) };
            header.Children.Add(detail); section.Header = header;
            section.Expanded += (_, _) => detail.Text = summary(); section.Collapsed += (_, _) => detail.Text = summary();
            content.AddHandler(System.Windows.Controls.Primitives.RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((_, _) => detail.Text = summary()));
        }
        section.Expanded += (_, _) => _expandedDetails.Add(title);
        section.Collapsed += (_, _) => _expandedDetails.Remove(title);
        Inspector.Children.Add(section);
        return content;
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
            var swatch = new Button { Width = 28, Height = 28, Background = Brush(hex), ToolTip = hex, Tag = hex, Padding = new Thickness(0), Margin = new Thickness(0, 4, 8, 4) };
            swatch.Click += (_, _) => { Push(); fx.ClickColor = hex; PaintColors(); Commit(rebuild: false); };
            colors.Children.Add(swatch);
        }
        PaintColors();
        panel.Children.Add(colors);
        AddTimingControls(panel, () => fx.TimingAdvance, value => fx.TimingAdvance = value, 1);
        panel.Children.Add(Caption("正值让光标和点击提前，负值延后，不改变移动速度。"));

        void PaintColors()
        {
            foreach (var swatch in colors.Children.OfType<Button>())
            {
                var selected = string.Equals((string)swatch.Tag, fx.ClickColor, StringComparison.OrdinalIgnoreCase);
                swatch.BorderThickness = new Thickness(selected ? 2 : 1);
                swatch.SetResourceReference(Control.BorderBrushProperty, selected ? "InkBrush" : "ControlBorderBrush");
            }
        }
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
        var box = new CheckBox { Content = title, IsChecked = value, Style = (Style)FindResource("SwitchCheckBox"), Margin = new Thickness(0, 4, 0, 4) };
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
