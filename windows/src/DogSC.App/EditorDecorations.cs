using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    sealed record DecorationClipboard(StickerImage? Sticker, SoftRegion? Region, ScreenMotionClip? ScreenMotion = null, CameraMotionClip? CameraMotion = null);
    static DecorationClipboard? _decorationClipboard;

    EffectWindow? DecorationTiming(string id) => _project.Canvas.Stickers.FirstOrDefault(x => x.Id == id)?.Timing
        ?? _project.Canvas.SoftRegions.FirstOrDefault(x => x.Id == id)?.Timing;

    void SelectDecoration(string id, bool rebuild = true)
    {
        CancelOverlayGesture(); _cameraSelected = false; CancelRegionEditing();
        _player.Pause();
        _selectedDecoration = id; _selectedClip = null; _selectedZoom = null;
        _tool = MotionTimingOf(id) is not null ? "zoom" : "effects";
        if (_project.ScreenMotions.Any(c => c.Id == id)) _motionMode = "screen3d";
        _expandedDetails.Add(L.Text("贴图、柔化与聚光"));
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

    void AddSticker() => PickDecoration(InsertSticker);

    void InsertSticker(string path, string name)
    {
        CancelOverlayGesture(); CancelRegionEditing(); _cameraSelected = false;
        _player.Pause();
        var image = new StickerImage { Path = path, Name = name, Timing = NewDecorationTiming() };
        _project.Canvas.Stickers.Add(image); _selectedDecoration = image.Id; _selectedClip = _selectedZoom = null;
        _tool = "effects";
        _expandedDetails.Add(L.Text("贴图、柔化与聚光"));
        ApplyTrackFlags(TrackFlags | 8);
    }

    static string? ClipboardImageFile() => Clipboard.ContainsFileDropList()
        ? Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(path => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
        : null;

    static bool CanPasteSticker()
    {
        try { return Clipboard.ContainsImage() || ClipboardImageFile() is not null; }
        catch (System.Runtime.InteropServices.ExternalException) { return false; } // Another application can briefly lock the clipboard.
    }

    void AddPastedSticker()
    {
        try
        {
            var image = Clipboard.GetImage();
            var sourceFile = image is null ? ClipboardImageFile() : null;
            if (image is null && sourceFile is null) return;
            var folder = Path.Combine(_directory, "media", "decor"); Directory.CreateDirectory(folder);
            var name = Guid.NewGuid().ToString("N") + (image is null ? Path.GetExtension(sourceFile) : ".png");
            var output = Path.Combine(folder, name);
            if (image is not null)
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var stream = File.Create(output); encoder.Save(stream);
            }
            else File.Copy(sourceFile!, output);
            Push(); InsertSticker("media/decor/" + name, sourceFile is null ? L.Text("粘贴图片") : Path.GetFileName(sourceFile)); Commit();
        }
        catch (Exception error)
        {
            TimeLabel.Text = L.Text("粘贴图片失败：") + error.Message;
        }
    }

    void BuildDecorations()
    {
        var selectedSoft = _project.Canvas.SoftRegions.FirstOrDefault(x => x.Id == _selectedDecoration);
        var selectedSticker = _project.Canvas.Stickers.Any(x => x.Id == _selectedDecoration);
        InspectorHeader(selectedSoft is null ? selectedSticker ? L.Text("贴图") : L.Text("效果") : selectedSoft.Style == "spotlight" ? L.Text("聚光") : L.Text("柔化"),
            selectedSoft is null ? selectedSticker ? L.Text("所选图片") : L.Text("项目片段") : L.Text("所选区域"));
        var section = selectedSoft is null && !selectedSticker ? Disclosure(L.Text("贴图、柔化与聚光")) : Inspector;
        var management = selectedSoft is null && !selectedSticker ? section : Disclosure(L.Text("管理效果"));
        var managementContainer = management.Parent as Expander;
        if ((selectedSoft is not null || selectedSticker) && managementContainer is not null) Inspector.Children.Remove(managementContainer);
        var add = new WrapPanel();
        add.Children.Add(DecorationButton(L.Text("＋贴图"), AddSticker));
        foreach (var (title, style) in new[] { (L.Text("＋柔化"), "blur"), (L.Text("＋聚光"), "spotlight") })
            add.Children.Add(DecorationButton(title, () => BeginRegionDrawing(style)));
        // Mac 运动轨道：在播放头插入屏幕 / 摄像头运动片段。
        add.Children.Add(DecorationButton(L.Text("＋屏幕运动"), AddScreenMotion));
        add.Children.Add(DecorationButton(L.Text("＋摄像头运动"), () =>
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
        management.Children.Add(add);
        if (_regionDrawingStyle is not null)
        {
            section.Children.Add(Caption(L.Text("在预览画面上拖动框选。选区效果暂时隐藏，方便看清目标；Esc 取消。")));
            section.Children.Add(DecorationButton(L.Text("取消框选"), () => { CancelRegionEditing(); RebuildInspector(); ApplyZoomVisual(); }));
            return;
        }
        var list = new ComboBox { Margin = new Thickness(0, 6, 0, 6), MinWidth = 160 };
        foreach (var (image, i) in _project.Canvas.Stickers.Select((x, i) => (x, i)))
            list.Items.Add(new ComboBoxItem { Content = L.Text("贴图") + $" {i + 1} · {image.Name}", Tag = image.Id });
        foreach (var (region, i) in _project.Canvas.SoftRegions.Select((x, i) => (x, i)))
            list.Items.Add(new ComboBoxItem { Content = L.Text(region.Style == "spotlight" ? "聚光" : "柔化") + $" {i + 1}", Tag = region.Id });
        foreach (var (motion, i) in _project.ScreenMotions.Select((x, i) => (x, i)))
            list.Items.Add(new ComboBoxItem { Content = L.Text("屏幕运动") + $" {i + 1}", Tag = motion.Id });
        foreach (var (motion, i) in _project.CameraMotions.Select((x, i) => (x, i)))
            list.Items.Add(new ComboBoxItem { Content = L.Text("摄像头运动") + $" {i + 1}", Tag = motion.Id });
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
        if (list.Items.Count > 0) management.Children.Add(list);
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
            section.Children.Add(Caption(L.Text("选择柔化或聚光后，在预览里框选目标。已有区域可直接点击、拖动和缩放。")));
            return;
        }
        var commands = new WrapPanel();
        commands.Children.Add(DecorationButton(L.Text("删除效果"), () => { Push(); DeleteDecoration(_selectedDecoration!); _selectedDecoration = null; Commit(); }));
        if (sticker is not null)
        {
            commands.Children.Add(DecorationButton(L.Text("换图…"), () => PickDecoration((path, name) => { sticker.Path = path; sticker.Name = name; })));
            foreach (var (title, delta) in new[] { (L.Text("上移图层"), 1), (L.Text("下移图层"), -1) })
                commands.Children.Add(DecorationButton(title, () =>
                {
                    var index = _project.Canvas.Stickers.IndexOf(sticker);
                    var target = Math.Clamp(index + delta, 0, _project.Canvas.Stickers.Count - 1);
                    if (target == index) return;
                    Push(); _project.Canvas.Stickers.RemoveAt(index); _project.Canvas.Stickers.Insert(target, sticker); Commit();
                }));
        }
        management.Children.Add(commands);
        void Control(string title, double min, double max, double value, Action<double> assign, bool refresh = false)
        { section.Children.Add(ValueSlider(title, min, max, value, assign, title == L.Text("不透明度") || title == L.Text("宽度") ? Percent : title == L.Text("旋转角度") ? v => $"{v:0}°" : null)); }
        if (sticker is not null)
        {
            management.Children.Add(Caption(L.Text("拖动摆放 · 边角缩放 · 顶部手柄旋转")));
            var onlyBackground = new CheckBox { Content = L.Text("仅背景"), IsChecked = sticker.HidesScreen && sticker.HidesCamera, Style = (Style)FindResource("SwitchCheckBox"), Margin = new Thickness(0, 6, 0, 6) };
            onlyBackground.Click += (_, _) => { Push(); sticker.HidesScreen = sticker.HidesCamera = onlyBackground.IsChecked == true; Commit(); };
            management.Children.Add(onlyBackground);
            management.Children.Add(DecorationButton(L.Text("适应画布"), () =>
            {
                if (!_stickerImages.TryGetValue(sticker.Id, out var holder) || holder.Child is not Image image || image.Source is not BitmapSource bitmap) return;
                Push(); sticker.X = sticker.Y = .5; sticker.Rotation = 0;
                sticker.Width = Math.Min(1, PreviewCanvas.Height / PreviewCanvas.Width * bitmap.PixelWidth / bitmap.PixelHeight);
                Commit();
            }));
            var tips = Caption(L.Text("Alt + 点击切换重叠图层；Tab 切图层。"));
            tips.ToolTip = L.Text("旋转时按住 Shift 吸附 15°；方向键移动 1 像素，Shift + 方向键移动 10 像素。"); management.Children.Add(tips);
            BuildStickerNumbers(management, sticker);
            section.Children.Add(SectionTitle(L.Text("贴图布局")));
            section.Children.Add(VectorPad(L.Text("贴图位置"), sticker.X, sticker.Y, (x, y) => { sticker.X = x; sticker.Y = y; }, headingTitle: L.Text("位置")));
            Control(L.Text("宽度"), .02, 1, sticker.Width, x => sticker.Width = x);
            Control(L.Text("旋转角度"), -180, 180, sticker.Rotation, x => sticker.Rotation = x);
            Control(L.Text("不透明度"), 0, 1, sticker.Opacity, x => sticker.Opacity = x);

            section.Children.Add(SectionTitle(L.Text("外观")));
            var body = new StackPanel(); section.Children.Add(body);
            // Mac 贴纸外观：圆角 / 描边 / 投影。
            void DetailControl(string title, double min, double max, double value, Action<double> assign)
            { body.Children.Add(ValueSlider(title, min, max, value, assign)); }
            DetailControl(L.Text("圆角"), 0, 120, sticker.CornerRadius, x => sticker.CornerRadius = x);
            DetailControl(L.Text("描边宽度"), 0, 40, sticker.BorderWidth, x => sticker.BorderWidth = x);
            var borderRow = new WrapPanel();
            foreach (var (name, hex) in new[] { (L.Text("白"), "#FFFFFF"), (L.Text("黑"), "#000000"), (L.Text("黄"), "#FFD60A"), (L.Text("蓝"), "#0A84FF"), (L.Text("红"), "#FF453A") })
            {
                var swatch = new Button
                {
                    Content = name, Tag = hex, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2),
                    Style = (Style)FindResource("ChipButton"),
                };
                ControlAppearance.SelectChip(swatch, string.Equals(sticker.BorderColor, hex, StringComparison.OrdinalIgnoreCase));
                swatch.Click += (_, _) => { Push(); sticker.BorderColor = hex; Commit(); };
                borderRow.Children.Add(swatch);
            }
            body.Children.Add(Caption(L.Text("描边颜色"))); body.Children.Add(borderRow);
            DetailControl(L.Text("阴影强度"), 0, 1, sticker.ShadowOpacity, x => sticker.ShadowOpacity = x);
            DetailControl(L.Text("阴影半径"), 0, 80, sticker.ShadowRadius, x => sticker.ShadowRadius = x);
            DetailControl(L.Text("阴影偏移 X"), -40, 40, sticker.ShadowOffsetX, x => sticker.ShadowOffsetX = x);
            DetailControl(L.Text("阴影偏移 Y"), -40, 40, sticker.ShadowOffsetY, x => sticker.ShadowOffsetY = x);
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
                    ControlAppearance.SelectChip(chip, selected);
                    chip.Click += (_, _) => { Push(); assign(value); Commit(); };
                    row.Children.Add(chip);
                }
                body.Children.Add(row);
            }
            (string, string?)[] animPresets = [(L.Text("无"), "none"), (L.Text("淡入"), "fade"), (L.Text("弹出"), "pop"),
                (L.Text("左滑入"), "slideLeft"), (L.Text("右滑入"), "slideRight"), (L.Text("上滑入"), "slideUp"), (L.Text("下滑入"), "slideDown"),
                (L.Text("左上"), "slideTopLeft"), (L.Text("右上"), "slideTopRight"), (L.Text("左下"), "slideBottomLeft"), (L.Text("右下"), "slideBottomRight")];
            Chips(L.Text("进入动画"), animPresets, sticker.Animation, x => sticker.Animation = x ?? "pop");
            Chips(L.Text("退出动画"), [(L.Text("自动"), null), .. animPresets], sticker.ExitAnimation, x => sticker.ExitAnimation = x);
            Chips(L.Text("动画曲线"), [(L.Text("平缓"), "smooth"), (L.Text("迅速"), "swift"), (L.Text("柔和"), "gentle"), (L.Text("线性"), "linear")],
                sticker.AnimationCurve, x => sticker.AnimationCurve = x ?? "swift");
            DetailControl(L.Text("进入时长"), 0, 2, sticker.EnterDuration, x => sticker.EnterDuration = x);
            DetailControl(L.Text("退出时长"), 0, 2, sticker.ExitDuration, x => sticker.ExitDuration = x);
            DetailControl(L.Text("背景虚化"), 0, 60, sticker.BackdropBlur, x => sticker.BackdropBlur = x);
            void Toggle(string title, bool value, Action<bool> assign)
            {
                var box = new CheckBox { Content = title, IsChecked = value, Style = (Style)FindResource("SwitchCheckBox"), Margin = new Thickness(0, 6, 0, 2) };
                box.Click += (_, _) => { Push(); assign(box.IsChecked == true); Commit(); };
                body.Children.Add(box);
            }
            Toggle(L.Text("虚化处理含摄像头"), sticker.BackdropBlurIncludesCamera, x => sticker.BackdropBlurIncludesCamera = x);
            Toggle(L.Text("显示时隐藏屏幕内容"), sticker.HidesScreen, x => sticker.HidesScreen = x);
            Toggle(L.Text("显示时隐藏摄像头"), sticker.HidesCamera, x => sticker.HidesCamera = x);
        }
        if (soft is not null)
        {
            section.Children.Add(SectionTitle(L.Text("柔化效果")));
            section.Children.Add(Segments([(L.Text("柔化"), "blur"), (L.Text("聚光"), "spotlight")], soft.Style,
                key => { Push(); soft.Style = key; Commit(); }));
            section.Children.Add(ValueSlider(L.Text("强度"), 0, 1, soft.Style == "spotlight" ? soft.Dimming : soft.Strength, v => { if (soft.Style == "spotlight") soft.Dimming = v; else soft.Strength = v; }, Percent));
            section.Children.Add(ValueSlider(L.Text("圆角"), 0, .5, soft.Corner, v => soft.Corner = v, Percent));
            section.Children.Add(Caption(L.Text("过渡")));
            section.Children.Add(Segments([(L.Text("无"), "none"), (L.Text("线性"), "linear"), (L.Text("平滑"), "smooth")], soft.Timing.Transition,
                key => { Push(); soft.Timing.Transition = key; Commit(); }));
            commands.Children.Insert(0, DecorationButton(L.Text("重画区域"), () => BeginRegionDrawing(soft.Style, soft.Id)));
            var guidance = Caption(L.Text("拖动移动 · 拖边角缩放 · 方向键微调"));
            guidance.ToolTip = L.Text("方向键移动 1 像素，Shift + 方向键移动 10 像素。暂停时完整显示所选效果，播放时按时间线显示。");
            management.Children.Add(guidance);
            section.Children.Add(SectionTitle(L.Text("位置与大小")));
            section.Children.Add(VectorPad(L.Text("选区位置"), soft.X + soft.Width / 2, soft.Y + soft.Height / 2,
                (x, y) => { soft.X = Math.Clamp(x - soft.Width / 2, 0, 1 - soft.Width); soft.Y = Math.Clamp(y - soft.Height / 2, 0, 1 - soft.Height); }, headingTitle: L.Text("位置"),
                readPoint: () => new Point(soft.X + soft.Width / 2, soft.Y + soft.Height / 2)));
            BuildRegionNumbers(management, soft);
            var details = management;
            var enabled = new CheckBox { Content = L.Text("启用效果"), IsChecked = soft.Enabled, Style = (Style)FindResource("SwitchCheckBox"), Margin = new Thickness(0, 6, 0, 2) };
            enabled.Click += (_, _) => { Push(); soft.Enabled = enabled.IsChecked == true; Commit(); };
            details.Children.Add(enabled);
            var source = new CheckBox { Content = L.Text("跟随原画面缩放与透视"), IsChecked = soft.CoordinateSpace == "source", Margin = new Thickness(0, 6, 0, 2) };
            source.ToolTip = L.Text("切换时按当前可见范围换算位置。画布区域固定在成片中，原画面区域跟随录制画面。");
            source.Click += (_, _) => ChangeRegionCoordinates(soft, source.IsChecked == true ? "source" : "canvas");
            details.Children.Add(source);
            RegionValueControl(details, L.Text("圆角"), 0, .5, soft.Corner, x => soft.Corner = x);
        }
        BuildEffectTiming(L.Text("所选效果"), sticker?.Timing ?? soft!.Timing);
        if (managementContainer is not null && managementContainer.Parent is null) Inspector.Children.Add(managementContainer);
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
        else if (_project.ScreenMotions.FirstOrDefault(x => x.Id == _selectedDecoration) is { } screen)
            _decorationClipboard = new(null, null, ScenePreset.Clone(screen));
        else if (_project.CameraMotions.FirstOrDefault(x => x.Id == _selectedDecoration) is { } camera)
            _decorationClipboard = new(null, null, null, ScenePreset.Clone(camera));
        else return false;
        _zoomClipboard = null; _effectClipboard = null;
        TimeLabel.Text = L.Text("已复制效果；Ctrl+V 添加副本，Ctrl+Shift+V 只粘属性。");
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
        if (source.ScreenMotion is not null || source.CameraMotion is not null)
        {
            PasteMotion(source, includeTiming);
            return;
        }
        var targetSticker = _project.Canvas.Stickers.FirstOrDefault(x => x.Id == _selectedDecoration);
        var targetRegion = _project.Canvas.SoftRegions.FirstOrDefault(x => x.Id == _selectedDecoration);
        if (!includeTiming && (source.Sticker is not null ? targetSticker is null : targetRegion is null))
        { TimeLabel.Text = L.Text("请先选中同类型效果，再粘贴属性。"); return; }
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

    void PasteMotion(DecorationClipboard source, bool includeTiming)
    {
        var screen = source.ScreenMotion is not null;
        var targetScreen = _project.ScreenMotions.FirstOrDefault(x => x.Id == _selectedDecoration);
        var targetCamera = _project.CameraMotions.FirstOrDefault(x => x.Id == _selectedDecoration);
        if (!includeTiming && (screen ? targetScreen is null : targetCamera is null))
        { TimeLabel.Text = L.Text("请先选中同类型运动，再粘贴属性。"); return; }
        var total = TimelineMath.Duration(_project);
        var start = Math.Clamp(Timeline.PasteTime, 0, Math.Max(0, total - .05));
        var original = source.ScreenMotion?.Timing ?? source.CameraMotion!.Timing;
        if (includeTiming && Timeline.SnapEnabled)
        {
            var boundaries = screen ? _project.ScreenMotions.SelectMany(c => new[] { c.Timing.StartTime, c.Timing.EndTime })
                : _project.CameraMotions.SelectMany(c => new[] { c.Timing.StartTime, c.Timing.EndTime });
            var closest = boundaries.OrderBy(t => Math.Abs(t - start)).FirstOrDefault(start);
            if (Math.Abs(closest - start) <= Timeline.SnapThreshold) start = Math.Clamp(closest, 0, Math.Max(0, total - .05));
        }
        var timing = ScenePreset.Clone(original);
        var target = targetScreen?.Timing ?? targetCamera?.Timing;
        timing.Id = includeTiming ? Guid.NewGuid().ToString("N") : target!.Id;
        timing.StartTime = includeTiming ? start : target!.StartTime;
        timing.Duration = includeTiming ? Math.Min(Math.Max(.05, original.Duration), total - start) : target!.Duration;
        Push();
        if (screen)
        {
            var copy = ScenePreset.Clone(source.ScreenMotion!); copy.Timing = timing;
            copy.Id = includeTiming ? Guid.NewGuid().ToString("N") : targetScreen!.Id;
            copy.GroupId = includeTiming ? null : targetScreen!.GroupId;
            if (includeTiming) _project.ScreenMotions.Add(copy);
            else _project.ScreenMotions[_project.ScreenMotions.IndexOf(targetScreen!)] = copy;
            _selectedDecoration = copy.Id; ApplyTrackFlags(TrackFlags | 2);
        }
        else
        {
            var copy = ScenePreset.Clone(source.CameraMotion!); copy.Timing = timing;
            copy.Id = includeTiming ? Guid.NewGuid().ToString("N") : targetCamera!.Id;
            copy.GroupId = includeTiming ? null : targetCamera!.GroupId;
            if (includeTiming) _project.CameraMotions.Add(copy);
            else _project.CameraMotions[_project.CameraMotions.IndexOf(targetCamera!)] = copy;
            _selectedDecoration = copy.Id; ApplyTrackFlags(TrackFlags | 4);
        }
        _selectedClip = _selectedZoom = null; _tool = "effects"; Commit();
    }
}

public partial class EditorWindow
{
    /// <summary>Mac 运动片段编辑：时间（起止/进入/回落/曲线）+ 目标构图。</summary>
    void BuildMotionInspector(Panel section, ScreenMotionClip? screen, CameraMotionClip? camera)
    {
        if (screen is not null)
        {
            section.Children.Add(SectionTitle(L.Text("运动类型")));
            section.Children.Add(Segments([(L.Text("缩放"), "zoom"), (L.Text("屏幕 3D"), "screen3d")], "screen3d",
                SelectMotionMode));
            BuildScreenPoseControls(section, screen);
            section = Disclosure(L.Text("动画时间"));
        }
        var timing = (screen?.Timing ?? camera!.Timing)!;
        section.Children.Add(Caption(screen is not null ? L.Text("屏幕运动片段 · 轨道可拖动、两端可拉长") : L.Text("摄像头运动片段 · 轨道可拖动、两端可拉长")));
        var commands = new WrapPanel();
        commands.Children.Add(DecorationButton(L.Text("删除运动片段"), () =>
        {
            Push(); DeleteDecoration(_selectedDecoration!); _selectedDecoration = null; Commit();
        }));
        commands.Children.Add(DecorationButton(L.Text("从起点播放"), () => _player.Seek(timing.StartTime, play: true)));
        if (!string.IsNullOrEmpty(screen?.GroupId ?? camera?.GroupId))
        {
            commands.Children.Add(DecorationButton(L.Text("解除布局联动"), () =>
            {
                Push();
                if (screen is not null) screen.GroupId = null; else camera!.GroupId = null;
                Commit();
            }));
        }
        section.Children.Add(commands);

        void Control(string title, double min, double max, double value, Action<double> assign)
        { section.Children.Add(MotionSlider(title, min, max, value, assign)); }

        var duration = TimelineMath.Duration(_project);
        section.Children.Add(Caption(L.Text("时间")));
        var motionId = screen?.Id ?? camera!.Id;
        Control(L.Text("起点"), 0, Math.Max(0.1, duration - .05), timing.StartTime,
            x => MotionTimingEdits.SetRange(_project, motionId, x, timing.Duration));
        Control(L.Text("片段时长（含进入）"), .05, Math.Max(.5, duration), timing.Duration,
            x => MotionTimingEdits.SetRange(_project, motionId, timing.StartTime, x));
        Control(L.Text("进入时长"), 0, 2, timing.LeadInDuration, x => { timing.LeadInDuration = x; timing.PreferredLeadInDuration = null; });
        Control(L.Text("回落时长"), 0, 2, timing.ReturnDuration, x => { timing.ReturnDuration = x; timing.PreferredReturnDuration = null; });

        var easings = new WrapPanel();
        foreach (var (name, key) in new[] { (L.Text("线性"), "linear"), (L.Text("二次"), "quadratic"), (L.Text("三次"), "cubic"), (L.Text("五次"), "quintic"), (L.Text("弹簧"), "spring"), (L.Text("自定义"), "custom") })
        {
            var chip = new Button { Content = name, Tag = key, Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 4) };
            ControlAppearance.SelectChip(chip, timing.Easing == key);
            chip.Click += (_, _) => { Push(); timing.Easing = key; Commit(); };
            easings.Children.Add(chip);
        }
        section.Children.Add(Caption(L.Text("曲线")));
        section.Children.Add(easings);
        if (timing.Easing == "custom")
        {
            timing.CustomCurve ??= [.65, 0, .35, 1];
            Control(L.Text("曲线 X1"), 0, 1, timing.CustomCurve[0], x => timing.CustomCurve[0] = x);
            Control(L.Text("曲线 Y1"), -2, 2, timing.CustomCurve[1], x => timing.CustomCurve[1] = x);
            Control(L.Text("曲线 X2"), 0, 1, timing.CustomCurve[2], x => timing.CustomCurve[2] = x);
            Control(L.Text("曲线 Y2"), -2, 2, timing.CustomCurve[3], x => timing.CustomCurve[3] = x);
        }

        if (screen is not null)
        {
            return;
        }
        else
        {
            var layouts = new WrapPanel();
            foreach (var (name, key) in new[] { (L.Text("方形"), "square"), (L.Text("圆形"), "circle"), (L.Text("横向"), "horizontal"), (L.Text("竖向"), "vertical"), (L.Text("原比例"), "original"), (L.Text("全屏"), "fullscreen") })
            {
                var chip = new Button { Content = name, Tag = key, Style = (Style)FindResource("ChipButton"), Margin = new Thickness(0, 0, 4, 4) };
                ControlAppearance.SelectChip(chip, camera!.Target.Layout == key);
                chip.Click += (_, _) => { Push(); camera.Target.Layout = key; Commit(); };
                layouts.Children.Add(chip);
            }
            section.Children.Add(Caption(L.Text("目标形状")));
            section.Children.Add(layouts);
            Control(L.Text("水平位置"), 0, 1, camera!.Target.X, x => camera.Target.X = x);
            Control(L.Text("垂直位置"), 0, 1, camera.Target.Y, x => camera.Target.Y = x);
            Control(L.Text("大小"), (camera.Target.Docked ?? _project.Camera.Docked) ? .01 : .08, 1, camera.Target.Size, x => camera.Target.Size = x);
            Control(L.Text("圆角"), 0, 1, camera.Target.Roundness, x => camera.Target.Roundness = x);
            Control(L.Text("不透明度（0=隐藏）"), 0, 1, camera.Target.Opacity, x => camera.Target.Opacity = x);
        }
    }
}
