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
        CancelOverlayGesture(); _cameraSelected = false; CancelRegionEditing();
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
        + _project.Canvas.SoftRegions.RemoveAll(x => x.Id == id)
        + _project.ScreenMotions.RemoveAll(x => x.Id == id)
        + _project.CameraMotions.RemoveAll(x => x.Id == id) > 0;

    TransitionTiming? MotionTimingOf(string id) =>
        _project.ScreenMotions.FirstOrDefault(x => x.Id == id)?.Timing
        ?? _project.CameraMotions.FirstOrDefault(x => x.Id == id)?.Timing;

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
            ApplyTrackFlags(TrackFlags | 8);
        })));
        foreach (var (title, style) in new[] { ("＋柔化", "blur"), ("＋聚光", "spotlight") })
            add.Children.Add(DecorationButton(title, () => BeginRegionDrawing(style)));
        // Mac 运动轨道：在播放头插入屏幕 / 摄像头运动片段。
        add.Children.Add(DecorationButton("＋屏幕运动", () =>
        {
            Push();
            var clip = new ScreenMotionClip();
            clip.Timing.StartTime = Math.Clamp(_player.Time, 0, Math.Max(0, TimelineMath.Duration(_project) - .12));
            clip.Timing.LeadInDuration = 0.7;
            clip.Timing.ReturnDuration = 0.7;
            _project.ScreenMotions.Add(clip);
            _selectedDecoration = clip.Id; _selectedClip = _selectedZoom = null;
            ApplyTrackFlags(TrackFlags | 2);
            Commit();
        }));
        add.Children.Add(DecorationButton("＋摄像头运动", () =>
        {
            Push();
            var clip = new CameraMotionClip();
            clip.Timing.StartTime = Math.Clamp(_player.Time, 0, Math.Max(0, TimelineMath.Duration(_project) - .12));
            clip.Timing.LeadInDuration = 0.7;
            clip.Timing.ReturnDuration = 0.7;
            _project.CameraMotions.Add(clip);
            _selectedDecoration = clip.Id; _selectedClip = _selectedZoom = null;
            ApplyTrackFlags(TrackFlags | 4);
            Commit();
        }));
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
        foreach (var (motion, i) in _project.ScreenMotions.Select((x, i) => (x, i)))
            list.Items.Add(new ComboBoxItem { Content = $"屏幕运动 {i + 1}", Tag = motion.Id });
        foreach (var (motion, i) in _project.CameraMotions.Select((x, i) => (x, i)))
            list.Items.Add(new ComboBoxItem { Content = $"摄像头运动 {i + 1}", Tag = motion.Id });
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
            selectionPending = false; RebuildInspector();
            if (_project.Canvas.Stickers.Any(s => s.Id == _selectedDecoration)) OverlayEditor.Focus(); else RegionEditor.Focus();
        };
        if (list.Items.Count > 0) section.Children.Add(list);
        var sticker = _project.Canvas.Stickers.FirstOrDefault(x => x.Id == _selectedDecoration);
        var soft = _project.Canvas.SoftRegions.FirstOrDefault(x => x.Id == _selectedDecoration);
        var screenMotion = _project.ScreenMotions.FirstOrDefault(x => x.Id == _selectedDecoration);
        var cameraMotion = _project.CameraMotions.FirstOrDefault(x => x.Id == _selectedDecoration);
        if (screenMotion is not null || cameraMotion is not null)
        {
            BuildMotionInspector(section, screenMotion, cameraMotion);
            return;
        }
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
            section.Children.Add(Caption("拖动摆放 · 边角缩放 · 顶部手柄旋转"));
            var tips = Caption("Alt + 点击切换重叠图层；Tab 切图层。");
            tips.ToolTip = "旋转时按住 Shift 吸附 15°；方向键移动 1 像素，Shift + 方向键移动 10 像素。"; section.Children.Add(tips);
            BuildStickerNumbers(section, sticker);
            Control("宽度", .02, 1, sticker.Width, x => sticker.Width = x);
            Control("旋转角度", -180, 180, sticker.Rotation, x => sticker.Rotation = x);
            Control("不透明度", 0, 1, sticker.Opacity, x => sticker.Opacity = x);
            Control("水平位置", 0, 1, sticker.X, x => sticker.X = x);
            Control("垂直位置", 0, 1, sticker.Y, x => sticker.Y = x);

            var body = Disclosure("外观与动画");
            // Mac 贴纸外观：圆角 / 描边 / 投影。
            void DetailControl(string title, double min, double max, double value, Action<double> assign)
            { body.Children.Add(Caption(title)); body.Children.Add(Slider(min, max, value, assign)); }
            DetailControl("圆角", 0, 120, sticker.CornerRadius, x => sticker.CornerRadius = x);
            DetailControl("描边宽度", 0, 40, sticker.BorderWidth, x => sticker.BorderWidth = x);
            var borderRow = new WrapPanel();
            foreach (var (name, hex) in new[] { ("白", "#FFFFFF"), ("黑", "#000000"), ("黄", "#FFD60A"), ("蓝", "#0A84FF"), ("红", "#FF453A") })
            {
                var swatch = new Button
                {
                    Content = name, Tag = hex, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2),
                    Style = (Style)FindResource("ChipButton"),
                };
                if (string.Equals(sticker.BorderColor, hex, StringComparison.OrdinalIgnoreCase))
                    swatch.Background = (Brush)FindResource("AccentBrush");
                swatch.Click += (_, _) => { Push(); sticker.BorderColor = hex; Commit(); };
                borderRow.Children.Add(swatch);
            }
            body.Children.Add(Caption("描边颜色")); body.Children.Add(borderRow);
            DetailControl("阴影强度", 0, 1, sticker.ShadowOpacity, x => sticker.ShadowOpacity = x);
            DetailControl("阴影半径", 0, 80, sticker.ShadowRadius, x => sticker.ShadowRadius = x);
            DetailControl("阴影偏移 X", -40, 40, sticker.ShadowOffsetX, x => sticker.ShadowOffsetX = x);
            DetailControl("阴影偏移 Y", -40, 40, sticker.ShadowOffsetY, x => sticker.ShadowOffsetY = x);
            // Mac 动画：预设 × 曲线 × 时长；退出预设 null = 自动互补。
            void Chips(string caption, (string Label, string? Value)[] presets, string? current, Action<string?> assign)
            {
                body.Children.Add(Caption(caption));
                var row = new WrapPanel();
                foreach (var (label, value) in presets)
                {
                    var chip = new Button
                    {
                        Content = label, Tag = value, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2),
                        Style = (Style)FindResource("ChipButton"),
                    };
                    var selected = string.Equals(current ?? "", value ?? "", StringComparison.OrdinalIgnoreCase);
                    chip.Background = (Brush)FindResource(selected ? "AccentBrush" : "WellBrush");
                    chip.Foreground = (Brush)FindResource(selected ? "OnAccentBrush" : "InkBrush");
                    chip.Click += (_, _) => { Push(); assign(value); Commit(); };
                    row.Children.Add(chip);
                }
                body.Children.Add(row);
            }
            (string, string?)[] animPresets = [("无", "none"), ("淡入", "fade"), ("弹出", "pop"),
                ("左滑入", "slideLeft"), ("右滑入", "slideRight"), ("上滑入", "slideUp"), ("下滑入", "slideDown"),
                ("左上", "slideTopLeft"), ("右上", "slideTopRight"), ("左下", "slideBottomLeft"), ("右下", "slideBottomRight")];
            Chips("进入动画", animPresets, sticker.Animation, x => sticker.Animation = x ?? "pop");
            Chips("退出动画", [("自动", null), .. animPresets], sticker.ExitAnimation, x => sticker.ExitAnimation = x);
            Chips("动画曲线", [("平缓", "smooth"), ("迅速", "swift"), ("柔和", "gentle"), ("线性", "linear")],
                sticker.AnimationCurve, x => sticker.AnimationCurve = x ?? "swift");
            DetailControl("进入时长", 0, 2, sticker.EnterDuration, x => sticker.EnterDuration = x);
            DetailControl("退出时长", 0, 2, sticker.ExitDuration, x => sticker.ExitDuration = x);
            DetailControl("背景虚化", 0, 60, sticker.BackdropBlur, x => sticker.BackdropBlur = x);
            void Toggle(string title, bool value, Action<bool> assign)
            {
                var box = new CheckBox { Content = title, IsChecked = value, Margin = new Thickness(0, 6, 0, 2) };
                box.Click += (_, _) => { Push(); assign(box.IsChecked == true); Commit(); };
                body.Children.Add(box);
            }
            Toggle("虚化处理含摄像头", sticker.BackdropBlurIncludesCamera, x => sticker.BackdropBlurIncludesCamera = x);
            Toggle("显示时隐藏屏幕内容", sticker.HidesScreen, x => sticker.HidesScreen = x);
            Toggle("显示时隐藏摄像头", sticker.HidesCamera, x => sticker.HidesCamera = x);
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

public partial class EditorWindow
{
    /// <summary>Mac 运动片段编辑：时间（起止/进入/回落/曲线）+ 目标构图。</summary>
    void BuildMotionInspector(Panel section, ScreenMotionClip? screen, CameraMotionClip? camera)
    {
        var timing = (screen?.Timing ?? camera!.Timing)!;
        section.Children.Add(Caption(screen is not null ? "屏幕运动片段 · 轨道可拖动、两端可拉长" : "摄像头运动片段 · 轨道可拖动、两端可拉长"));
        var commands = new WrapPanel();
        commands.Children.Add(DecorationButton("删除运动片段", () =>
        {
            Push(); DeleteDecoration(_selectedDecoration!); _selectedDecoration = null; Commit();
        }));
        section.Children.Add(commands);

        void Control(string title, double min, double max, double value, Action<double> assign)
        { section.Children.Add(Caption(title)); section.Children.Add(Slider(min, max, value, assign, refresh: true)); }

        var duration = TimelineMath.Duration(_project);
        section.Children.Add(Caption("时间"));
        Control("起点", 0, Math.Max(0.1, duration - .05), timing.StartTime, x => timing.StartTime = x);
        Control("片段时长（含进入）", .05, Math.Max(.5, duration), timing.Duration, x => timing.Duration = x);
        Control("进入时长", 0, 2, timing.LeadInDuration, x => { timing.LeadInDuration = x; timing.PreferredLeadInDuration = null; });
        Control("回落时长", 0, 2, timing.ReturnDuration, x => { timing.ReturnDuration = x; timing.PreferredReturnDuration = null; });

        var easings = new WrapPanel();
        foreach (var (name, key) in new[] { ("线性", "linear"), ("二次", "quadratic"), ("三次", "cubic"), ("五次", "quintic"), ("弹簧", "spring") })
        {
            var chip = new Button { Content = name, Tag = key, Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 4) };
            chip.Background = (Brush)FindResource(timing.Easing == key ? "AccentBrush" : "WellBrush");
            chip.Foreground = (Brush)FindResource(timing.Easing == key ? "OnAccentBrush" : "InkBrush");
            chip.Click += (_, _) => { Push(); timing.Easing = key; Commit(); };
            easings.Children.Add(chip);
        }
        section.Children.Add(Caption("曲线"));
        section.Children.Add(easings);

        if (screen is not null)
        {
            section.Children.Add(Caption("目标构图（归一化）"));
            Control("水平位置", 0, 1, screen.Target.X, x => screen.Target.X = x);
            Control("垂直位置", 0, 1, screen.Target.Y, x => screen.Target.Y = x);
            Control("缩放", .25, 3, screen.Target.Scale, x => screen.Target.Scale = x);
            Control("旋转 Z", -90, 90, screen.Target.RotationZ, x => screen.Target.RotationZ = x);
            Control("旋转 X", -60, 60, screen.Target.RotationX, x => screen.Target.RotationX = x);
            Control("旋转 Y", -60, 60, screen.Target.RotationY, x => screen.Target.RotationY = x);
            Control("透视", 0, 1, screen.Target.Perspective, x => screen.Target.Perspective = x);
            section.Children.Add(Caption("聚焦锚点（%）"));
            Control("锚点 X", 0, 1, screen.Target.AnchorX, x => screen.Target.AnchorX = x);
            Control("锚点 Y", 0, 1, screen.Target.AnchorY, x => screen.Target.AnchorY = x);
            var blur = new CheckBox { Content = "线性模糊（运动窗口内加高斯模糊）", IsChecked = screen.LinearBlur, Margin = new Thickness(0, 6, 0, 2) };
            blur.Checked += (_, _) => { Push(); screen.LinearBlur = true; Commit(); };
            blur.Unchecked += (_, _) => { Push(); screen.LinearBlur = false; Commit(); };
            section.Children.Add(blur);
        }
        else
        {
            var layouts = new WrapPanel();
            foreach (var (name, key) in new[] { ("方形", "square"), ("圆形", "circle"), ("横向", "horizontal"), ("竖向", "vertical"), ("原比例", "original"), ("全屏", "fullscreen") })
            {
                var chip = new Button { Content = name, Tag = key, Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 4) };
                chip.Background = (Brush)FindResource(camera!.Target.Layout == key ? "AccentBrush" : "WellBrush");
                chip.Foreground = (Brush)FindResource(camera!.Target.Layout == key ? "OnAccentBrush" : "InkBrush");
                chip.Click += (_, _) => { Push(); camera.Target.Layout = key; Commit(); };
                layouts.Children.Add(chip);
            }
            section.Children.Add(Caption("目标形状"));
            section.Children.Add(layouts);
            Control("水平位置", 0, 1, camera!.Target.X, x => camera.Target.X = x);
            Control("垂直位置", 0, 1, camera.Target.Y, x => camera.Target.Y = x);
            Control("大小", .05, 1, camera.Target.Size, x => camera.Target.Size = x);
            Control("圆角", 0, 1, camera.Target.Roundness, x => camera.Target.Roundness = x);
            Control("不透明度（0=隐藏）", 0, 1, camera.Target.Opacity, x => camera.Target.Opacity = x);
        }
    }
}
