using System.Globalization;
using System.Text.Json;
using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    bool _cropEditing, _cropDragging;
    RegionBounds _cropInitial;
    string[] _cropUndo = [], _cropRedo = [];
    Point _cropStart;
    RegionEdge _cropEdge;
    RegionBounds _cropOriginal;
    string? _cropSnapshot;
    TextBlock? _cropSize;

    void InitializeCropEditor()
    {
        CropSource.Fill = new DrawingBrush(_player.ScreenDrawing) { Stretch = Stretch.Fill };
        CropEditor.Paint = PaintCropEditor;
        CropEditor.Down = CropDown;
        CropEditor.Move = CropMove;
        CropEditor.Finish = FinishCropGesture;
    }

    void BeginCrop()
    {
        FlushPendingSave();
        _player.Pause(); CancelOverlayGesture(); CancelRegionEditing();
        var crop = _project.Canvas.Crop; _cropInitial = new(crop.X, crop.Y, crop.Width, crop.Height);
        _cropUndo = _undo.ToArray(); _cropRedo = _redo.ToArray();
        _cropEditing = true; _cameraSelected = false;
        PreviewCanvas.Visibility = Visibility.Collapsed; CropWorkspace.Visibility = Visibility.Visible;
        Timeline.IsEnabled = TimelineToolbar.IsEnabled = ToolRailSurface.IsEnabled = false;
        BuildMonitorToolbar(); RebuildInspector(); LayoutCropPreview();
    }

    void EndCrop()
    {
        FinishCropGesture(false); _cropEditing = false;
        PreviewCanvas.Visibility = Visibility.Visible; CropWorkspace.Visibility = Visibility.Collapsed;
        Timeline.IsEnabled = TimelineToolbar.IsEnabled = ToolRailSurface.IsEnabled = true;
        BuildMonitorToolbar(); RebuildInspector(); LayoutPreview();
    }

    void CancelCrop()
    {
        FinishCropGesture(true); SetCrop(_cropInitial);
        _undo.Clear(); foreach (var snapshot in _cropUndo.Reverse()) _undo.Push(snapshot);
        _redo.Clear(); foreach (var snapshot in _cropRedo.Reverse()) _redo.Push(snapshot);
        EndCrop(); Commit();
    }

    void LayoutCropPreview()
    {
        var scale = Math.Min(Math.Max(2, PreviewSlot.ActualWidth - 20) / _project.Width,
            Math.Max(2, PreviewSlot.ActualHeight - 20) / _project.Height);
        InspectorSurface.Height = Math.Min(PreviewSlot.ActualHeight, _project.Height * scale) / _chromeScale;
        CropSource.Width = CropEditor.Width = _project.Width * scale;
        CropSource.Height = CropEditor.Height = _project.Height * scale;
        CropEditor.InvalidateVisual(); UpdateCropSize();
    }

    Rect CropBox()
    {
        var c = _project.Canvas.Crop;
        return new(c.X * CropEditor.Width, c.Y * CropEditor.Height, c.Width * CropEditor.Width, c.Height * CropEditor.Height);
    }

    void PaintCropEditor(DrawingContext dc)
    {
        if (CropEditor.Width <= 0) return;
        var r = CropBox();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), null,
            new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, CropEditor.Width, CropEditor.Height)), new RectangleGeometry(r)));
        dc.DrawRectangle(null, new Pen(Brushes.White, 1.5), r);
        if (_cropDragging)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), .8);
            for (var i = 1; i <= 2; i++)
            {
                dc.DrawLine(pen, new(r.Left + r.Width * i / 3, r.Top), new(r.Left + r.Width * i / 3, r.Bottom));
                dc.DrawLine(pen, new(r.Left, r.Top + r.Height * i / 3), new(r.Right, r.Top + r.Height * i / 3));
            }
        }
        foreach (var p in new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight,
                     new Point(r.Left + r.Width / 2, r.Top), new Point(r.Left + r.Width / 2, r.Bottom),
                     new Point(r.Left, r.Top + r.Height / 2), new Point(r.Right, r.Top + r.Height / 2) })
            dc.DrawRoundedRectangle(Brushes.White, new Pen(Brushes.Gray, .7), new(p.X - 3, p.Y - 3, 6, 6), 1, 1);
    }

    RegionEdge? CropHit(Point p)
    {
        var r = CropBox(); var expanded = r; expanded.Inflate(9, 9);
        if (!expanded.Contains(p)) return null;
        var edge = RegionEdge.Move;
        if (Math.Abs(p.X - r.Left) <= 9) edge |= RegionEdge.Left;
        else if (Math.Abs(p.X - r.Right) <= 9) edge |= RegionEdge.Right;
        if (Math.Abs(p.Y - r.Top) <= 9) edge |= RegionEdge.Top;
        else if (Math.Abs(p.Y - r.Bottom) <= 9) edge |= RegionEdge.Bottom;
        return edge;
    }

    void CropDown(Point p)
    {
        if (CropHit(p) is not { } edge) return;
        _cropEdge = edge; _cropStart = p;
        var c = _project.Canvas.Crop; _cropOriginal = new(c.X, c.Y, c.Width, c.Height);
        _cropSnapshot = JsonSerializer.Serialize(_project, ProjectSchema.Json);
        _cropDragging = true; CropEditor.CaptureMouse();
    }

    void CropMove(Point p)
    {
        if (!_cropDragging)
        {
            CropEditor.Cursor = CropHit(p) switch
            {
                RegionEdge.Move => Cursors.SizeAll,
                RegionEdge.Left or RegionEdge.Right => Cursors.SizeWE,
                RegionEdge.Top or RegionEdge.Bottom => Cursors.SizeNS,
                RegionEdge.Left | RegionEdge.Top or RegionEdge.Right | RegionEdge.Bottom => Cursors.SizeNWSE,
                RegionEdge.Left | RegionEdge.Bottom or RegionEdge.Right | RegionEdge.Top => Cursors.SizeNESW,
                _ => Cursors.Arrow,
            };
            return;
        }
        var r = _cropOriginal.Drag(_cropEdge, (p.X - _cropStart.X) / CropEditor.Width, (p.Y - _cropStart.Y) / CropEditor.Height);
        // Preserve the opposite edge when dragging past the minimum crop size.
        if (r.Width < .02) r = r with { X = _cropEdge.HasFlag(RegionEdge.Left) ? _cropOriginal.X + _cropOriginal.Width - .02 : r.X, Width = .02 };
        if (r.Height < .02) r = r with { Y = _cropEdge.HasFlag(RegionEdge.Top) ? _cropOriginal.Y + _cropOriginal.Height - .02 : r.Y, Height = .02 };
        SetCrop(r); CropEditor.InvalidateVisual(); UpdateCropSize();
    }

    void SetCrop(RegionBounds r) => _project.Canvas.Crop = new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };

    void FinishCropGesture(bool cancel)
    {
        if (!_cropDragging) return;
        _cropDragging = false; CropEditor.ReleaseMouseCapture();
        var c = _project.Canvas.Crop; var changed = new RegionBounds(c.X, c.Y, c.Width, c.Height) != _cropOriginal;
        if (cancel) SetCrop(_cropOriginal);
        else if (changed) { Push(_cropSnapshot); Commit(); FlushPendingSave(); }
        _cropSnapshot = null; CropEditor.InvalidateVisual(); UpdateCropSize();
    }

    void UpdateCropSize()
    {
        if (_cropSize is null) return;
        _cropSize.Text = $"裁切后尺寸  {Math.Round(_project.Width * _project.Canvas.Crop.Width)} × {Math.Round(_project.Height * _project.Canvas.Crop.Height)}";
    }

    void BuildCropInspector()
    {
        InspectorHeader("裁切", "屏幕");
        Inspector.Children.Add(Caption("拖动边缘或角点调整裁切，拖动框内移动选区。"));
        var c = _project.Canvas.Crop;
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
        Side("左", c.X, _project.Width, RegionEdge.Left);
        Side("右", 1 - c.X - c.Width, _project.Width, RegionEdge.Right);
        Side("上", c.Y, _project.Height, RegionEdge.Top);
        Side("下", 1 - c.Y - c.Height, _project.Height, RegionEdge.Bottom);
        Inspector.Children.Add(grid);
        var divider = new Border { Height = .75, Background = (Brush)FindResource("LineBrush"), Margin = new Thickness(0, 8, 0, 0) }; Inspector.Children.Add(divider);
        _cropSize = new TextBlock { FontSize = 11, Margin = new Thickness(0, 12, 0, 0) }; Inspector.Children.Add(_cropSize); UpdateCropSize();
        void Side(string title, double value, double dimension, RegionEdge edge)
        {
            var row = new DockPanel { Margin = new Thickness(2, 2, 6, 8) };
            var label = new TextBlock { Text = title, FontSize = 11, Margin = new Thickness(5, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Left); row.Children.Add(label);
            var line = new DockPanel();
            var box = new TextBox { Text = Math.Round(value * dimension).ToString(CultureInfo.InvariantCulture), MinWidth = 38, FontSize = 11, Padding = new Thickness(5, 3, 5, 3), TextAlignment = TextAlignment.Center, BorderThickness = new Thickness(0), Background = (Brush)FindResource("ValueWellBrush"), Margin = new Thickness(2, 0, 2, 0) };
            System.Windows.Automation.AutomationProperties.SetName(box, title + "裁切像素");
            void Apply(double pixels)
            {
                var crop = _project.Canvas.Crop; var original = new RegionBounds(crop.X, crop.Y, crop.Width, crop.Height);
                var current = edge switch { RegionEdge.Left => crop.X, RegionEdge.Right => 1 - crop.X - crop.Width, RegionEdge.Top => crop.Y, _ => 1 - crop.Y - crop.Height };
                var remaining = edge is RegionEdge.Left or RegionEdge.Right ? original.Width : original.Height;
                var desired = Math.Clamp(pixels / dimension, 0, current + remaining - .02);
                var delta = (desired - current) * (edge is RegionEdge.Right or RegionEdge.Bottom ? -1 : 1);
                Push(); SetCrop(original.Drag(edge, edge is RegionEdge.Left or RegionEdge.Right ? delta : 0, edge is RegionEdge.Top or RegionEdge.Bottom ? delta : 0));
                Commit();
            }
            var minus = DecorationButton("−", () => Apply(value * dimension - 1)); minus.Padding = new Thickness(5);
            var plus = DecorationButton("+", () => Apply(value * dimension + 1)); plus.Padding = new Thickness(5);
            minus.FontSize = plus.FontSize = 11; minus.Margin = plus.Margin = new Thickness(0);
            DockPanel.SetDock(minus, Dock.Right); DockPanel.SetDock(plus, Dock.Right);
            line.Children.Add(plus); line.Children.Add(minus); line.Children.Add(box); row.Children.Add(line); grid.Children.Add(row);
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter && double.TryParse(box.Text, out var p) && double.IsFinite(p)) { Apply(p); e.Handled = true; } };
        }
    }
}
