using System.Globalization;
using System.Text.Json;
using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    bool _cameraSelected, _overlayDragging, _cameraContentEditing;
    string? _overlayId, _overlaySnapshot;
    int _overlayHandle;
    Point _overlayStart;
    OverlayBox _overlayOriginal;
    CameraBubble? _cameraOriginal;
    StickerImage? _stickerOriginal;
    double? _snapX, _snapY;
    double? _cameraMaskAspect;
    double CameraSourceAspect => _player.CameraDrawing.Rect.Width / _player.CameraDrawing.Rect.Height;
    bool EditingOverlays => !_player.IsPlaying && _tool is "effects" or "layout" && _regionDrawingStyle is null;

    void InitializeOverlayEditor()
    {
        OverlayEditor.Paint = PaintOverlayEditor;
        OverlayEditor.Hit = p => OverlayHit(p).Id is not null;
        OverlayEditor.Down = OverlayPointerDown;
        OverlayEditor.Move = OverlayPointerMove;
        OverlayEditor.Finish = FinishOverlayGesture;
    }

    void UpdateOverlayEditor()
    {
        OverlayEditor.Width = PreviewCanvas.Width; OverlayEditor.Height = PreviewCanvas.Height;
        OverlayEditor.Visibility = EditingOverlays ? Visibility.Visible : Visibility.Collapsed;
        OverlayEditor.InvalidateVisual();
        UpdateCameraContent();
    }

    void UpdateCameraContent()
    {
        if (CameraBubble.Fill is not DrawingBrush brush || CameraBubble.Width <= 0 || CameraBubble.Height <= 0) return;
        var camera = _project.Camera;
        var aspect = CameraBubble.Width / CameraBubble.Height;
        var visible = CameraContentFill.Visible(CameraSourceAspect, aspect, camera);
        brush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
        brush.Viewbox = new Rect(visible.X, visible.Y, visible.Width, visible.Height);
        brush.RelativeTransform = camera.Mirrored ? new ScaleTransform(-1, 1, .5, .5) : Transform.Identity;
    }

    OverlayBox OverlayBounds(string id)
    {
        if (id == "camera") return new(Canvas.GetLeft(CameraBubble) + CameraBubble.Width / 2,
            Canvas.GetTop(CameraBubble) + CameraBubble.Height / 2, CameraBubble.Width, CameraBubble.Height);
        var image = _stickerImages[id];
        return new(Canvas.GetLeft(image) + image.Width / 2, Canvas.GetTop(image) + image.Height / 2,
            image.Width, image.Height, _project.Canvas.Stickers.First(s => s.Id == id).Rotation);
    }

    string? SelectedOverlay => _cameraSelected && _tool == "layout" ? "camera"
        : _project.Canvas.Stickers.Any(s => s.Id == _selectedDecoration) ? _selectedDecoration : null;

    static Point OverlayPoint(OverlayBox box, double u, double v)
    { var p = box.Point(u, v); return new(p.X, p.Y); }

    IEnumerable<string> VisibleOverlays() => _project.Canvas.Stickers.AsEnumerable().Reverse()
        .Where(s => _stickerImages.TryGetValue(s.Id, out var image) && image.Visibility == Visibility.Visible && image.Opacity > 0)
        .Select(s => s.Id).Concat(_tool == "layout" && CameraBubble.Visibility == Visibility.Visible && CameraBubble.Opacity > 0 ? new[] { "camera" } : []);

    IEnumerable<(Point Point, int Handle)> OverlayHandles(string id)
    {
        var box = OverlayBounds(id);
        for (var i = 0; i < 4; i++)
        {
            var u = i is 0 or 3 ? 0d : 1d; var v = i < 2 ? 0d : 1d;
            var p = OverlayPoint(box, u, v);
            var a = box.Rotation * Math.PI / 180;
            var dx = box.Width < 28 ? (u == 0 ? -10 : 10) : 0;
            var dy = box.Height < 28 ? (v == 0 ? -10 : 10) : 0;
            p += new Vector(dx * Math.Cos(a) - dy * Math.Sin(a), dx * Math.Sin(a) + dy * Math.Cos(a));
            yield return (p, i);
        }
        if (id != "camera")
        {
            var p = OverlayPoint(box, .5, -26 / box.Height);
            yield return (new Point(Math.Clamp(p.X, 7, OverlayEditor.Width - 7), Math.Clamp(p.Y, 7, OverlayEditor.Height - 7)), 4);
        }
    }

    (string? Id, int Handle) OverlayHit(Point point)
    {
        if (!EditingOverlays) return (null, -1);
        if (SelectedOverlay is { } selected && VisibleOverlays().Contains(selected))
            foreach (var h in OverlayHandles(selected))
                if ((h.Point - point).Length <= 9) return (selected, h.Handle);
        if (SelectedOverlay is { } small && VisibleOverlays().Contains(small))
        {
            var box = OverlayBounds(small); var local = box.Local(point.X, point.Y);
            if ((box.Width < 28 || box.Height < 28) && local.U >= -6 / box.Width && local.U <= 1 + 6 / box.Width
                && local.V >= -6 / box.Height && local.V <= 1 + 6 / box.Height) return (small, -1);
        }
        var hits = VisibleOverlays().Where(id =>
        { var p = OverlayBounds(id).Local(point.X, point.Y); return p.U >= 0 && p.U <= 1 && p.V >= 0 && p.V <= 1; }).ToArray();
        if (hits.Length == 0) return (null, -1);
        var index = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? (Array.IndexOf(hits, SelectedOverlay) + 1) % hits.Length : 0;
        return (hits[index], -1);
    }

    void PaintOverlayEditor(DrawingContext dc)
    {
        if (SelectedOverlay is not { } id || !VisibleOverlays().Contains(id)) return;
        var accent = (Brush)FindResource("SelectionOutlineBrush");
        var box = OverlayBounds(id);
        var corners = new[] { OverlayPoint(box, 0, 0), OverlayPoint(box, 1, 0), OverlayPoint(box, 1, 1), OverlayPoint(box, 0, 1) };
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 3.25), Polygon(corners));
        dc.DrawGeometry(null, new Pen(accent, 1.25), Polygon(corners));
        foreach (var h in OverlayHandles(id))
        {
            if (h.Handle == 4)
            { dc.DrawLine(new Pen(accent, 1), OverlayPoint(box, .5, 0), h.Point); dc.DrawEllipse(Brushes.White, new Pen(accent, 1), h.Point, 4, 4); }
            else dc.DrawRoundedRectangle(Brushes.White, new Pen(accent, 1), new(h.Point.X - 3.5, h.Point.Y - 3.5, 7, 7), 2, 2);
        }
        var snap = new Pen(accent, 1) { DashStyle = DashStyles.Dash };
        if (_snapX is { } x) dc.DrawLine(snap, new(x, 0), new(x, OverlayEditor.Height));
        if (_snapY is { } y) dc.DrawLine(snap, new(0, y), new(OverlayEditor.Width, y));
        if (id == "camera" && _cameraContentEditing)
        {
            var text = new FormattedText(L.Text("拖动画面调整构图 · 边角调整外框"), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily.Source), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(225, 28, 32, 40)), null, new(8, 8, text.Width + 20, 28), 6, 6);
            dc.DrawText(text, new(18, 14));
        }
    }

    void OverlayPointerDown(Point point)
    {
        var hit = OverlayHit(point); if (hit.Id is null) return;
        if (hit.Id == "camera")
        {
            CancelRegionEditing(); _player.Pause(); _cameraSelected = true; _selectedDecoration = _selectedClip = _selectedZoom = null;
            RebuildInspector();
            Inspector.Children.OfType<Button>().FirstOrDefault()?.BringIntoView();
        }
        else SelectDecoration(hit.Id);
        FlushPendingSave(); _saveTimer.Stop();
        _overlaySnapshot = JsonSerializer.Serialize(_project, ProjectSchema.Json);
        _overlayOriginal = OverlayBounds(hit.Id); _overlayStart = point; _overlayId = hit.Id; _overlayHandle = hit.Handle;
        _cameraOriginal = hit.Id == "camera" ? ScenePreset.Clone(_project.Camera) : null;
        _stickerOriginal = hit.Id != "camera" ? ScenePreset.Clone(_project.Canvas.Stickers.First(s => s.Id == hit.Id)) : null;
        _overlayDragging = true; OverlayEditor.CaptureMouse(); OverlayEditor.Focus(); OverlayEditor.InvalidateVisual();
    }

    void OverlayPointerMove(Point point)
    {
        if (!_overlayDragging || _overlayId is null)
        {
            var hit = OverlayHit(point);
            OverlayEditor.Cursor = hit.Handle == 4 ? Cursors.Hand : hit.Handle >= 0 ? Cursors.SizeNWSE : Cursors.SizeAll;
            return;
        }
        var delta = point - _overlayStart;
        if (_overlayId == "camera" && _cameraContentEditing && _overlayHandle == -1)
        {
            var c = _project.Camera; var original = _cameraOriginal!; var box = _overlayOriginal;
            var sourceWidth = Math.Max(box.Width, box.Height * CameraSourceAspect) * c.ContentScale * CameraContentFill.EdgeOverscan;
            var sourceHeight = sourceWidth / CameraSourceAspect;
            // Content controls follow the final mirrored visual direction, as on macOS.
            if (sourceWidth - box.Width > .1) c.ContentX = Math.Clamp(original.ContentX - delta.X / (sourceWidth - box.Width), 0, 1);
            if (sourceHeight - box.Height > .1) c.ContentY = Math.Clamp(original.ContentY - delta.Y / (sourceHeight - box.Height), 0, 1);
            UpdateCameraContent(); return;
        }
        OverlayBox next;
        if (_overlayHandle == 4)
        {
            double Angle(Point p) => Math.Atan2(p.Y - _overlayOriginal.Y, p.X - _overlayOriginal.X) * 180 / Math.PI;
            var rotation = _overlayOriginal.Rotation + Angle(point) - Angle(_overlayStart);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) rotation = Math.Round(rotation / 15) * 15;
            rotation = (rotation + 540) % 360 - 180;
            next = _overlayOriginal with { Rotation = rotation };
        }
        else if (_overlayHandle >= 0)
        {
            var docked = _overlayId == "camera" && _project.Camera.Docked;
            var ratio = _overlayOriginal.Width / _overlayOriginal.Height;
            var shortEdge = Math.Min(OverlayEditor.Width, OverlayEditor.Height);
            var zoomScale = 1 + (_project.Camera.ScaleDuringZoom - 1) * Math.Clamp((_zoomTransform.ScaleX - 1) / .565, 0, 1);
            next = _overlayOriginal.Resize(_overlayHandle, delta.X, delta.Y,
                docked ? shortEdge * 72 / 1080 * ratio : OverlayEditor.Width * (_overlayId == "camera" ? .08 : .02),
                docked ? Math.Min(OverlayEditor.Width * 1.2, shortEdge * Math.Min(1.2, zoomScale) * ratio) : OverlayEditor.Width * (_overlayId == "camera" ? .46 : 1));
        }
        else next = _overlayOriginal with { X = _overlayOriginal.X + delta.X, Y = _overlayOriginal.Y + delta.Y };
        _snapX = _snapY = null;
        next = _overlayId == "camera" ? CanvasLayout.FitCamera(next, OverlayEditor.Width, OverlayEditor.Height, _project.Camera)
            : next.Fit(OverlayEditor.Width, OverlayEditor.Height);
        if (_overlayHandle == -1 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            var a = next.Rotation * Math.PI / 180;
            var ex = (Math.Abs(Math.Cos(a)) * next.Width + Math.Abs(Math.Sin(a)) * next.Height) / 2;
            var ey = (Math.Abs(Math.Sin(a)) * next.Width + Math.Abs(Math.Cos(a)) * next.Height) / 2;
            if (Math.Abs(next.X - ex) < 6) { next = next with { X = ex }; _snapX = 0; }
            else if (Math.Abs(OverlayEditor.Width - next.X - ex) < 6) { next = next with { X = OverlayEditor.Width - ex }; _snapX = OverlayEditor.Width; }
            if (Math.Abs(next.Y - ey) < 6) { next = next with { Y = ey }; _snapY = 0; }
            else if (Math.Abs(OverlayEditor.Height - next.Y - ey) < 6) { next = next with { Y = OverlayEditor.Height - ey }; _snapY = OverlayEditor.Height; }
            if (Math.Abs(next.X - OverlayEditor.Width / 2) < 6) { next = next with { X = OverlayEditor.Width / 2 }; _snapX = next.X; }
            if (Math.Abs(next.Y - OverlayEditor.Height / 2) < 6) { next = next with { Y = OverlayEditor.Height / 2 }; _snapY = next.Y; }
        }
        ApplyOverlayBox(_overlayId, next, resize: _overlayHandle >= 0); LayoutPreview();
    }

    void ApplyOverlayBox(string id, OverlayBox box, bool resize = false)
    {
        if (id == "camera")
        {
            box = CanvasLayout.FitCamera(box, OverlayEditor.Width, OverlayEditor.Height, _project.Camera);
            CanvasLayout.WriteCameraBox(_project.Camera, box, OverlayEditor.Width, OverlayEditor.Height, _zoomTransform.ScaleX, resize);
        }
        else
        { box = box.Fit(OverlayEditor.Width, OverlayEditor.Height); var s = _project.Canvas.Stickers.First(s => s.Id == id); s.X = box.X / OverlayEditor.Width; s.Y = box.Y / OverlayEditor.Height; s.Width = box.Width / OverlayEditor.Width; s.Rotation = box.Rotation; }
    }

    void FinishOverlayGesture(bool cancel)
    {
        if (!_overlayDragging) return;
        _overlayDragging = false; OverlayEditor.ReleaseMouseCapture();
        if (cancel)
        {
            if (_cameraOriginal is not null) _project.Camera = _cameraOriginal;
            if (_stickerOriginal is not null)
                _project.Canvas.Stickers[_project.Canvas.Stickers.FindIndex(s => s.Id == _stickerOriginal.Id)] = _stickerOriginal;
        }
        else if (JsonSerializer.Serialize(_project, ProjectSchema.Json) != _overlaySnapshot) Push(_overlaySnapshot);
        _overlayId = _overlaySnapshot = null; _cameraOriginal = null; _stickerOriginal = null; _snapX = _snapY = null;
        Commit();
    }

    void CancelOverlayGesture() => FinishOverlayGesture(true);

    bool OverlayKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Escape && _overlayDragging) { CancelOverlayGesture(); return true; }
        if (!EditingOverlays || SelectedOverlay is not { } id || !OverlayEditor.IsKeyboardFocused) return false;
        if (key == Key.Tab)
        {
            var ids = VisibleOverlays().Where(x => x != "camera").ToArray();
            if (ids.Length < 2) return false;
            var step = modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            SelectDecoration(ids[(Array.IndexOf(ids, id) + step + ids.Length) % ids.Length]); OverlayEditor.Focus(); return true;
        }
        if (modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt)) return false;
        var stepPixels = modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var amount = stepPixels * OverlayEditor.Width / CanvasLayout.OutputSize(_project.Width, _project.Height, _project.Canvas.Aspect, "source").Width;
        var dx = key == Key.Left ? -amount : key == Key.Right ? amount : 0;
        var dy = key == Key.Up ? -amount : key == Key.Down ? amount : 0;
        if (dx == 0 && dy == 0) return false;
        var box = OverlayBounds(id); Push(); ApplyOverlayBox(id, box with { X = box.X + dx, Y = box.Y + dy }); Commit(); return true;
    }

    void BuildCameraContentControls()
    {
        Inspector.Children.Add(Caption(L.Text("拖动外框摆放 · 边角缩放 · 方向键微调")));
        var edit = new CheckBox { Content = L.Text("调整内部画面（拖动构图）"), IsChecked = _cameraContentEditing, Margin = new(0, 6, 0, 6) };
        edit.Click += (_, _) => { _cameraContentEditing = edit.IsChecked == true; _cameraSelected = true; _player.Pause(); ApplyZoomVisual(); };
        Inspector.Children.Add(edit);
        RegionValueControl(Inspector, L.Text("内部放大"), 1, 4, _project.Camera.ContentScale, x => _project.Camera.ContentScale = x);
        RegionValueControl(Inspector, L.Text("内部水平位置"), 0, 1, _project.Camera.ContentX, x => _project.Camera.ContentX = x);
        RegionValueControl(Inspector, L.Text("内部垂直位置"), 0, 1, _project.Camera.ContentY, x => _project.Camera.ContentY = x);
        Inspector.Children.Add(DecorationButton(L.Text("重置构图"), () => { Push(); _project.Camera.ContentX = _project.Camera.ContentY = .5; _project.Camera.ContentScale = 1; Commit(); }));
    }

    void BuildStickerNumbers(Panel panel, StickerImage sticker)
    {
        var (width, height) = CanvasLayout.OutputSize(_project.Width, _project.Height, _project.Canvas.Aspect, "source");
        var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var (title, value, assign) in new (string, double, Action<double>)[] {
            (L.Text("中心 X / px"), sticker.X * width, x => sticker.X = Math.Clamp(x / width, 0, 1)),
            (L.Text("中心 Y / px"), sticker.Y * height, y => sticker.Y = Math.Clamp(y / height, 0, 1)),
            (L.Text("宽度 / px"), sticker.Width * width, w => sticker.Width = Math.Clamp(w / width, .02, 1)),
            (L.Text("角度 / °"), sticker.Rotation, a => sticker.Rotation = Math.Clamp(a, -180, 180)) })
        {
            var group = new StackPanel { Margin = new(0, 0, 6, 4) }; group.Children.Add(Caption(title));
            var initial = value.ToString("0.#", CultureInfo.CurrentCulture); var box = new TextBox { Text = initial, MinWidth = 70 };
            System.Windows.Automation.AutomationProperties.SetName(box, title);
            var submitted = false;
            void Submit()
            {
                if (submitted || box.Text == initial) return;
                if (!double.TryParse(box.Text, out var number) || !double.IsFinite(number)) { box.Text = initial; return; }
                submitted = true; Push(); assign(number); Commit();
            }
            box.LostKeyboardFocus += (_, _) => Submit();
            box.PreviewKeyDown += (_, e) => {
                if (e.Key == Key.Enter) { Submit(); OverlayEditor.Focus(); e.Handled = true; }
                else if (e.Key == Key.Escape) { box.Text = initial; OverlayEditor.Focus(); e.Handled = true; }
            };
            group.Children.Add(box); row.Children.Add(group);
        }
        panel.Children.Add(row);
    }
}
