using DogSC.Core;
using System.Windows.Shapes;

namespace DogSC;

public partial class EditorWindow
{
    void BuildCameraLayoutAnimations()
    {
        var panel = Disclosure(L.Text("播放头布局动画"));
        CameraMotionState CurrentCamera() => ScenePreset.Clone(_project.CameraMotions.FirstOrDefault(c =>
            _player.Time >= c.Timing.StartTime && _player.Time < c.Timing.EndTime)?.Target ?? new CameraMotionState
        {
            Layout = _project.Camera.Shape == "none" ? "square" : _project.Camera.Shape,
            X = _project.Camera.CenterX, Y = _project.Camera.CenterY, Size = _project.Camera.Size,
            Roundness = _project.Camera.Corner * 2, Opacity = _project.Camera.Hidden ? 0 : 1,
        });
        void Apply(CameraMotionState target, ScreenMotionState? screen = null)
            => ApplyCameraLayout(p => LayoutMotionEdits.Apply(p, _player.Time, target, screen), screen is not null);
        var buttons = new WrapPanel();
        buttons.Children.Add(DecorationButton(L.Text("当前"), () => Apply(CurrentCamera())));
        buttons.Children.Add(DecorationButton(L.Text("全屏"), () => Apply(new() { Layout = "fullscreen", X = .5, Y = .5, Size = 1 })));
        buttons.Children.Add(DecorationButton(L.Text("画中画"), () => Apply(new()
        {
            Layout = _project.Camera.Shape == "none" ? "square" : _project.Camera.Shape,
            X = _project.Camera.CenterX, Y = _project.Camera.CenterY, Size = _project.Camera.Size, Roundness = _project.Camera.Corner * 2,
        })));
        buttons.Children.Add(DecorationButton(L.Text("屏幕与摄像头联动"), () => Apply(CurrentCamera(),
            _project.ScreenMotions.FirstOrDefault(c => _player.Time >= c.Timing.StartTime && _player.Time < c.Timing.EndTime)?.Target
                ?? new ScreenMotionState())));
        panel.Children.Add(buttons);
        panel.Children.Add(Caption(L.Text("在播放头创建布局动画；已有动画直接更新。联动后拖动或调整一侧时长，另一侧跟随。")));
        var presets = new WrapPanel();
        presets.Children.Add(DecorationButton(L.Text("保存摄像头布局预设"), ShowSaveLayoutPreset));
        var mine = DecorationButton(L.Text("我的摄像头布局预设"), () => { });
        mine.IsEnabled = SettingsStore.Current.LayoutPresets.Count > 0;
        mine.Click += (_, _) =>
        {
            var menu = BuildLayoutPresetMenu(); menu.PlacementTarget = mine; menu.IsOpen = true;
        };
        presets.Children.Add(mine); panel.Children.Add(presets);
    }

    void ApplyCameraLayout(Func<ProjectDocument, CameraMotionClip?> apply, bool paired)
    {
        _player.Pause(); _activeParameterReadout?.FinishEditing();
        var updated = ScenePreset.Clone(_project);
        var clip = apply(updated);
        if (clip is null) { TimeLabel.Text = L.Text("播放头附近没有空间添加布局动画，请先移动播放头。"); return; }
        Push(); _project = updated;
        ApplyTrackFlags(TrackFlags | 4 | (paired ? 2 : 0));
        _selectedDecoration = clip.Id; _selectedClip = _selectedZoom = null; _tool = "zoom";
        Commit();
    }

    void ApplySavedLayoutPreset(LayoutPreset preset) => ApplyCameraLayout(p => preset.Apply(p, _player.Time), paired: true);

    string? SaveLayoutPreset(string name)
    {
        var settings = SettingsStore.Current; var before = settings.LayoutPresets;
        var saved = LayoutPreset.Capture(_project, name);
        settings.LayoutPresets = before.Where(p => p.Name != saved.Name).Append(saved).ToList();
        try { SettingsStore.SaveOrThrow(); return null; }
        catch (Exception error) { settings.LayoutPresets = before; return L.Text("预设未保存：") + error.Message; }
    }

    ContextMenu BuildLayoutPresetMenu()
    {
        var menu = new ContextMenu();
        foreach (var preset in SettingsStore.Current.LayoutPresets)
        {
            var item = new MenuItem { Header = preset.Name, Tag = preset };
            item.Click += (_, _) => ApplySavedLayoutPreset(preset); menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        foreach (var preset in SettingsStore.Current.LayoutPresets)
        {
            var item = new MenuItem { Header = L.Format("删除“{0}”", preset.Name), Tag = preset };
            item.Click += (_, _) =>
            {
                var settings = SettingsStore.Current; var before = settings.LayoutPresets;
                settings.LayoutPresets = before.Where(p => p.Name != preset.Name).ToList();
                try { SettingsStore.SaveOrThrow(); }
                catch (Exception error) { settings.LayoutPresets = before; TimeLabel.Text = L.Text("预设未删除：") + error.Message; return; }
                RebuildInspector();
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    LayoutPresetSavePanel CreateLayoutPresetSavePanel() => new(
        name => SettingsStore.Current.LayoutPresets.Any(p => p.Name == name), SaveLayoutPreset);

    void ShowSaveLayoutPreset()
    {
        _player.Pause(); _activeParameterReadout?.FinishEditing();
        var dialog = new Window { Title = L.Text("保存摄像头布局预设"), SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = CreateLayoutPresetSavePanel(); dialog.Content = panel;
        panel.CancelRequested += dialog.Close; panel.Saved += () => { dialog.Close(); RebuildInspector(); };
        dialog.Loaded += (_, _) => { WindowPlacement.KeepInWorkArea(dialog, center: true); FluidMotion.Entrance(panel); };
        ModalShade.Visibility = Visibility.Visible;
        try { dialog.ShowDialog(); } finally { ModalShade.Visibility = Visibility.Collapsed; }
    }

    void SelectMotionMode(string key)
    {
        _motionMode = key; _selectedDecoration = null; _selectedZoom = null;
        if (key == "zoom") _selectedZoom = _project.Zooms.LastOrDefault(z => z.Start <= _player.Time && z.Start + z.Duration >= _player.Time)?.Id;
        else _selectedDecoration = _project.ScreenMotions.LastOrDefault(c => c.Timing.StartTime <= _player.Time && c.Timing.EffectEndTime >= _player.Time)?.Id;
        _tool = "zoom"; RebuildInspector(); InspectorScroll.ScrollToTop(); SyncSelection(); ApplyZoomVisual();
    }

    void AddScreenMotion()
    {
        _player.Pause();
        var clip = TimelineEdits.PrepareScreenMotion(_project, _player.Time, fallbackStyle: SettingsStore.Current.RememberedScreenMotion);
        if (clip is null) return;
        ApplyTrackFlags(TrackFlags | 2);
        if (!_project.ScreenMotions.Contains(clip))
        {
            Push(); _project.ScreenMotions.Add(clip);
            Commit(rebuild: false);
        }
        SelectDecoration(clip.Id); InspectorScroll.ScrollToTop();
    }

    void BuildScreenMotionCreation()
    {
        InspectorHeader(L.Text("运镜"), L.Text("屏幕 3D"));
        Inspector.Children.Add(Caption(L.Text("运动类型")));
        Inspector.Children.Add(Segments([(L.Text("缩放"), "zoom"), (L.Text("屏幕 3D"), "screen3d")], "screen3d", SelectMotionMode));
        Inspector.Children.Add(Caption(L.Text("在播放头加入位置、大小与透视动画，可与缩放叠加。")));
        var add = DecorationButton(L.Text("在播放头添加屏幕 3D"), AddScreenMotion);
        add.Style = (Style)FindResource("PrimaryButton"); Inspector.Children.Add(add);
        var transition = Disclosure(L.Text("新动画过渡"), () => Seconds(_project.Motion.DefaultZoomTransitionDuration));
        transition.Children.Add(ValueSlider(L.Text("过渡时长"), 0, 5, _project.Motion.DefaultZoomTransitionDuration,
            v => _project.Motion.DefaultZoomTransitionDuration = v, Seconds));
    }

    UIElement VectorPad(string title, double x, double y, Action<double, double> set, bool tilt = false, string? headingTitle = null, string? detail = null,
        Func<Point>? readPoint = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        var pad = new EditorPadSurface(tilt);
        ParameterReadout? vx = null, vy = null;
        Button reset = null!;
        System.Windows.Automation.AutomationProperties.SetName(pad, title);
        var heading = PadHeading(headingTitle ?? title, tilt ? L.Text("归零") : L.Text("居中"), () => { Push(); x = y = tilt ? 0 : .5; set(x, y); Paint(); Commit(rebuild: false); });
        reset = (Button)heading.Children[0];
        System.Windows.Automation.AutomationProperties.SetName(reset, (tilt ? L.Text("归零 ") : L.Text("居中")) + title);
        heading.Margin = new Thickness(0, 0, 0, 9); panel.Children.Add(heading);
        if (detail is not null)
        {
            var text = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, FontSize = tilt ? 9.5 : 11,
                FontWeight = tilt ? FontWeights.Medium : FontWeights.Normal, Margin = new Thickness(0, tilt ? -7 : 0, 0, 9) };
            text.SetResourceReference(TextBlock.ForegroundProperty, tilt ? "PadDetailBrush" : "InkSecondaryBrush");
            panel.Children.Add(text);
        }
        Func<double, string> format = tilt ? v => $"{v:0.0}°" : Percent;
        vx = Parameter(title + (tilt ? L.Text(" X 角度") : L.Text(" X 坐标")), tilt ? -28 : 0, tilt ? 28 : 1, x,
            v => { x = v; set(x, y); Paint(); }, format);
        vy = Parameter(title + (tilt ? L.Text(" Y 角度") : L.Text(" Y 坐标")), tilt ? -32 : 0, tilt ? 32 : 1, y,
            v => { y = v; set(x, y); Paint(); }, format);
        var values = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 9, 0, 0) };
        values.Children.Add(CoordinateValue("X", vx)); values.Children.Add(CoordinateValue("Y", vy));
        void Paint()
        {
            if (readPoint is not null) { var point = readPoint(); x = point.X; y = point.Y; }
            pad.SetPoint(tilt ? .5 + y / 64 : x, tilt ? .5 - x / 56 : y);
            reset.IsEnabled = tilt ? Math.Abs(x) >= .0005 || Math.Abs(y) >= .0005
                : Math.Abs(x - .5) >= .0005 || Math.Abs(y - .5) >= .0005;
            if (vx is not null) vx.Value = x;
            if (vy is not null) vy.Value = y;
        }
        foreach (var readout in new[] { vx, vy })
        {
            readout.EditingStarted += () => pad.CoordinateEditing = true;
            readout.EditingFinished += _ => pad.CoordinateEditing = vx.IsEditing || vy.IsEditing;
        }
        pad.DragStarted += () => Push();
        pad.PointChanged += (px, py) => { x = tilt ? (.5 - py) * 56 : px; y = tilt ? (px - .5) * 64 : py; set(x, y); Paint(); Commit(rebuild: false); };
        pad.DragFinished += FlushPendingSave;
        Paint(); panel.Children.Add(pad); panel.Children.Add(values); return panel;
    }

    void BuildScreenPoseControls(Panel section, ScreenMotionClip screen)
    {
        var s = screen.Target;
        section.Children.Add(SectionTitle(L.Text("构图")));
        section.Children.Add(VectorPad(L.Text("屏幕 3D 构图"), s.X, s.Y, (x, y) => { s.X = x; s.Y = y; }, headingTitle: L.Text(s.CanvasPlacement ? "屏幕位置" : "缩放锚点"),
            detail: L.Text(s.CanvasPlacement ? "布局预设记录画布中的位置和大小，应用到其他项目时按当前画布适配。"
                : "放大时画面向锚点的反方向展开；缩小时会向锚点收拢，因此 1× 两侧的移动方向会自然相反。")));
        section.Children.Add(InlineValueSlider(L.Text("目标大小"), .25, 4, s.Scale, v => s.Scale = v, v => $"{v:0.00}×"));
        section.Children.Add(SectionTitle(L.Text("空间姿态")));
        section.Children.Add(VectorPad(L.Text("屏幕 3D 倾斜"), s.RotationX, s.RotationY,
            (x, y) => { s.RotationX = x; s.RotationY = y; }, tilt: true, headingTitle: L.Text("3D 倾斜"), detail: L.Text("上下控制 X · 左右控制 Y")));
        var rotation = Disclosure(L.Text("平面旋转与透视"), () => L.Format("Z {0:0.0}° · 透视 {1:0.00}", s.RotationZ, s.Perspective));
        rotation.Children.Add(InlineValueSlider(L.Text("平面旋转"), -30, 30, s.RotationZ, v => s.RotationZ = v, v => $"{v:0.0}°"));
        rotation.Children.Add(InlineValueSlider(L.Text("透视强度"), 0, 2, s.Perspective, v => s.Perspective = v, v => $"{v:0.00}"));
        BuildFocusControls(section, () => FocusTrack.EffectOf(screen), value => { screen.LinearBlur = false; screen.FocusEffect = value; }, linear: true);
        var anchor = Disclosure(L.Text("聚焦锚点"));
        anchor.Children.Add(VectorPad(L.Text("屏幕投影锚点"), s.AnchorX, s.AnchorY, (x, y) => { s.AnchorX = x; s.AnchorY = y; }));
    }

}
