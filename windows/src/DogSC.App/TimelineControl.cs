using System.Globalization;
using DogSC.Core;

namespace DogSC;

public sealed record FilmFrame(string AssetId, double SourceTime, ImageSource Image);

public sealed class TimelineControl : FrameworkElement
{
    public static readonly DependencyProperty LaneBackgroundProperty = DependencyProperty.Register(
        nameof(LaneBackground), typeof(Brush), typeof(TimelineControl),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush LaneBackground { get => (Brush)GetValue(LaneBackgroundProperty); set => SetValue(LaneBackgroundProperty, value); }

    const double RulerHeight = 26;
    const double ClipTop = 32;
    const double ClipHeight = 74;
    const double ZoomTop = 114;
    const double ZoomHeight = 30;
    const double LaneLabelWidth = 116;

    ProjectDocument? _project;
    readonly List<FilmFrame> _frames = [];
    readonly Dictionary<string, float[]> _waves = new(StringComparer.Ordinal);
    double _playhead;
    double _zoom = 1;
    double _scroll;
    double _duration;
    string? _selectedClip;
    string? _selectedZoom;
    Drag? _drag;
    readonly DrawingVisual _playheadVisual = new();
    readonly TranslateTransform _playheadOffset = new();

    public event EventHandler? TrackMenuRequested;
    public event EventHandler<int>? TrackHidden;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler? SeekFinished;
    public event EventHandler? InteractionStarted;
    public event EventHandler<ClipTrim>? TrimFinished;
    public event EventHandler<(string ClipId, double Speed)>? SpeedFinished;
    public event EventHandler<ZoomMove>? ZoomFinished;
    public event EventHandler<ZoomMove>? DecorationFinished;
    public event EventHandler<ClipMove>? ClipMoved;
    public event EventHandler<string>? ClipSelected;
    public event EventHandler<string>? ZoomSelected;
    public event EventHandler<string>? DecorationSelected;
    /// <summary>右键菜单动作："split"/"speed:1.5"/"ripple-before"/"ripple-after"/"delete"。</summary>
    public event EventHandler<(string Id, string Action)>? ClipMenuAction;
    /// <summary>右键删除标记（标记录制回看标记）。</summary>
    public event EventHandler<SourceMarker>? MarkerDeleteRequested;
    /// <summary>吸附开关（对齐 Mac Snap 开关）：关闭后拖动不按刻度/边缘吸附。</summary>
    public bool SnapEnabled { get; set; } = true;
    bool _showWaveform, _showMarkers = true;
    public bool ShowWaveform { get => _showWaveform; set { _showWaveform = value; InvalidateVisual(); } }
    public bool ShowMarkers { get => _showMarkers; set { _showMarkers = value; InvalidateVisual(); } }
    string? _selectedDecoration;
    public string? SelectedDecorationId { get => _selectedDecoration; set { _selectedDecoration = value; InvalidateVisual(); } }

    public TimelineControl()
    {
        MinHeight = 202;
        Focusable = true;
        Cursor = Cursors.Arrow;
        ClipToBounds = true;
        _playheadVisual.Transform = _playheadOffset;
        AddVisualChild(_playheadVisual);
        // The retained drawing must repaint when the theme dictionary changes,
        // even when the pointer and project remain stationary.
        SetResourceReference(LaneBackgroundProperty, "CardBrush");
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? _playheadVisual : throw new ArgumentOutOfRangeException(nameof(index));

    public ProjectDocument? Project
    {
        get => _project;
        set
        {
            _project = value;
            _duration = value is null ? 0 : TimelineMath.Duration(value);
            UpdateLaneHeight();
            InvalidateVisual();
        }
    }

    public double Playhead
    {
        get => _playhead;
        set
        {
            if (_playhead == value) return;
            _playhead = value;
            MovePlayhead();
        }
    }

    public bool IsScrubbing => _drag?.Kind == DragKind.Seek;

    public string? SelectedClipId
    {
        get => _selectedClip;
        set
        {
            if (_selectedClip == value) return;
            _selectedClip = value;
            InvalidateVisual();
        }
    }

    public string? SelectedZoomId
    {
        get => _selectedZoom;
        set
        {
            if (_selectedZoom == value) return;
            _selectedZoom = value;
            InvalidateVisual();
        }
    }

    /// <summary>轨道显示位集：1=缩放 2=屏幕运动 4=摄像头运动 8=叠加（贴图/柔化）。只收起行，不停用效果。</summary>
    int _trackFlags = 0xF;
    public int TrackFlags { get => _trackFlags; set { _trackFlags = value; UpdateLaneHeight(); InvalidateVisual(); } }

    void UpdateLaneHeight() => MinHeight = Math.Max(168, Decorations().Select(x => x.Top + 38).DefaultIfEmpty(168).Max());

    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 1, 8);
            InvalidateVisual();
        }
    }

    public double PasteTime => IsMouseOver
        ? Math.Max(0, XToTime(Mouse.GetPosition(this).X, PixelsPerSecond(ActualWidth)))
        : _playhead;

    public double SnapThreshold => 6 / PixelsPerSecond(ActualWidth);

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
        var card = LaneBackground;
        var well = BrushOf("WellBrush", Brushes.WhiteSmoke);
        var accent = BrushOf("AccentBrush", Brushes.Black);
        var zoomBrush = BrushOf("ZoomBrush", Brushes.Tan);
        var zoomInk = BrushOf("ZoomInkBrush", Brushes.SaddleBrown);
        var pixels = PixelsPerSecond(width);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, TimelineMath.Duration(_project) * pixels - width + LaneLabelWidth + 16));
        // 空白轨道也要参与命中测试，才能定位播放头和鼠标粘贴位置。
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        DrawLaneLabels(drawing, ink, muted, line, card);
        drawing.PushClip(new RectangleGeometry(new Rect(LaneLabelWidth, 0, Math.Max(0, width - LaneLabelWidth), height)));
        drawing.DrawRectangle(well, null, new Rect(LaneLabelWidth, ClipTop, Math.Max(0, width - LaneLabelWidth), ClipHeight));
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

            drawing.DrawRoundedRectangle(card, null, rect, 8, 8);
            if (ShowWaveform) DrawWave(drawing, clip, rect, muted);
            else DrawFrames(drawing, clip, rect);
            drawing.DrawRoundedRectangle(null, new Pen(clip.Id == _selectedClip ? accent : line, clip.Id == _selectedClip ? 2 : 1), rect, 8, 8);
            if (clip.Id == _selectedClip && w > 28)
            {
                foreach (var edge in new[] { rect.Left + 3, rect.Right - 9 })
                {
                    drawing.DrawRoundedRectangle(accent, null, new Rect(edge, rect.Top + 12, 6, rect.Height - 24), 2, 2);
                    drawing.DrawLine(new Pen(BrushOf("OnAccentBrush", Brushes.White), 1), new Point(edge + 3, rect.Top + 20), new Point(edge + 3, rect.Bottom - 20));
                }
            }
            if (w > 64)
            {
                var durationLabel = Text(TimeSpan.FromSeconds(clip.Duration).ToString(@"m\:ss\.ff"), 10, Brushes.White);
                var badge = new Rect(rect.Right - durationLabel.Width - 12, rect.Bottom - 23, durationLabel.Width + 8, 18);
                drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(150, 90, 90, 90)), null, badge, 6, 6);
                drawing.DrawText(durationLabel, new Point(badge.Left + 4, badge.Top + 2));
            }
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
                var label = Text(L.Format("缩放 {0:0.#}× · {1}", zoom.Scale, L.Text(zoom.Origin == "automatic" ? "自动" : "手动")), 11, zoomInk);
                label.MaxTextWidth = Math.Max(1, rect.Width - 16); label.Trimming = TextTrimming.CharacterEllipsis;
                drawing.DrawText(label, new Point(rect.X + Math.Max(8, (rect.Width - label.Width) / 2), rect.Y + 8));
            }
        }

        foreach (var (id, title, window, top) in Decorations())
        {
            var span = new ZoomSpan { Id = id, Start = window.Start, Duration = window.Duration };
            var visual = _drag is { } moving && moving.Id == id ? ZoomValues(span, moving) : (span.Start, span.Duration);
            var rect = new Rect(TimeToX(visual.Item1, pixels), top, Math.Max(8, visual.Item2 * pixels), 26);
            var fill = BrushOf(_project.ScreenMotions.Any(x => x.Id == id) || _project.CameraMotions.Any(x => x.Id == id) ? "MotionTrackBrush"
                : _project.Canvas.SoftRegions.Any(x => x.Id == id) ? "BlurTrackBrush" : "StickerTrackBrush", zoomBrush);
            drawing.DrawRoundedRectangle(fill, new Pen(id == SelectedDecorationId ? accent : muted, id == SelectedDecorationId ? 1.4 : .5), rect, 6, 6);
            if (rect.Width > 48)
            {
                var label = Text(title, 11, ink); label.MaxTextWidth = Math.Max(1, rect.Width - 16); label.Trimming = TextTrimming.CharacterEllipsis;
                drawing.DrawText(label, new Point(rect.X + 8, rect.Y + 5));
            }
            drawing.DrawLine(new Pen(zoomInk, 1), new Point(rect.Left + 4, rect.Top + 7), new Point(rect.Left + 4, rect.Bottom - 7));
            drawing.DrawLine(new Pen(zoomInk, 1), new Point(rect.Right - 4, rect.Top + 7), new Point(rect.Right - 4, rect.Bottom - 7));
        }
        if (_project is not null && ShowMarkers)
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

        drawing.Pop();
        DrawPlayhead();
    }

    void DrawPlayhead()
    {
        using var drawing = _playheadVisual.RenderOpen();
        if (_project is null || ActualWidth < 10 || ActualHeight < 10) return;
        var accent = BrushOf("AccentBrush", Brushes.Black);
        drawing.DrawLine(new Pen(accent, 1.5), new Point(0, 4), new Point(0, ActualHeight - 4));
        drawing.DrawEllipse(accent, null, new Point(0, 6), 4, 4);
        MovePlayhead();
    }

    void MovePlayhead()
    {
        _playheadOffset.X = TimeToX(_playhead, PixelsPerSecond(ActualWidth));
        _playheadVisual.Opacity = _playheadOffset.X >= LaneLabelWidth ? 1 : 0;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        Focus();
        InteractionStarted?.Invoke(this, EventArgs.Empty);
        var point = e.GetPosition(this);
        var pixels = PixelsPerSecond(ActualWidth);
        if (point.X < LaneLabelWidth) { HandleLaneClick(point); e.Handled = true; return; }
        // The visible pointer is a drag handle, including the line over a clip.
        if (Math.Abs(point.X - TimeToX(_playhead, pixels)) <= 5)
        {
            BeginSeek(XToTime(point.X, pixels));
            e.Handled = true;
            return;
        }
        var decoration = HitDecoration(point, pixels);
        if (decoration is { } effect)
        {
            SelectedDecorationId = effect.Id;
            DecorationSelected?.Invoke(this, effect.Id);
            InvalidateVisual();
            _drag = new Drag(effect.Edge < 0 ? DragKind.ZoomStart : effect.Edge > 0 ? DragKind.ZoomEnd : DragKind.MoveZoom, effect.Id, XToTime(point.X, pixels));
            CaptureMouse();
            e.Handled = true;
            return;
        }
        var time = XToTime(point.X, pixels);
        var zoomHit = HitZoom(point, pixels);
        if (zoomHit is not null)
        {
            _selectedZoom = zoomHit.Value.Zoom.Id;
            _selectedClip = null;
            ZoomSelected?.Invoke(this, zoomHit.Value.Zoom.Id);
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
            var kind = clipHit.Value.Edge < 0 ? DragKind.TrimStart
                : clipHit.Value.Edge > 0 ? (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? DragKind.Speed : DragKind.TrimEnd)
                : DragKind.MoveClip;
            _drag = new Drag(kind, clipHit.Value.Clip.Id, time);
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        BeginSeek(time);
        e.Handled = true;
    }

    void BeginSeek(double time)
    {
        _drag = new Drag(DragKind.Seek, "", time);
        CaptureMouse();
        CompositionTarget.Rendering += RenderScrub;
        Playhead = Math.Clamp(time, 0, _duration);
        SeekRequested?.Invoke(this, Playhead);
    }

    void RenderScrub(object? sender, EventArgs e)
    {
        // Sample the current pointer once per displayed frame, even when mouse
        // messages are waiting behind a video update on the dispatcher.
        UpdateSeek(Mouse.GetPosition(this).X);
    }

    void UpdateSeek(double x)
    {
        var time = Math.Clamp(XToTime(x, PixelsPerSecond(ActualWidth)), 0, _duration);
        if (_playhead == time) return;
        Playhead = time;
        SeekRequested?.Invoke(this, time);
    }

    SourceMarker? HitMarker(Point point, double pixels)
    {
        if (_project is null || !ShowMarkers) return null;
        foreach (var marker in _project.Markers)
        {
            var time = TimelineMath.MarkerTime(_project, marker);
            if (time is null) continue;
            var x = TimeToX(time.Value, pixels);
            if (Math.Abs(point.X - x) < 7 && point.Y >= 6 && point.Y <= 22) return marker;
        }
        return null;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (_project is null) return;
        var point = e.GetPosition(this);
        var pixels = PixelsPerSecond(ActualWidth);
        if (point.X < LaneLabelWidth) { TrackMenuRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
        if (HitMarker(point, pixels) is { } marker)
        {
            var menu = new ContextMenu();
            var item = new MenuItem { Header = L.Text("删除标记") };
            item.Click += (_, _) => MarkerDeleteRequested?.Invoke(this, marker);
            menu.Items.Add(item);
            menu.IsOpen = true;
            e.Handled = true;
            return;
        }
        if (HitClip(point, pixels) is { } hit)
        {
            var id = hit.Clip.Id;
            var menu = new ContextMenu();
            void Add(string title, string action, bool enabled = true)
            {
                var item = new MenuItem { Header = title, IsEnabled = enabled };
                item.Click += (_, _) => ClipMenuAction?.Invoke(this, (id, action));
                menu.Items.Add(item);
            }
            var placedClip = TimelineMath.Place(_project).First(c => c.Clip.Id == id);
            var insideClip = Playhead > placedClip.Start && Playhead < placedClip.End;
            Add(L.Text("在播放头切开"), "split", insideClip);
            var speed = new MenuItem { Header = L.Text("片段速度") };
            foreach (var (label, value) in new[] { (L.Text("1.0x（默认）"), "1"), ("1.5x", "1.5"), ("2x", "2"), ("4x", "4"), ("8x", "8"), ("12x", "12"), ("20x", "20"), ("50x", "50"), ("100x", "100") })
            {
                var sub = new MenuItem { Header = label };
                var v = value;
                sub.Click += (_, _) => ClipMenuAction?.Invoke(this, (id, "speed:" + v));
                speed.Items.Add(sub);
            }
            menu.Items.Add(speed);
            menu.Items.Add(new Separator());
            Add(L.Text("波纹删除播放头前"), "ripple-before", insideClip);
            Add(L.Text("波纹删除播放头后"), "ripple-after", insideClip);
            menu.Items.Add(new Separator());
            Add(L.Text("删除片段"), "delete", _project.Clips.Count > 1);
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var pixels = PixelsPerSecond(ActualWidth);
        var point = e.GetPosition(this);
        if (_drag is null)
        {
            var edge = HitDecoration(point, pixels)?.Edge ?? HitZoom(point, pixels)?.Edge ?? HitClip(point, pixels)?.Edge;
            Cursor = edge is < 0 or > 0 ? Cursors.SizeWE : edge == 0 ? Cursors.Hand : Cursors.Arrow;
            ToolTip = HitMarker(point, pixels) is { } marker
                ? L.Text("标记 ") + TimeSpan.FromSeconds(TimelineMath.MarkerTime(_project, marker) ?? 0).ToString(@"mm\:ss\.f")
                : null;
            return;
        }
        var time = XToTime(point.X, pixels);
        var delta = time - _drag.Origin;
        switch (_drag.Kind)
        {
            case DragKind.Seek:
                UpdateSeek(point.X);
                break;
            case DragKind.MoveClip:
            case DragKind.TrimStart:
            case DragKind.TrimEnd:
            case DragKind.Speed:
            case DragKind.MoveZoom:
            case DragKind.ZoomStart:
            case DragKind.ZoomEnd:
                if (!_drag.Moved && Math.Abs(delta * pixels) < SystemParameters.MinimumHorizontalDragDistance) break;
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
        if (drag.Kind == DragKind.Seek) CompositionTarget.Rendering -= RenderScrub;
        _drag = null;
        ReleaseMouseCapture();
        if (_project is null)
        {
            return;
        }

        if (!drag.Moved && drag.Kind != DragKind.Seek)
        {
            InvalidateVisual();
            return;
        }
        switch (drag.Kind)
        {
            case DragKind.Speed:
                var speedClip = _project.Clips.FirstOrDefault(item => item.Id == drag.Id);
                if (speedClip is not null) SpeedFinished?.Invoke(this, (drag.Id, DragSpeed(speedClip, drag.Delta)));
                break;
            case DragKind.Seek:
                Playhead = Math.Clamp(XToTime(e.GetPosition(this).X, PixelsPerSecond(ActualWidth)), 0, _duration);
                SeekRequested?.Invoke(this, Playhead);
                SeekFinished?.Invoke(this, EventArgs.Empty);
                break;
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
                    var placed = TimelineMath.Place(_project).First(item => item.Clip.Id == drag.Id);
                    var index = MoveTargetIndex(drag, placed.Start + drag.Delta + placed.Duration / 2);
                    if (index >= 0)
                    {
                        ClipMoved?.Invoke(this, new ClipMove(drag.Id, index));
                    }
                }

                break;
            case DragKind.MoveZoom:
            case DragKind.ZoomStart:
            case DragKind.ZoomEnd:
                var zoom = Span(drag.Id);
                if (zoom is not null)
                {
                    var moved = ZoomValues(zoom, drag);
                    if (Decorations().Any(effect => effect.Id == drag.Id))
                        DecorationFinished?.Invoke(this, new ZoomMove(drag.Id, moved.Start, moved.Duration));
                    else ZoomFinished?.Invoke(this, new ZoomMove(drag.Id, moved.Start, moved.Duration));
                }

                break;
        }

        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && Parent is ScrollViewer viewer && ActualHeight > viewer.ViewportHeight + 1)
        { base.OnMouseWheel(e); return; }
        var max = Math.Max(0, TimelineMath.Duration(_project ?? new ProjectDocument()) * PixelsPerSecond(ActualWidth) - ActualWidth + LaneLabelWidth + 16);
        _scroll = Math.Clamp(_scroll - e.Delta * 0.25, 0, max);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        var seeking = IsScrubbing;
        CompositionTarget.Rendering -= RenderScrub;
        _drag = null;
        if (seeking) SeekFinished?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drag is not null)
        {
            var seeking = IsScrubbing;
            CompositionTarget.Rendering -= RenderScrub;
            _drag = null;
            ReleaseMouseCapture();
            if (seeking) SeekFinished?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }
        base.OnKeyDown(e);
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

        var snapped = SnapEnabled ? TimelineMath.Snap(edge.Value, SnapTargets(drag.Id), threshold) : edge.Value;
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
        Span(drag.Id)?.Start ?? 0;

    double ZoomDuration(Drag drag) =>
        Span(drag.Id)?.Duration ?? 0;

    ZoomSpan? Span(string id)
    {
        foreach (var effect in Decorations())
            if (effect.Id == id) return new ZoomSpan { Id = id, Start = effect.Window.Start, Duration = effect.Window.Duration };
        return _project?.Zooms.FirstOrDefault(item => item.Id == id);
    }

    static EffectWindow MotionWindow(TransitionTiming timing) => new()
    {
        Start = timing.StartTime,
        Duration = timing.Duration > 0 ? timing.Duration + Math.Max(0, timing.ReturnDuration) : 0,
        Fade = 0,
    };

    sealed record DecorationLane(string Title, IconKind Icon, int Bit, double Top, double Height,
        List<(string Id, string Title, EffectWindow Window, double Top)> Effects);

    IEnumerable<DecorationLane> DecorationLanes()
    {
        if (_project is null) yield break;
        var top = (TrackFlags & 1) != 0 ? ZoomTop + 48 : ZoomTop;
        var groups = new[]
        {
            (Title: L.Text("屏幕 3D"), Icon: IconKind.Cube, Bit: 2,
                Effects: _project.ScreenMotions.Select((x, i) => (x.Id, Title: L.Text("过渡") + $" {i + 1} · {TimeSpan.FromSeconds(x.Timing.Duration).ToString(@"m\:ss\.ff")}", Timing: MotionWindow(x.Timing)))),
            (Title: L.Text("摄像头运动"), Icon: IconKind.Camera, Bit: 4,
                Effects: _project.CameraMotions.Select((x, i) => (x.Id, Title: L.Text("过渡") + $" {i + 1} · {TimeSpan.FromSeconds(x.Timing.Duration).ToString(@"m\:ss\.ff")}", Timing: MotionWindow(x.Timing)))),
            (Title: L.Text("叠加"), Icon: IconKind.Layers, Bit: 8,
                Effects: _project.Canvas.Stickers.Select((x, i) => (x.Id, Title: L.Text("贴图") + $" {i + 1}", Timing: x.Timing))
                    .Concat(_project.Canvas.SoftRegions.Where(x => x.Enabled).Select(x => (x.Id, Title: x.Style == "spotlight" ? L.Text("聚光") : L.Text("柔化"), Timing: x.Timing)))),
        };
        foreach (var group in groups)
        {
            if ((TrackFlags & group.Bit) == 0) continue;
            var ends = new List<double>();
            var effects = new List<(string Id, string Title, EffectWindow Window, double Top)>();
            foreach (var effect in group.Effects.OrderBy(x => x.Timing.Duration <= 0 ? 0 : x.Timing.Start))
            {
                var window = effect.Timing.Duration > 0 ? effect.Timing : new EffectWindow { Duration = _duration, Fade = 0 };
                var lane = ends.FindIndex(end => end <= window.Start);
                if (lane < 0) { lane = ends.Count; ends.Add(0); }
                ends[lane] = window.Start + window.Duration;
                effects.Add((effect.Id, effect.Title, window, top + lane * 34));
            }
            if (effects.Count == 0) continue;
            var height = Math.Max(48, ends.Count * 34 + 14);
            yield return new(group.Title, group.Icon, group.Bit, top, height, effects);
            top += height;
        }
    }

    IEnumerable<(string Id, string Title, EffectWindow Window, double Top)> Decorations() =>
        DecorationLanes().SelectMany(lane => lane.Effects);

    void DrawLaneLabels(DrawingContext drawing, Brush ink, Brush muted, Brush line, Brush card)
    {
        drawing.DrawRectangle(card, null, new Rect(0, 0, LaneLabelWidth, ActualHeight));
        drawing.DrawLine(new Pen(line, .75), new Point(LaneLabelWidth, 0), new Point(LaneLabelWidth, ActualHeight));
        void Symbol(IconKind kind, double x, double y, double size = 14)
        {
            drawing.PushTransform(new TranslateTransform(x, y)); Icon.DrawSymbol(drawing, kind, muted, size, size); drawing.Pop();
        }
        void Label(string title, IconKind kind, double top, double height, bool hide)
        {
            drawing.DrawLine(new Pen(line, .5), new Point(0, top - 6), new Point(ActualWidth, top - 6));
            var y = top + height / 2 - 8;
            Symbol(kind, 12, y); var text = Text(title, 11, ink); text.MaxTextWidth = 68;
            drawing.DrawText(text, new Point(32, y + 1));
            if (hide) Symbol(IconKind.Eye, 96, y + 2, 12);
        }
        Symbol(IconKind.Folder, 12, 4, 12);
        drawing.DrawText(Text(L.Text("轨道"), 11, ink), new Point(31, 4));
        Symbol(IconKind.ChevronDown, 98, 5, 10);
        Label(L.Text("屏幕录制"), IconKind.Monitor, ClipTop, ClipHeight, false);
        if ((TrackFlags & 1) != 0) Label(L.Text("缩放"), IconKind.Zoom, ZoomTop, 30, true);
        foreach (var lane in DecorationLanes()) Label(lane.Title, lane.Icon, lane.Top, lane.Height - 14, true);
    }

    void HandleLaneClick(Point point)
    {
        if (point.Y < RulerHeight) { TrackMenuRequested?.Invoke(this, EventArgs.Empty); return; }
        if (point.X < 90) return;
        if ((TrackFlags & 1) != 0 && point.Y >= ZoomTop - 6 && point.Y <= ZoomTop + 40)
            TrackHidden?.Invoke(this, 1);
        else foreach (var lane in DecorationLanes())
            if (point.Y >= lane.Top - 6 && point.Y <= lane.Top + lane.Height) { TrackHidden?.Invoke(this, lane.Bit); break; }
    }

    (string Id, int Edge)? HitDecoration(Point point, double pixels)
    {
        foreach (var (id, _, window, top) in Decorations())
        {
            var x = TimeToX(window.Start, pixels);
            var rect = new Rect(x, top, Math.Max(8, window.Duration * pixels), 26);
            if (rect.Contains(point)) return (id, point.X - rect.Left < 7 ? -1 : rect.Right - point.X < 7 ? 1 : 0);
        }
        return null;
    }

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

        foreach (var effect in Decorations().Where(x => x.Id != excludeId))
        {
            yield return effect.Window.Start;
            yield return effect.Window.Start + effect.Window.Duration;
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
        Math.Max(48, (width - LaneLabelWidth - 16) / Math.Max(_duration, 0.1)) * _zoom;

    double TimeToX(double time, double pixels) => LaneLabelWidth + 8 + time * pixels - _scroll;

    double XToTime(double x, double pixels) => (x - LaneLabelWidth - 8 + _scroll) / pixels;

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

            var speed = _drag is { Kind: DragKind.Speed } speedDrag && speedDrag.Id == clip.Id ? DragSpeed(clip, speedDrag.Delta) : clip.Speed;
            var duration = Math.Max(0, (sourceEnd - sourceStart) / speed);
            yield return new DrawnClip(clip.Id, clip.AssetId, time, duration, sourceStart, sourceEnd, speed);
            time += duration;
        }
    }

    IEnumerable<ZoomSpan> VisualZooms()
    {
        if (_project is null || (TrackFlags & 1) == 0)
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

    static double DragSpeed(ClipSegment clip, double delta) => Math.Clamp(
        (clip.SourceEnd - clip.SourceStart) / Math.Max(.01, (clip.SourceEnd - clip.SourceStart) / clip.Speed + delta), .5, 100);

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
            duration = Math.Min(duration, total);
            start = Math.Clamp(zoom.Start + drag.Delta, 0, Math.Max(0, total - duration));
        }
        else if (drag.Kind == DragKind.ZoomStart)
        {
            var end = zoom.Start + zoom.Duration;
            start = Math.Clamp(zoom.Start + drag.Delta, 0, Math.Max(0, end - 0.12));
            duration = end - start;
        }
        else if (drag.Kind == DragKind.ZoomEnd)
        {
            duration = Math.Clamp(zoom.Duration + drag.Delta, 0.12, Math.Max(0.12, total - zoom.Start));
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
        if (_project is null || (TrackFlags & 1) == 0 || point.Y < ZoomTop || point.Y > ZoomTop + ZoomHeight)
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
        var first = rect.X + Math.Max(0, Math.Floor(-rect.X / slot)) * slot;
        for (var x = first; x < Math.Min(rect.Right, ActualWidth); x += slot)
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
        var band = Math.Max(2, rect.Height - 12);
        var midY = rect.Top + rect.Height / 2;
        drawing.PushClip(new RectangleGeometry(rect, 8, 8));
        var pen = new Pen(brush, 1);
        for (var x = Math.Max(0, rect.X + 2); x < Math.Min(ActualWidth, rect.Right - 2); x += 2)
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
        var first = Math.Max(0, Math.Floor(_scroll / pixels / step) * step);
        var last = Math.Min(duration, (_scroll + width) / pixels);
        for (double time = first; time <= last + 0.01; time += step)
        {
            var x = TimeToX(time, pixels);
            if (x < 0 || x > width)
            {
                continue;
            }

            for (var minor = 1; minor < 5; minor++)
            {
                var mx = TimeToX(time + minor * step / 5d, pixels);
                if (mx <= width) drawing.DrawLine(new Pen(BrushOf("LineBrush", Brushes.LightGray), .75), new Point(mx, 20), new Point(mx, 24));
            }
            drawing.DrawLine(new Pen(muted, .75), new Point(x, 16), new Point(x, 24));
            drawing.DrawText(Text(TimeLabel(time), 11, muted), new Point(x + 2, 2));
        }
    }

    static string TimeLabel(double time)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, time));
        return span.ToString(span.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
    }

    static FormattedText Text(string value, double size, Brush brush) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Application.Current?.TryFindResource("FontRegular") as FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, 1);

    static Brush BrushOf(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    enum DragKind
    {
        Seek,
        MoveClip,
        TrimStart,
        TrimEnd,
        Speed,
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
