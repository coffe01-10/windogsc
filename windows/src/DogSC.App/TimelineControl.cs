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
    readonly Dictionary<string, float[]> _waves = new(StringComparer.Ordinal);
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
    public event EventHandler<ClipMove>? ClipMoved;
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

    /// <summary>某个素材的简化波形峰值（0..1，等距桶），异步算好后调一次。</summary>
    public void SetWaveform(string assetId, float[] peaks)
    {
        _waves[assetId] = peaks;
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
            var moving = _drag is { Kind: DragKind.MoveClip } && _drag.Id == clip.Id;
            if (moving)
            {
                // 拖动的片段画在跟随鼠标的位置，原位置留个空槽轮廓。
                drawing.DrawRoundedRectangle(null, new Pen(line, 1) { DashStyle = DashStyles.Dash }, rect, 8, 8);
                var preview = new Rect(rect.X + _drag!.Delta * pixels, rect.Y, rect.Width, rect.Height);
                drawing.PushOpacity(0.65);
                drawing.DrawRoundedRectangle(card, new Pen(accent, 1.6), preview, 8, 8);
                drawing.Pop();
                continue;
            }

            drawing.DrawRoundedRectangle(card, new Pen(clip.Id == _selectedClip ? accent : line, clip.Id == _selectedClip ? 1.6 : 1), rect, 8, 8);
            DrawFrames(drawing, clip, rect);
            DrawWave(drawing, clip, rect, muted);
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
            InteractionStarted?.Invoke(this, EventArgs.Empty);
            var kind = clipHit.Value.Edge < 0 ? DragKind.TrimStart
                : clipHit.Value.Edge > 0 ? DragKind.TrimEnd
                : DragKind.MoveClip;
            _drag = new Drag(kind, clipHit.Value.Clip.Id, time);
            CaptureMouse();
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

        var pixels = PixelsPerSecond(ActualWidth);
        var time = XToTime(e.GetPosition(this).X, pixels);
        var delta = time - _drag.Origin;
        switch (_drag.Kind)
        {
            case DragKind.Seek:
                SeekRequested?.Invoke(this, Math.Clamp(time, 0, TimelineMath.Duration(_project)));
                break;
            case DragKind.MoveClip:
            case DragKind.TrimStart:
            case DragKind.TrimEnd:
            case DragKind.MoveZoom:
            case DragKind.ZoomStart:
            case DragKind.ZoomEnd:
                _drag.Delta = SnapDelta(_drag, delta, pixels);
                _drag.Moved = true;
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
                    var (start, end) = TrimValues(clip, drag.Kind, drag.Delta);
                    TrimFinished?.Invoke(this, new ClipTrim(drag.Id, start, end));
                }

                break;
            case DragKind.MoveClip:
                // 只是点了一下（没真的拖）不算重排。
                if (drag.Moved)
                {
                    var index = MoveTargetIndex(drag, XToTime(e.GetPosition(this).X, PixelsPerSecond(ActualWidth)));
                    if (index >= 0)
                    {
                        ClipMoved?.Invoke(this, new ClipMove(drag.Id, index));
                    }
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

    /// <summary>
    /// 吸附：把拖拽 delta 调整到最近的候选点（其它片段边界、缩放边界、
    /// 标记点）。阈值 6 个屏幕像素换算成秒。
    /// </summary>
    double SnapDelta(Drag drag, double delta, double pixels)
    {
        if (_project is null)
        {
            return delta;
        }

        var threshold = 6 / pixels;
        // 正在拖的那条边当前落在时间线上的位置。
        double? edge = drag.Kind switch
        {
            DragKind.TrimStart or DragKind.TrimEnd => ClipDragEdge(drag, delta),
            DragKind.MoveZoom => ZoomOrigin(drag) + delta,
            DragKind.ZoomStart => ZoomOrigin(drag) + delta,
            DragKind.ZoomEnd => ZoomOrigin(drag) + ZoomDuration(drag) + delta,
            DragKind.MoveClip => MoveClipEdge(drag, delta),
            _ => null,
        };
        if (edge is null)
        {
            return delta;
        }

        var snapped = TimelineMath.Snap(edge.Value, SnapTargets(drag.Id), threshold);
        return delta + (snapped - edge.Value);
    }

    double ClipDragEdge(Drag drag, double delta)
    {
        var clip = _project?.Clips.FirstOrDefault(item => item.Id == drag.Id);
        if (clip is null)
        {
            return 0;
        }

        var (start, end) = TrimValues(clip, drag.Kind, delta);
        var placed = TimelineMath.Place(_project!).FirstOrDefault(item => item.Clip.Id == drag.Id);
        // 拖的是「会动的那条边」：左缘拖动改变时长→右缘动；右缘同理。
        return (placed?.Start ?? 0) + Math.Max(0, (end - start) / clip.Speed);
    }

    double ZoomOrigin(Drag drag) =>
        _project?.Zooms.FirstOrDefault(item => item.Id == drag.Id)?.Start ?? 0;

    double ZoomDuration(Drag drag) =>
        _project?.Zooms.FirstOrDefault(item => item.Id == drag.Id)?.Duration ?? 0;

    double? MoveClipEdge(Drag drag, double delta)
    {
        var clip = _project?.Clips.FirstOrDefault(item => item.Id == drag.Id);
        if (clip is null)
        {
            return null;
        }

        var placed = TimelineMath.Place(_project!).FirstOrDefault(item => item.Clip.Id == drag.Id);
        return placed is null ? null : placed.Start + delta;
    }

    IEnumerable<double> SnapTargets(string excludeId)
    {
        if (_project is null)
        {
            yield break;
        }

        foreach (var placed in TimelineMath.Place(_project))
        {
            if (placed.Clip.Id == excludeId)
            {
                continue;
            }

            yield return placed.Start;
            yield return placed.End;
        }

        foreach (var zoom in _project.Zooms)
        {
            if (zoom.Id == excludeId)
            {
                continue;
            }

            yield return zoom.Start;
            yield return zoom.Start + zoom.Duration;
        }

        foreach (var marker in _project.Markers)
        {
            var time = TimelineMath.MarkerTime(_project, marker);
            if (time is not null)
            {
                yield return time.Value;
            }
        }
    }

    /// <summary>松手时按鼠标落点算片段应插入的位置（跳过自身）。</summary>
    int MoveTargetIndex(Drag drag, double time)
    {
        if (_project is null)
        {
            return -1;
        }

        var placed = TimelineMath.Place(_project);
        var from = placed.ToList().FindIndex(item => item.Clip.Id == drag.Id);
        if (from < 0)
        {
            return -1;
        }

        // 用鼠标点落到哪个片段的中点之前来决定插入位。
        var index = 0;
        for (var i = 0; i < placed.Count; i++)
        {
            if (i == from)
            {
                continue;
            }

            if (time > placed[i].Start + placed[i].Duration / 2)
            {
                index = i > from ? i : i + 1;
            }
        }

        return Math.Clamp(index, 0, _project.Clips.Count - 1);
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
            if (_drag is { } drag && drag.Id == clip.Id && drag.Kind is DragKind.TrimStart or DragKind.TrimEnd)
            {
                (sourceStart, sourceEnd) = TrimValues(clip, drag.Kind, drag.Delta);
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

    (double Start, double End) TrimValues(ClipSegment clip, DragKind kind, double delta)
    {
        var asset = _project?.Asset(clip.AssetId);
        var limit = asset?.Duration ?? clip.SourceEnd;
        var start = clip.SourceStart;
        var end = clip.SourceEnd;
        var sourceDelta = delta * clip.Speed;
        if (kind == DragKind.TrimStart)
        {
            start = Math.Clamp(clip.SourceStart + sourceDelta, 0, end - 0.2 * clip.Speed);
        }
        else if (kind == DragKind.TrimEnd)
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

    /// <summary>在片段底部画简化波形：峰值来自 EditorWindow 异步填充的 _waves。</summary>
    void DrawWave(DrawingContext drawing, DrawnClip clip, Rect rect, Brush brush)
    {
        if (!_waves.TryGetValue(clip.AssetId, out var peaks) || peaks.Length == 0)
        {
            return;
        }

        var asset = _project?.Asset(clip.AssetId);
        var span = Math.Max(0.01, asset?.Duration ?? clip.SourceEnd);
        var band = Math.Min(14, rect.Height / 3);
        var midY = rect.Bottom - band / 2 - 2;
        drawing.PushClip(new RectangleGeometry(rect, 8, 8));
        var pen = new Pen(brush, 1);
        for (var x = rect.X + 2; x < rect.Right - 2; x += 2)
        {
            var ratio = (x - rect.X) / rect.Width;
            var sourceTime = clip.SourceStart + (clip.SourceEnd - clip.SourceStart) * ratio;
            var bucket = (int)Math.Clamp(sourceTime / span * peaks.Length, 0, peaks.Length - 1);
            var half = Math.Max(0.8, peaks[bucket] * band / 2);
            drawing.DrawLine(pen, new Point(x, midY - half), new Point(x, midY + half));
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
        MoveClip,
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
        public bool Moved { get; set; }
    }

    readonly record struct DrawnClip(string Id, string AssetId, double Start, double Duration, double SourceStart, double SourceEnd, double Speed);
}

public readonly record struct ClipTrim(string ClipId, double SourceStart, double SourceEnd);

public readonly record struct ClipMove(string ClipId, int Index);

public readonly record struct ZoomMove(string ZoomId, double Start, double Duration);
