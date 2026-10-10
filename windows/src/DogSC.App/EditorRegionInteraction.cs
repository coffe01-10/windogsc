using System.Globalization;
using System.Text.Json;
using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    string? _regionDrawingStyle, _regionRedrawId, _regionSnapshot;
    SoftRegion? _regionGestureTarget, _regionDraft;
    RegionBounds _regionOriginal;
    RegionEdge _regionEdge;
    Point _regionStart;
    bool _regionDragging;
    bool EditingRegions => _tool == "effects" && !_player.IsPlaying;

    string? _regionToolbarId, _regionToolbarStyle;

    void UpdateRegionToolbar()
    {
        var region = _project.Canvas.SoftRegions.FirstOrDefault(r => r.Id == _selectedDecoration);
        var visible = EditingRegions && _regionDrawingStyle is null && region is not null;
        RegionToolbar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        if (_regionToolbarId != region!.Id || _regionToolbarStyle != region.Style)
        {
            _regionToolbarId = region.Id; _regionToolbarStyle = region.Style;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (name, style) in new[] { ("柔化", "blur"), ("聚光", "spotlight") })
            {
                var button = DecorationButton(name, () => { Push(); region.Style = style; Commit(); });
                ControlAppearance.SelectChip(button, region.Style == style); row.Children.Add(button);
            }
            void Control(string name, double value, Action<double> assign)
            {
                var group = new StackPanel { Width = 84, Margin = new Thickness(6, 0, 4, 0) };
                group.Children.Add(new TextBlock { Text = name, FontSize = 10 });
                var slider = Slider(0, 1, value, assign);
                System.Windows.Automation.AutomationProperties.SetName(slider, "选区" + name);
                group.Children.Add(slider); row.Children.Add(group);
                // Rebuild the toolbar after inspector changes so both controls agree.
                slider.PreviewMouseLeftButtonUp += (_, _) => { _regionToolbarId = null; RebuildInspector(); };
            }
            Control("强度", region.Style == "spotlight" ? region.Dimming : region.Strength,
                v => { if (region.Style == "spotlight") region.Dimming = v; else region.Strength = v; });
            Control("圆角", region.Corner, v => region.Corner = v);
            RegionToolbar.Child = row;
        }
        RegionToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var corners = RegionCorners(region!);
        Canvas.SetLeft(RegionToolbar, Math.Clamp(corners.Min(p => p.X), 0, Math.Max(0, PreviewCanvas.Width - RegionToolbar.DesiredSize.Width)));
        Canvas.SetTop(RegionToolbar, Math.Max(0, corners.Min(p => p.Y) - RegionToolbar.DesiredSize.Height - 10));
    }

    void InitializeRegionEditor()
    {
        RegionEditor.Paint = PaintRegionEditor;
        RegionEditor.Down = RegionPointerDown;
        RegionEditor.Move = RegionPointerMove;
        RegionEditor.Finish = FinishRegionGesture;
    }

    void BeginRegionDrawing(string style, string? redrawId = null)
    {
        CancelRegionEditing();
        _player.Pause(); _tool = "effects";
        _regionDrawingStyle = style; _regionRedrawId = redrawId;
        _selectedClip = _selectedZoom = null;
        RebuildInspector(); ApplyZoomVisual(); RegionEditor.Focus();
    }

    void CancelRegionEditing()
    {
        FinishRegionGesture(true);
        _regionDrawingStyle = _regionRedrawId = null; _regionDraft = null;
        RegionEditor.InvalidateVisual();
    }

    ProjectionCorner[] RegionQuad() => _screenProjectionQuad ?? PerspectiveGeometry.Corners(FrameHost.Width, FrameHost.Height,
        _project.Canvas.TiltX, _project.Canvas.TiltY);

    Point RegionToView(SoftRegion region, double x, double y)
    {
        if (region.CoordinateSpace != "source") return new(x * RegionEditor.Width, y * RegionEditor.Height);
        var crop = _project.Canvas.Crop;
        var focus = SourceEffects.RenderTransformOrigin;
        var scale = SourceEffects.RenderTransform is ScaleTransform s ? s.ScaleX : 1;
        x = focus.X + ((x - crop.X) / crop.Width - focus.X) * scale;
        y = focus.Y + ((y - crop.Y) / crop.Height - focus.Y) * scale;
        var p = PerspectiveGeometry.DestinationPoint(RegionQuad(), x, y);
        return FrameHost.TranslatePoint(new(p.X, p.Y), RegionEditor);
    }

    Point ViewToRegion(SoftRegion region, Point view)
    {
        if (region.CoordinateSpace != "source") return new(view.X / RegionEditor.Width, view.Y / RegionEditor.Height);
        var local = RegionEditor.TranslatePoint(view, FrameHost);
        var (x, y) = PerspectiveGeometry.SourcePoint(RegionQuad(), local.X, local.Y);
        var focus = SourceEffects.RenderTransformOrigin;
        var scale = SourceEffects.RenderTransform is ScaleTransform s ? s.ScaleX : 1;
        var crop = _project.Canvas.Crop;
        return new(crop.X + (focus.X + (x - focus.X) / scale) * crop.Width,
            crop.Y + (focus.Y + (y - focus.Y) / scale) * crop.Height);
    }

    static Geometry Polygon(IEnumerable<Point> points)
    {
        var polygon = new StreamGeometry();
        using (var context = polygon.Open())
        {
            var vertices = points.ToArray();
            context.BeginFigure(vertices[0], true, true);
            context.PolyLineTo(vertices.Skip(1).ToArray(), true, false);
        }
        return polygon;
    }

    Geometry RegionVisibleFrame() => Polygon(new[] { 0, 1, 3, 2 }.Select(i =>
    {
        var q = RegionQuad()[i]; return FrameHost.TranslatePoint(new(q.X, q.Y), RegionEditor);
    }));

    Point[] RegionCorners(SoftRegion r) => [RegionToView(r, r.X, r.Y), RegionToView(r, r.X + r.Width, r.Y),
        RegionToView(r, r.X + r.Width, r.Y + r.Height), RegionToView(r, r.X, r.Y + r.Height)];

    IEnumerable<(Point Point, RegionEdge Edge)> RegionHandles(SoftRegion r)
    {
        var corners = RegionCorners(r);
        var narrow = Math.Min((corners[1] - corners[0]).Length, (corners[2] - corners[3]).Length) < 28;
        var shortRegion = Math.Min((corners[3] - corners[0]).Length, (corners[2] - corners[1]).Length) < 28;
        foreach (var (x, y, edge) in new[]
        {
            (0d, 0d, RegionEdge.Left | RegionEdge.Top), (1d, 0d, RegionEdge.Right | RegionEdge.Top),
            (1d, 1d, RegionEdge.Right | RegionEdge.Bottom), (0d, 1d, RegionEdge.Left | RegionEdge.Bottom),
            (.5, 0d, RegionEdge.Top), (1d, .5, RegionEdge.Right), (.5, 1d, RegionEdge.Bottom), (0d, .5, RegionEdge.Left),
        })
        {
            // Small text masks need a free center for moving; eight overlapping handles trap it.
            if ((narrow || shortRegion) && (x == .5 || y == .5)) continue;
            var p = RegionToView(r, r.X + x * r.Width, r.Y + y * r.Height);
            if (narrow) p.X += x == 0 ? -10 : 10;
            if (shortRegion) p.Y += y == 0 ? -10 : 10;
            yield return (p, edge);
        }
    }

    void PaintRegionEditor(DrawingContext dc)
    {
        var accent = (Brush)FindResource("SelectionOutlineBrush");
        foreach (var r in _project.Canvas.SoftRegions.Where(r => r.Id == _selectedDecoration
            || EffectTiming.OpacityAt(r.Timing, _player.Time) > 0).Concat(_regionDraft is null ? [] : new[] { _regionDraft }))
        {
            var selected = (_regionDrawingStyle is null && r.Id == _selectedDecoration) || r == _regionDraft;
            if (r.CoordinateSpace == "source") dc.PushClip(RegionVisibleFrame());
            var border = new Pen(selected ? accent : Brushes.White, selected ? 1.25 : 1) { DashStyle = selected ? DashStyles.Solid : DashStyles.Dash };
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), selected ? 3.25 : 3), Polygon(RegionCorners(r)));
            dc.DrawGeometry(null, border, Polygon(RegionCorners(r)));
            if (r.CoordinateSpace == "source") dc.Pop();
            if (selected)
                foreach (var (p, _) in RegionHandles(r)) dc.DrawRoundedRectangle(Brushes.White, new Pen(accent, 1), new(p.X - 3.5, p.Y - 3.5, 7, 7), 2, 2);
        }
        if (_regionDrawingStyle is not null)
        {
            var text = new FormattedText($"拖动框选{(_regionDrawingStyle == "blur" ? "柔化" : "聚光")}区域 · Esc 取消",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Source), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(225, 28, 32, 40)), null, new(8, 8, text.Width + 20, 28), 6, 6);
            dc.DrawText(text, new(18, 14));
        }
        else if (_project.Canvas.SoftRegions.FirstOrDefault(r => r.Id == _selectedDecoration) is { } selectedRegion)
        {
            var text = new FormattedText($"{(selectedRegion.Style == "spotlight" ? "聚光" : "柔化")} · 编辑预览{(selectedRegion.Enabled ? "" : " · 已停用")}",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Source), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(225, 28, 32, 40)), null, new(8, 8, text.Width + 20, 28), 6, 6);
            dc.DrawText(text, new(18, 14));
        }
    }

    (SoftRegion? Region, RegionEdge Edge) RegionHit(Point point)
    {
        var selected = _project.Canvas.SoftRegions.FirstOrDefault(r => r.Id == _selectedDecoration);
        bool Visible(SoftRegion r) => r.CoordinateSpace != "source" || RegionVisibleFrame().FillContains(point);
        if (selected is not null)
            foreach (var (p, edge) in RegionHandles(selected).OrderBy(h => (h.Point - point).Length))
                if ((p - point).Length <= 9) return (selected, edge);
        // Prefer the smallest target when regions overlap, so a large spotlight does not trap smaller masks.
        foreach (var r in _project.Canvas.SoftRegions.Where(r => r.Id == _selectedDecoration || EffectTiming.OpacityAt(r.Timing, _player.Time) > 0)
            .OrderBy(r => r.Width * r.Height))
        {
            if (!Visible(r)) continue;
            var p = ViewToRegion(r, point);
            if (p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height) return (r, RegionEdge.Move);
        }
        return (null, RegionEdge.Move);
    }

    void RegionPointerDown(Point point)
    {
        if (_regionDrawingStyle is not null)
        {
            var original = _project.Canvas.SoftRegions.FirstOrDefault(r => r.Id == _regionRedrawId);
            _regionDraft = original is null ? new SoftRegion { Style = _regionDrawingStyle, Enabled = true, CoordinateSpace = "source",
                Strength = .85, Dimming = .65, Timing = NewDecorationTiming() } : ScenePreset.Clone(original);
            if (_regionDraft.CoordinateSpace == "source" && !RegionVisibleFrame().FillContains(point)) { _regionDraft = null; return; }
            _regionGestureTarget = _regionDraft;
            _regionStart = ViewToRegion(_regionDraft, point); _regionDraft.Width = _regionDraft.Height = 0;
            _regionDraft.X = _regionStart.X; _regionDraft.Y = _regionStart.Y;
        }
        else
        {
            var hit = RegionHit(point);
            if (hit.Region is null) return;
            SelectDecoration(hit.Region.Id);
            _regionGestureTarget = hit.Region; _regionEdge = hit.Edge;
            _regionStart = ViewToRegion(hit.Region, point);
            _regionOriginal = RegionBounds.Read(hit.Region);
        }
        FlushPendingSave(); _saveTimer.Stop();
        _regionSnapshot = JsonSerializer.Serialize(_project, ProjectSchema.Json);
        _regionDragging = true; RegionEditor.CaptureMouse();
        RegionEditor.InvalidateVisual();
    }

    void RegionPointerMove(Point point)
    {
        if (!_regionDragging || _regionGestureTarget is null)
        {
            var hit = RegionHit(point);
            RegionEditor.Cursor = _regionDrawingStyle is not null ? Cursors.Cross : hit.Region is null ? Cursors.Arrow
                : hit.Edge == RegionEdge.Move ? Cursors.SizeAll
                : hit.Edge is RegionEdge.Top or RegionEdge.Bottom ? Cursors.SizeNS
                : hit.Edge is RegionEdge.Left or RegionEdge.Right ? Cursors.SizeWE
                : hit.Edge is (RegionEdge.Left | RegionEdge.Top) or (RegionEdge.Right | RegionEdge.Bottom) ? Cursors.SizeNWSE : Cursors.SizeNESW;
            return;
        }
        var p = ViewToRegion(_regionGestureTarget, point);
        if (_regionDraft is not null)
            RegionBounds.Draw(_regionStart.X, _regionStart.Y, p.X, p.Y).Apply(_regionDraft);
        else
        {
            _regionOriginal.Drag(_regionEdge, p.X - _regionStart.X, p.Y - _regionStart.Y).Apply(_regionGestureTarget);
            LayoutPreview();
        }
        RegionEditor.InvalidateVisual();
    }

    void FinishRegionGesture(bool cancel)
    {
        if (!_regionDragging || _regionGestureTarget is null) return;
        _regionDragging = false; // Release triggers LostMouseCapture; finish exactly once.
        RegionEditor.ReleaseMouseCapture();
        if (_regionDraft is { } draft)
        {
            var corners = RegionCorners(draft);
            if (!cancel && (corners[1] - corners[0]).Length >= 2 && (corners[3] - corners[0]).Length >= 2)
            {
                Push(_regionSnapshot);
                var original = _project.Canvas.SoftRegions.FirstOrDefault(r => r.Id == _regionRedrawId);
                if (original is not null) RegionBounds.Read(draft).Apply(original);
                else
                {
                    if (draft.Style == "blur") draft.Timing.Transition = "none";
                    _project.Canvas.SoftRegions.Add(draft);
                    ApplyTrackFlags(TrackFlags | 8);
                }
                _selectedDecoration = original?.Id ?? draft.Id;
                _regionDrawingStyle = _regionRedrawId = null;
            }
        }
        else if (cancel) _regionOriginal.Apply(_regionGestureTarget);
        else if (RegionBounds.Read(_regionGestureTarget) != _regionOriginal) Push(_regionSnapshot);
        _regionDraft = _regionGestureTarget = null; _regionSnapshot = null;
        Commit();
    }

    bool RegionKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Escape && (_regionDrawingStyle is not null || _regionDragging))
        { CancelRegionEditing(); RebuildInspector(); ApplyZoomVisual(); return true; }
        if (!EditingRegions || modifiers is not (ModifierKeys.None or ModifierKeys.Shift)
            || key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return false;
        if (Keyboard.FocusedElement is ComboBox or ComboBoxItem or System.Windows.Controls.Slider) return false;
        var region = _project.Canvas.SoftRegions.FirstOrDefault(r => r.Id == _selectedDecoration);
        if (region is null) return false;
        var (w, h) = region.CoordinateSpace == "source" ? (_project.Width, _project.Height)
            : CanvasLayout.OutputSize(_project.Width, _project.Height, _project.Canvas.Aspect, "source");
        var step = modifiers == ModifierKeys.Shift ? 10 : 1;
        var dx = key == Key.Left ? -step / (double)w : key == Key.Right ? step / (double)w : 0;
        var dy = key == Key.Up ? -step / (double)h : key == Key.Down ? step / (double)h : 0;
        var original = RegionBounds.Read(region); var next = original.Drag(RegionEdge.Move, dx, dy);
        if (next != original) { Push(); next.Apply(region); Commit(); }
        return true;
    }

    void ChangeRegionCoordinates(SoftRegion region, string coordinates)
    {
        if (coordinates == region.CoordinateSpace) return;
        var corners = RegionCorners(region);
        Push(); region.CoordinateSpace = coordinates;
        var target = corners.Select(p => ViewToRegion(region, p)).ToArray();
        RegionBounds.Draw(target.Min(p => p.X), target.Min(p => p.Y), target.Max(p => p.X), target.Max(p => p.Y)).Apply(region);
        Commit();
    }

    void RegionValueControl(StackPanel panel, string title, double min, double max, double value, Action<double> assign)
    {
        var label = Caption($"{title} · {value:P0}"); panel.Children.Add(label);
        panel.Children.Add(Slider(min, max, value, x => { assign(x); label.Text = $"{title} · {x:P0}"; }));
    }

    void BuildRegionNumbers(StackPanel panel, SoftRegion region)
    {
        var (w, h) = region.CoordinateSpace == "source" ? (_project.Width, _project.Height)
            : CanvasLayout.OutputSize(_project.Width, _project.Height, _project.Canvas.Aspect, "source");
        panel.Children.Add(Caption($"位置与大小（{(region.CoordinateSpace == "source" ? "原画面" : "画布")}像素）"));
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new());
        var values = new[] { ("左侧", region.X * w), ("顶部", region.Y * h), ("宽度", region.Width * w), ("高度", region.Height * h) };
        for (var i = 0; i < values.Length; i++)
        {
            var index = i; var (name, value) = values[i];
            var group = new StackPanel { Margin = new Thickness(i % 2 == 0 ? 0 : 6, 0, i % 2 == 0 ? 6 : 0, 4) };
            group.Children.Add(Caption(name));
            var initial = value.ToString("0.#", CultureInfo.CurrentCulture);
            var box = new TextBox { Text = initial, Padding = new Thickness(6, 3, 6, 3), MinWidth = 65 };
            System.Windows.Automation.AutomationProperties.SetName(box, name + "像素");
            bool submitted = false;
            void Submit()
            {
                if (submitted || box.Text == initial) return;
                if (!double.TryParse(box.Text, out var number) || !double.IsFinite(number)) { box.Text = initial; return; }
                var before = RegionBounds.Read(region);
                var next = index switch
                {
                    0 => before with { X = Math.Clamp(number / w, 0, 1 - before.Width) },
                    1 => before with { Y = Math.Clamp(number / h, 0, 1 - before.Height) },
                    2 => before with { Width = Math.Clamp(number / w, 1d / w, 1 - before.X) },
                    _ => before with { Height = Math.Clamp(number / h, 1d / h, 1 - before.Y) },
                };
                submitted = true;
                if (next != before) { Push(); next.Apply(region); Commit(); }
            }
            box.LostKeyboardFocus += (_, _) => Submit();
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Submit(); RegionEditor.Focus(); e.Handled = true; }
                else if (e.Key == Key.Escape) { box.Text = initial; RegionEditor.Focus(); e.Handled = true; }
            };
            group.Children.Add(box); Grid.SetColumn(group, i % 2); Grid.SetRow(group, i / 2); grid.Children.Add(group);
        }
        panel.Children.Add(grid);
    }
}
