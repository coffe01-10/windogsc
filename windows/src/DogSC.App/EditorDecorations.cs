using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    sealed record DecorationClipboard(StickerImage? Sticker, SoftRegion? Region);
    static DecorationClipboard? _decorationClipboard;

    EffectWindow? DecorationTiming(string id) => _project.Canvas.Stickers.FirstOrDefault(x => x.Id == id)?.Timing
        ?? _project.Canvas.SoftRegions.FirstOrDefault(x => x.Id == id)?.Timing;

    void SelectDecoration(string id, bool rebuild = true)
    {
        CancelRegionEditing();
        _player.Pause();
        _selectedDecoration = id; _selectedClip = null; _selectedZoom = null; _tool = "effects";
        _expandedDetails.Add("贴图、柔化与聚光");
        if (rebuild) RebuildInspector();
        SyncSelection(); ApplyZoomVisual();
    }

    EffectWindow NewDecorationTiming() => new()
    {
        Start = Math.Clamp(_player.Time, 0, Math.Max(0, TimelineMath.Duration(_project) - .12)),
        Duration = Math.Min(3, Math.Max(.12, TimelineMath.Duration(_project) - _player.Time)),
    };

    bool DeleteDecoration(string id) => _project.Canvas.Stickers.RemoveAll(x => x.Id == id)
        + _project.Canvas.SoftRegions.RemoveAll(x => x.Id == id) > 0;

    Button DecorationButton(string title, Action action)
    {
        var button = new Button { Content = title, Style = (Style)FindResource("QuietButton"), Margin = new Thickness(0, 0, 4, 4) };
        button.Click += (_, _) => action();
        return button;
    }

    void BuildDecorations()
    {
        var section = Disclosure("贴图、柔化与聚光");
        var add = new WrapPanel();
        add.Children.Add(DecorationButton("＋贴图", () => PickDecoration((path, name) =>
        {
            var image = new StickerImage { Path = path, Name = name, Timing = NewDecorationTiming() };
            _project.Canvas.Stickers.Add(image); _selectedDecoration = image.Id; _selectedClip = _selectedZoom = null;
        })));
        foreach (var (title, style) in new[] { ("＋柔化", "blur"), ("＋聚光", "spotlight") })
            add.Children.Add(DecorationButton(title, () => BeginRegionDrawing(style)));
        section.Children.Add(add);
        if (_regionDrawingStyle is not null)
        {
            section.Children.Add(Caption("在预览画面上拖动框选。选区效果暂时隐藏，方便看清目标；Esc 取消。"));
            section.Children.Add(DecorationButton("取消框选", () => { CancelRegionEditing(); RebuildInspector(); ApplyZoomVisual(); }));
            return;
        }
        var list = new ComboBox { Margin = new Thickness(0, 6, 0, 6), MinWidth = 160 };
        foreach (var (image, i) in _project.Canvas.Stickers.Select((x, i) => (x, i)))
            list.Items.Add(new ComboBoxItem { Content = $"贴图 {i + 1} · {image.Name}", Tag = image.Id });
        foreach (var (region, i) in _project.Canvas.SoftRegions.Select((x, i) => (x, i)))
            list.Items.Add(new ComboBoxItem { Content = $"{(region.Style == "spotlight" ? "聚光" : "柔化")} {i + 1}", Tag = region.Id });
        list.SelectedItem = list.Items.Cast<ComboBoxItem>().FirstOrDefault(x => Equals(x.Tag, _selectedDecoration));
        var selectionPending = false;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ComboBoxItem item) return;
            selectionPending = list.IsDropDownOpen;
            SelectDecoration((string)item.Tag, rebuild: !selectionPending);
        };
        list.DropDownClosed += (_, _) =>
        {
            if (!selectionPending) return;
            selectionPending = false; RebuildInspector(); RegionEditor.Focus();
        };
        if (list.Items.Count > 0) section.Children.Add(list);
        var sticker = _project.Canvas.Stickers.FirstOrDefault(x => x.Id == _selectedDecoration);
        var soft = _project.Canvas.SoftRegions.FirstOrDefault(x => x.Id == _selectedDecoration);
        if (sticker is null && soft is null)
        {
            section.Children.Add(Caption("选择柔化或聚光后，在预览里框选目标。已有区域可直接点击、拖动和缩放。"));
            return;
        }
        var commands = new WrapPanel();
        commands.Children.Add(DecorationButton("删除效果", () => { Push(); DeleteDecoration(_selectedDecoration!); _selectedDecoration = null; Commit(); }));
        if (sticker is not null)
        {
            commands.Children.Add(DecorationButton("换图…", () => PickDecoration((path, name) => { sticker.Path = path; sticker.Name = name; })));
            foreach (var (title, delta) in new[] { ("上移图层", 1), ("下移图层", -1) })
                commands.Children.Add(DecorationButton(title, () =>
                {
                    var index = _project.Canvas.Stickers.IndexOf(sticker);
                    var target = Math.Clamp(index + delta, 0, _project.Canvas.Stickers.Count - 1);
                    if (target == index) return;
                    Push(); _project.Canvas.Stickers.RemoveAt(index); _project.Canvas.Stickers.Insert(target, sticker); Commit();
                }));
        }
        section.Children.Add(commands);
        void Control(string title, double min, double max, double value, Action<double> assign, bool refresh = false)
        { section.Children.Add(Caption(title)); section.Children.Add(Slider(min, max, value, assign, refresh)); }
        if (sticker is not null)
        {
            Control("宽度", .02, 1, sticker.Width, x => sticker.Width = x);
            Control("旋转角度", -180, 180, sticker.Rotation, x => sticker.Rotation = x);
            Control("不透明度", 0, 1, sticker.Opacity, x => sticker.Opacity = x);
            Control("水平位置", 0, 1, sticker.X, x => sticker.X = x);
            Control("垂直位置", 0, 1, sticker.Y, x => sticker.Y = x);
        }
        if (soft is not null)
        {
            commands.Children.Insert(0, DecorationButton("重画区域", () => BeginRegionDrawing(soft.Style, soft.Id)));
            var guidance = Caption("拖动移动 · 拖边角缩放 · 方向键微调");
            guidance.ToolTip = "方向键移动 1 像素，Shift + 方向键移动 10 像素。暂停时完整显示所选效果，播放时按时间线显示。";
            section.Children.Add(guidance);
            RegionValueControl(section, soft.Style == "spotlight" ? "外围压暗" : "柔化强度", 0, 1,
                soft.Style == "spotlight" ? soft.Dimming : soft.Strength,
                x => { if (soft.Style == "spotlight") soft.Dimming = x; else soft.Strength = x; });
            BuildRegionNumbers(section, soft);
            var details = Disclosure("区域外观与坐标");
            var enabled = new CheckBox { Content = "启用效果", IsChecked = soft.Enabled, Margin = new Thickness(0, 6, 0, 2) };
            enabled.Click += (_, _) => { Push(); soft.Enabled = enabled.IsChecked == true; Commit(); };
            details.Children.Add(enabled);
            var source = new CheckBox { Content = "跟随原画面缩放与透视", IsChecked = soft.CoordinateSpace == "source", Margin = new Thickness(0, 6, 0, 2) };
            source.ToolTip = "切换时按当前可见范围换算位置。画布区域固定在成片中，原画面区域跟随录制画面。";
            source.Click += (_, _) => ChangeRegionCoordinates(soft, source.IsChecked == true ? "source" : "canvas");
            details.Children.Add(source);
            RegionValueControl(details, "圆角", 0, .5, soft.Corner, x => soft.Corner = x);
        }
        BuildEffectTiming("所选效果", sticker?.Timing ?? soft!.Timing);
    }

    bool CopyDecoration()
    {
        if (_project.Canvas.Stickers.FirstOrDefault(x => x.Id == _selectedDecoration) is { } sticker)
        {
            var copy = ScenePreset.Clone(sticker);
            copy.Path = DecorationPath(copy.Path) ?? copy.Path;
            _decorationClipboard = new(copy, null);
        }
        else if (_project.Canvas.SoftRegions.FirstOrDefault(x => x.Id == _selectedDecoration) is { } region)
            _decorationClipboard = new(null, ScenePreset.Clone(region));
        else return false;
        _zoomClipboard = null; _effectClipboard = null;
        TimeLabel.Text = "已复制效果；Ctrl+V 添加副本，Ctrl+Shift+V 只粘属性。";
        return true;
    }

    static EffectWindow AttributesTiming(EffectWindow source, EffectWindow target)
    {
        var timing = ScenePreset.Clone(source);
        timing.Start = target.Start; timing.Duration = target.Duration;
        return timing;
    }

    void PasteDecoration(bool includeTiming)
    {
        var source = _decorationClipboard!;
        var targetSticker = _project.Canvas.Stickers.FirstOrDefault(x => x.Id == _selectedDecoration);
        var targetRegion = _project.Canvas.SoftRegions.FirstOrDefault(x => x.Id == _selectedDecoration);
        if (!includeTiming && (source.Sticker is not null ? targetSticker is null : targetRegion is null))
        { TimeLabel.Text = "请先选中同类型效果，再粘贴属性。"; return; }
        var start = Math.Clamp(Timeline.PasteTime, 0, Math.Max(0, TimelineMath.Duration(_project) - .12));
        EffectWindow Timing(EffectWindow original, EffectWindow? target) => includeTiming
            ? ScenePreset.Clone(original)
            : AttributesTiming(original, target!);
        Push();
        if (source.Sticker is { } image)
        {
            var copy = ScenePreset.Clone(image);
            copy.Id = includeTiming ? Guid.NewGuid().ToString("N") : targetSticker!.Id;
            copy.Timing = Timing(image.Timing, targetSticker?.Timing);
            if (includeTiming) { copy.Timing.Start = start; copy.Timing.Duration = Math.Min(image.Timing.Duration > 0 ? image.Timing.Duration : 3, TimelineMath.Duration(_project) - start); }
            if (File.Exists(copy.Path))
            {
                var relative = "media/decor/" + Guid.NewGuid().ToString("N") + Path.GetExtension(copy.Path);
                var absolute = ProjectFile.Media(_directory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(absolute)!); File.Copy(copy.Path, absolute); copy.Path = relative;
            }
            if (includeTiming) _project.Canvas.Stickers.Add(copy);
            else _project.Canvas.Stickers[_project.Canvas.Stickers.IndexOf(targetSticker!)] = copy;
            _selectedDecoration = copy.Id;
        }
        else if (source.Region is { } region)
        {
            var copy = ScenePreset.Clone(region);
            copy.Id = includeTiming ? Guid.NewGuid().ToString("N") : targetRegion!.Id;
            copy.Timing = Timing(region.Timing, targetRegion?.Timing);
            if (includeTiming) { copy.Timing.Start = start; copy.Timing.Duration = Math.Min(region.Timing.Duration > 0 ? region.Timing.Duration : 3, TimelineMath.Duration(_project) - start); }
            if (includeTiming) _project.Canvas.SoftRegions.Add(copy);
            else _project.Canvas.SoftRegions[_project.Canvas.SoftRegions.IndexOf(targetRegion!)] = copy;
            _selectedDecoration = copy.Id;
        }
        _selectedClip = _selectedZoom = null; _tool = "effects"; Commit();
    }
}
