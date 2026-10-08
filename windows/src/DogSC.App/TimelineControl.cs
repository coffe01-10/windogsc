using System.Globalization;
using DogSC.Core;

namespace DogSC;

public sealed record FilmFrame(string AssetId, double SourceTime, ImageSource Image);

public sealed class TimelineControl : FrameworkElement
{
    const double RulerHeight = 26;
    const double ClipTop = 32;
    const double ClipHeight = 58;
    const double ZoomTop = 98;
    const double ZoomHeight = 28;

    ProjectDocument? _project;
    readonly List<FilmFrame> _frames = [];
    double _playhead;
    double _zoom = 1;
    double _scroll;
    string? _selectedClip;
    string? _selectedZoom;
    Drag? _drag;

    public event EventHandler<double>? SeekRequested;
    public event EventHandler? InteractionStarted;
    public event EventHandler<ClipTrim>? TrimFinished;
    public event EventHandler<ZoomMove>? ZoomFinished;
    public event EventHandler<string>? ClipSelected;
    public event EventHandler<string>? ZoomSelected;

    public TimelineControl()
    {
        MinHeight = 136;
        Cursor = Cursors.Arrow;
    }

    public ProjectDocument? Project
    {
        get => _project;
        set
        {
            _project = value;
            InvalidateVisual();
        }
    }

    public double Playhead
    {
        get => _playhead;
        set
        {
            _playhead = value;
            InvalidateVisual();
        }
    }

    public string? SelectedClipId
    {
        get => _selectedClip;
        set
        {
            _selectedClip = value;
            InvalidateVisual();
        }
    }

    public string? SelectedZoomId
    {
        get => _selectedZoom;
        set
        {
            _selectedZoom = value;
            InvalidateVisual();
        }
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 1, 8);
            InvalidateVisual();
        }
    }

    public void SetFrames(IEnumerable<FilmFrame> frames)
    {
        _frames.Clear();
        _frames.AddRange(frames);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 10 || _project is null)
        {
            return;
        }

        var ink = BrushOf("InkBrush", Brushes.Black);
        var muted = BrushOf("InkSecondaryBrush", Brushes.Gray);
        var line = BrushOf("LineBrush", Brushes.LightGray);
        var card = BrushOf("CardBrush", Brushes.White);
        var well = BrushOf("WellBrush", Brushes.WhiteSmoke);
        var accent = BrushOf("AccentBrush", Brushes.Black);
        var zoomBrush = BrushOf("ZoomBrush", Brushes.Tan);
        var zoomInk = BrushOf("ZoomInkBrush", Brushes.SaddleBrown);
        var pixels = PixelsPerSecond(width);
        drawing.DrawRectangle(well, null, new Rect(0, ClipTop, width, ClipHeight));
        DrawRuler(drawing, width, pixels, muted);
        foreach (var clip in VisualClips())
        {
            var x = TimeToX(clip.Start, pixels);
            var w = Math.Max(8, clip.Duration * pixels);
            var rect = new Rect(x, ClipTop + 4, w, ClipHeight - 8);
            drawing.DrawRoundedRectangle(card, new Pen(clip.Id == _selectedClip ? accent : line, clip.Id == _selectedClip ? 1.6 : 1), rect, 8, 8);
            DrawFrames(drawing, clip, rect);
            var label = clip.Speed == 1 ? "" : clip.Speed.ToString("0.##", CultureInfo.InvariantCulture) + "×";
            if (label.Length > 0 && w > 36)
            {
                drawing.DrawText(Text(label, 11, muted), new Point(rect.Right - 28, rect.Bottom - 16));
            }
        }

        foreach (var zoom in VisualZooms())
        {
            var x = TimeToX(zoom.Start, pixels);
            var w = Math.Max(8, zoom.Duration * pixels);
            var rect = new Rect(x, ZoomTop, w, ZoomHeight);
            drawing.DrawRoundedRectangle(zoomBrush, new Pen(zoom.Id == _selectedZoom ? accent : null, 1.4), rect, 8, 8);
            if (w > 70)
            {
                drawing.DrawText(Text(zoom.Scale.ToString("0.0", CultureInfo.InvariantCulture) + "×", 12, zoomInk), new Point(rect.X + 8, rect.Y + 5));
            }
        }

        if (_project is not null)
        {
            foreach (var marker in _project.Markers)
            {
                var time = TimelineMath.MarkerTime(_project, marker);
                if (time is null)
                {
                    continue;
                }

                var x = TimeToX(time.Value, pixels);
                var geometry = Geometry.Parse($"M{x.ToString(CultureInfo.InvariantCulture)},8 L{x + 5},14 L{x},{20} L{x - 5},14 Z");
                drawing.DrawGeometry(accent, null, geometry);
            }
        }

        var playX = TimeToX(_playhead, pixels);
        drawing.DrawLine(new Pen(accent, 1.5), new Point(playX, 4), new Point(playX, height - 4));
        drawing.DrawEllipse(accent, null, new Point(playX, 6), 4, 4);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var pixels = PixelsPerSecond(ActualWidth);
        var time = XToTime(point.X, pixels);
        var zoomHit = HitZoom(point, pixels);
        if (zoomHit is not null)
        {
            _selectedZoom = zoomHit.Value.Zoom.Id;
            _selectedClip = null;
            ZoomSelected?.Invoke(this, zoomHit.Value.Zoom.Id);
            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _drag = new Drag(zoomHit.Value.Edge == 0 ? DragKind.MoveZoom : zoomHit.Value.Edge < 0 ? DragKind.ZoomStart : DragKind.ZoomEnd, zoomHit.Value.Zoom.Id, time);
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var clipHit = HitClip(point, pixels);
        if (clipHit is not null)
        {
            _selectedClip = clipHit.Value.Clip.Id;
            _selectedZoom = null;
            ClipSelected?.Invoke(this, clipHit.Value.Clip.Id);
            if (clipHit.Value.Edge != 0)
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                _drag = new Drag(clipHit.Value.Edge < 0 ? DragKind.TrimStart : DragKind.TrimEnd, clipHit.Value.Clip.Id, time);
                CaptureMouse();
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _drag = new Drag(DragKind.Seek, "", time);
        CaptureMouse();
        SeekRequested?.Invoke(this, Math.Max(0, time));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag is null || _project is null)
        {
            return;
        }

        var time = XToTime(e.GetPosition(this).X, PixelsPerSecond(ActualWidth));
        var delta = time - _drag.Origin;
        switch (_drag.Kind)
        {
            case DragKind.Seek:
                SeekRequested?.Invoke(this, Math.Clamp(time, 0, TimelineMath.Duration(_project)));
                break;
            case DragKind.TrimStart:
            case DragKind.TrimEnd:
            case DragKind.MoveZoom:
            case DragKind.ZoomStart:
            case DragKind.ZoomEnd:
                _drag.Delta = delta;
                InvalidateVisual();
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag is null)
        {
            return;
        }

        var drag = _drag;
        _drag = null;
        ReleaseMouseCapture();
        if (_project is null)
        {
            return;
        }

        switch (drag.Kind)
        {
            case DragKind.TrimStart:
            case DragKind.TrimEnd:
                var clip = _project.Clips.FirstOrDefault(item => item.Id == drag.Id);
                if (clip is not null)
                {
                    var (start, end) = TrimValues(clip, drag);
                    TrimFinished?.Invoke(this, new ClipTrim(drag.Id, start, end));
                }

                break;
            case DragKind.MoveZoom:
            case DragKind.ZoomStart:
            case DragKind.ZoomEnd:
                var zoom = _project.Zooms.FirstOrDefault(item => item.Id == drag.Id);
                if (zoom is not null)
                {
                    var moved = ZoomValues(zoom, drag);
                    ZoomFinished?.Invoke(this, new ZoomMove(drag.Id, moved.Start, moved.Duration));
                }

                break;
        }

        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        _scroll = Math.Max(0, _scroll - e.Delta * 0.15);
        InvalidateVisual();
        e.Handled = true;
    }

    double PixelsPerSecond(double width) =>
        Math.Max(48, (width - 16) / Math.Max(TimelineMath.Duration(_project ?? new ProjectDocument()), 0.1)) * _zoom;

    double TimeToX(double time, double pixels) => 8 + time * pixels - _scroll;

    double XToTime(double x, double pixels) => (x - 8 + _scroll) / pixels;

    IEnumerable<DrawnClip> VisualClips()
    {
        if (_project is null)
        {
            yield break;
        }

        double time = 0;
        foreach (var clip in _project.Clips)
        {
            var asset = _project.Asset(clip.AssetId);
            if (asset is null)
            {
                continue;
            }

            var sourceStart = clip.SourceStart;
            var sourceEnd = clip.SourceEnd;
            if (_drag is { } drag && drag.Id == clip.Id)
            {
                (sourceStart, sourceEnd) = TrimValues(clip, drag);
            }

            var duration = Math.Max(0, (sourceEnd - sourceStart) / clip.Speed);
            yield return new DrawnClip(clip.Id, clip.AssetId, time, duration, sourceStart, sourceEnd, clip.Speed);
            time += duration;
        }
    }

    IEnumerable<ZoomSpan> VisualZooms()
    {
        if (_project is null)
        {
            yield break;
        }

        foreach (var zoom in _project.Zooms)
        {
            if (_drag is { } drag && drag.Id == zoom.Id && drag.Kind is DragKind.MoveZoom or DragKind.ZoomStart or DragKind.ZoomEnd)
            {
                var preview = ZoomValues(zoom, drag);
                yield return new ZoomSpan { Id = zoom.Id, Start = preview.Start, Duration = preview.Duration, Scale = zoom.Scale, FocusX = zoom.FocusX, FocusY = zoom.FocusY };
            }
            else
            {
                yield return zoom;
            }
        }
    }

    (double Start, double End) TrimValues(ClipSegment clip, Drag drag)
    {
        var asset = _project?.Asset(clip.AssetId);
        var limit = asset?.Duration ?? clip.SourceEnd;
        var start = clip.SourceStart;
        var end = clip.SourceEnd;
        var sourceDelta = drag.Delta * clip.Speed;
        if (drag.Kind == DragKind.TrimStart)
        {
            start = Math.Clamp(clip.SourceStart + sourceDelta, 0, end - 0.2 * clip.Speed);
        }
        else if (drag.Kind == DragKind.TrimEnd)
        {
            end = Math.Clamp(clip.SourceEnd + sourceDelta, start + 0.2 * clip.Speed, limit);
        }

        return (start, end);
    }

    (double Start, double Duration) ZoomValues(ZoomSpan zoom, Drag drag)
    {
        var total = Math.Max(0.2, TimelineMath.Duration(_project ?? new ProjectDocument()));
        var start = zoom.Start;
        var duration = zoom.Duration;
        if (drag.Kind == DragKind.MoveZoom)
        {
            start = Math.Clamp(zoom.Start + drag.Delta, 0, total - duration);
        }
        else if (drag.Kind == DragKind.ZoomStart)
        {
            var end = zoom.Start + zoom.Duration;
            start = Math.Clamp(zoom.Start + drag.Delta, 0, end - 0.2);
            duration = end - start;
        }
        else if (drag.Kind == DragKind.ZoomEnd)
        {
            duration = Math.Clamp(zoom.Duration + drag.Delta, 0.2, total - zoom.Start);
        }

        return (start, duration);
    }

    (ClipSegment Clip, int Edge)? HitClip(Point point, double pixels)
    {
        if (point.Y < ClipTop || point.Y > ClipTop + ClipHeight)
        {
            return null;
        }

        foreach (var visual in VisualClips())
        {
            var clip = _project!.Clips.First(item => item.Id == visual.Id);
            var x = TimeToX(visual.Start, pixels);
            var right = x + visual.Duration * pixels;
            if (point.X >= x && point.X <= right)
            {
                if (point.X - x < 8)
                {
                    return (clip, -1);
                }

                if (right - point.X < 8)
                {
                    return (clip, 1);
                }

                return (clip, 0);
            }
        }

        return null;
    }

    (ZoomSpan Zoom, int Edge)? HitZoom(Point point, double pixels)
    {
        if (_project is null || point.Y < ZoomTop || point.Y > ZoomTop + ZoomHeight)
        {
            return null;
        }

        foreach (var zoom in _project.Zooms)
        {
            var x = TimeToX(zoom.Start, pixels);
            var right = x + zoom.Duration * pixels;
            if (point.X >= x && point.X <= right)
            {
                var edge = point.X - x < 8 ? -1 : right - point.X < 8 ? 1 : 0;
                return (zoom, edge);
            }
        }

        return null;
    }

    void DrawFrames(DrawingContext drawing, DrawnClip clip, Rect rect)
    {
        var frames = _frames.Where(frame => frame.AssetId == clip.AssetId && frame.SourceTime >= clip.SourceStart && frame.SourceTime <= clip.SourceEnd).ToList();
        if (frames.Count == 0)
        {
            return;
        }

        drawing.PushClip(new RectangleGeometry(rect, 8, 8));
        var slot = Math.Max(46, rect.Height * 1.4);
        for (var x = rect.X; x < rect.Right; x += slot)
        {
            var ratio = rect.Width <= 1 ? 0 : (x - rect.X) / rect.Width;
            var sourceTime = clip.SourceStart + (clip.SourceEnd - clip.SourceStart) * ratio;
            var frame = frames.MinBy(item => Math.Abs(item.SourceTime - sourceTime));
            if (frame is not null)
            {
                drawing.DrawImage(frame.Image, new Rect(x, rect.Y, slot, rect.Height));
            }
        }

        drawing.Pop();
    }

    void DrawRuler(DrawingContext drawing, double width, double pixels, Brush muted)
    {
        var step = pixels > 140 ? 1 : pixels > 70 ? 2 : 5;
        var duration = TimelineMath.Duration(_project!);
        for (double time = 0; time <= duration + 0.01; time += step)
        {
            var x = TimeToX(time, pixels);
            if (x < 0 || x > width)
            {
                continue;
            }

            drawing.DrawLine(new Pen(muted, 1), new Point(x, 16), new Point(x, 24));
            drawing.DrawText(Text(TimeLabel(time), 11, muted), new Point(x + 2, 2));
        }
    }

    static string TimeLabel(double time)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, time));
        return span.ToString(span.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
    }

    static FormattedText Text(string value, double size, Brush brush) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1);

    static Brush BrushOf(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    enum DragKind
    {
        Seek,
        TrimStart,
        TrimEnd,
        MoveZoom,
        ZoomStart,
        ZoomEnd,
    }

    sealed class Drag(DragKind kind, string id, double origin)
    {
        public DragKind Kind { get; } = kind;
        public string Id { get; } = id;
        public double Origin { get; } = origin;
        public double Delta { get; set; }
    }

    readonly record struct DrawnClip(string Id, string AssetId, double Start, double Duration, double SourceStart, double SourceEnd, double Speed);
}

public readonly record struct ClipTrim(string ClipId, double SourceStart, double SourceEnd);

public readonly record struct ZoomMove(string ZoomId, double Start, double Duration);
