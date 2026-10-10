using DogSC.Core;
using System.Windows.Shapes;

namespace DogSC;

public partial class EditorWindow
{
    double _newMotionTransition = .7;

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
        var clip = TimelineEdits.PrepareScreenMotion(_project, _player.Time, _newMotionTransition);
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
        InspectorHeader("运镜", "屏幕 3D");
        Inspector.Children.Add(Caption("运动类型"));
        Inspector.Children.Add(Segments([("缩放", "zoom"), ("屏幕 3D", "screen3d")], "screen3d", SelectMotionMode));
        Inspector.Children.Add(Caption("在播放头加入位置、大小与透视动画，可与缩放叠加。"));
        var add = DecorationButton("在播放头添加屏幕 3D", AddScreenMotion);
        add.Style = (Style)FindResource("PrimaryButton"); Inspector.Children.Add(add);
        var transition = Disclosure("新动画过渡", () => Seconds(_newMotionTransition));
        transition.Children.Add(ValueSlider("过渡时长", 0, 5, _newMotionTransition, v => _newMotionTransition = v, Seconds));
    }

    UIElement VectorPad(string title, double x, double y, Action<double, double> set, bool tilt = false)
    {
        const double w = 200, h = 140;
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        var pad = new Canvas { Width = w, Height = h, ClipToBounds = true,
            Clip = new RectangleGeometry(new Rect(0, 0, w, h), 10, 10) };
        System.Windows.Automation.AutomationProperties.SetName(pad, title);
        pad.SetResourceReference(Panel.BackgroundProperty, "ControlBrush");
        var guide = (Brush)FindResource("LineBrush");
        pad.Children.Add(new Line { X1 = w / 2, X2 = w / 2, Y1 = 0, Y2 = h, Stroke = guide, StrokeDashArray = [2, 3] });
        pad.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h / 2, Y2 = h / 2, Stroke = guide, StrokeDashArray = [2, 3] });
        var plane = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(30, 100, 100, 100)), Stroke = guide, StrokeThickness = 1 };
        if (tilt) pad.Children.Add(plane);
        var dot = new Ellipse { Width = 14, Height = 14 }; dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
        pad.Children.Add(dot);
        var vx = new TextBlock(); var vy = new TextBlock();
        var values = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 8, 0, 0) };
        values.Children.Add(CoordinateValue("X", vx)); values.Children.Add(CoordinateValue("Y", vy));
        void Paint()
        {
            Canvas.SetLeft(dot, x * (w - 14)); Canvas.SetTop(dot, y * (h - 14));
            vx.Text = tilt ? $"{((.5 - y) * 90):0.0}°" : Percent(x);
            vy.Text = tilt ? $"{((x - .5) * 90):0.0}°" : Percent(y);
            if (tilt)
            {
                var points = PerspectiveGeometry.Corners(160, 85, (.5 - y) * 90, (x - .5) * 90);
                plane.Points = new PointCollection(new[] { 0, 1, 3, 2 }.Select(i => new Point(points[i].X + 20, points[i].Y + 27)));
            }
        }
        void Move(Point p)
        {
            x = Math.Clamp((p.X - 7) / (w - 14), 0, 1); y = Math.Clamp((p.Y - 7) / (h - 14), 0, 1);
            set(x, y); Paint(); Commit(rebuild: false);
        }
        pad.MouseLeftButtonDown += (_, e) => { Push(); pad.CaptureMouse(); Move(e.GetPosition(pad)); e.Handled = true; };
        pad.MouseMove += (_, e) => { if (pad.IsMouseCaptured) Move(e.GetPosition(pad)); };
        pad.MouseLeftButtonUp += (_, _) => { pad.ReleaseMouseCapture(); FlushPendingSave(); };
        Paint(); panel.Children.Add(pad); panel.Children.Add(values); return panel;
    }

    void BuildScreenPoseControls(Panel section, ScreenMotionClip screen)
    {
        var s = screen.Target;
        section.Children.Add(SectionTitle("构图"));
        section.Children.Add(PadHeading("缩放锚点", "⌖ 居中", () => { Push(); s.X = s.Y = .5; Commit(); }));
        section.Children.Add(new TextBlock { Text = "放大时，画面从锚点向外展开；缩小时向锚点收拢。", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2), Foreground = (Brush)FindResource("InkSecondaryBrush") });
        section.Children.Add(VectorPad("屏幕 3D 构图", s.X, s.Y, (x, y) => { s.X = x; s.Y = y; }));
        section.Children.Add(InlineValueSlider("目标大小", .25, 4, s.Scale, v => s.Scale = v, Times));
        section.Children.Add(SectionTitle("空间姿态"));
        section.Children.Add(PadHeading("3D 倾斜", "⟳ 重置", () => { Push(); s.RotationX = s.RotationY = 0; Commit(); }));
        section.Children.Add(new TextBlock { Text = "上下控制 X · 左右控制 Y", FontSize = 11, Foreground = (Brush)FindResource("InkSecondaryBrush") });
        section.Children.Add(VectorPad("屏幕 3D 倾斜", .5 + s.RotationY / 90, .5 - s.RotationX / 90,
            (x, y) => { s.RotationY = (x - .5) * 90; s.RotationX = (.5 - y) * 90; }, tilt: true));
        var rotation = Disclosure("Z 旋转与透视", () => $"Z {s.RotationZ:0.0}° · 透视 {s.Perspective:0.00}");
        rotation.Children.Add(InlineValueSlider("平面旋转", -180, 180, s.RotationZ, v => s.RotationZ = v, v => $"{v:0.0}°"));
        rotation.Children.Add(InlineValueSlider("透视", 0, 2, s.Perspective, v => s.Perspective = v));
        AddToggle(section, "线性柔化", screen.LinearBlur, v => { screen.LinearBlur = v; RebuildInspector(); });
        if (screen.LinearBlur) BuildLinearFocusControls(section, screen);
        var anchor = Disclosure("聚焦锚点");
        anchor.Children.Add(VectorPad("屏幕投影锚点", s.AnchorX, s.AnchorY, (x, y) => { s.AnchorX = x; s.AnchorY = y; }));
    }

    void BuildLinearFocusControls(Panel section, ScreenMotionClip clip)
    {
        section.Children.Add(Caption("清晰带 · 向两侧逐渐柔化"));
        section.Children.Add(ValueSlider("清晰范围", .1, 1, clip.LinearBlurSize, v => clip.LinearBlurSize = v, Percent));
        section.Children.Add(ValueSlider("柔化强度", 0, 1, clip.LinearBlurStrength, v => clip.LinearBlurStrength = v, Percent));
        section.Children.Add(ValueSlider("羽化", 0, 1, clip.LinearBlurSoftness, v => clip.LinearBlurSoftness = v, Percent));
        section.Children.Add(ValueSlider("方向", 0, 360, clip.LinearBlurAngle, v => clip.LinearBlurAngle = v, v => $"{v:0}°"));
    }

    void UpdateLinearFocus()
    {
        var selected = !_player.IsPlaying ? _project.ScreenMotions.FirstOrDefault(c => c.Id == _selectedDecoration && c.LinearBlur && c.LinearBlurStrength > .001) : null;
        var clip = selected ?? _project.ScreenMotions.LastOrDefault(c => c.LinearBlur && c.LinearBlurStrength > .001 && _player.Time >= c.Timing.StartTime && LinearFocus.Progress(c, _player.Time) > .001);
        LinearFocusOverlay.Visibility = clip is null ? Visibility.Collapsed : Visibility.Visible;
        if (clip is null) return;
        var width = FrameHost.Width; var height = FrameHost.Height;
        if (LinearFocusOverlay.Fill is not VisualBrush focusBrush)
            LinearFocusOverlay.Fill = focusBrush = new VisualBrush(SourceViewport) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute };
        focusBrush.Viewbox = new Rect(0, 0, width, height);
        LinearFocusOverlay.Width = width; LinearFocusOverlay.Height = height;
        LinearFocusOverlay.RenderTransform = Transform.Identity;
        LinearFocusOverlay.Opacity = selected is not null ? 1 : LinearFocus.Progress(clip, _player.Time);
        LinearFocusBlur.Radius = (clip.LinearBlurStrength * 36) * _pvFrameScale;
        var angle = clip.LinearBlurAngle * Math.PI / 180; var dx = -Math.Sin(angle); var dy = Math.Cos(angle);
        var span = Math.Max(width, height); var shortEdge = Math.Min(width, height);
        var half = shortEdge * clip.LinearBlurSize / 2 / (2 * span);
        var feather = Math.Max(1, shortEdge * clip.LinearBlurSoftness * .4) / (2 * span);
        LinearFocusOverlay.OpacityMask = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new(width / 2 - dx * span, height / 2 - dy * span),
            EndPoint = new(width / 2 + dx * span, height / 2 + dy * span),
            GradientStops = new() { new(Colors.White, .5 - half - feather), new(Colors.Transparent, .5 - half),
                new(Colors.Transparent, .5 + half), new(Colors.White, .5 + half + feather) },
        };
    }
}
