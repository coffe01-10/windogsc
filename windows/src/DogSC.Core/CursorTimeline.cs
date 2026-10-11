namespace DogSC.Core;

public readonly record struct CursorFrame(
    (double X, double Y)? Pointer,
    (double X, double Y)? Click,
    double Progress,
    string? Asset = null,
    double Rotation = 0,
    string ClickStyle = "ripple");

/// <summary>预览和导出共同使用的源时钟采样器；轨迹保留全部点，随机定位使用二分查找。</summary>
public sealed class CursorTimeline
{
    readonly ProjectDocument _project;
    public ZoomMotion Motion { get; }
    readonly IReadOnlyList<PlacedClip> _clips;
    readonly (double Time, double X, double Y)[] _clicks;
    readonly PointerTrack.Track?[] _tracks;

    public CursorTimeline(ProjectDocument project)
    {
        _project = project;
        Motion = new ZoomMotion(project);
        _clips = TimelineMath.Place(project);
        var fx = project.Canvas.Cursor;
        var advance = fx.TimingAdvance;
        _clicks = _clips.SelectMany(clip => clip.Asset.Cursor
            .Where(p => p.Click && p.Time - advance >= clip.Clip.SourceStart && p.Time - advance < clip.Clip.SourceEnd)
            .Select(p => (Time: clip.Start + (p.Time - advance - clip.Clip.SourceStart) / clip.Clip.Speed, p.X, p.Y)))
            .OrderBy(p => p.Time).ToArray();
        // Shared source tracks keep interpolation and spring state across cuts.
        var tracks = new Dictionary<MediaAsset, PointerTrack.Track?>();
        _tracks = _clips.Select(clip =>
        {
            if (tracks.TryGetValue(clip.Asset, out var cached)) return cached;
            var knots = clip.Asset.Cursor
                .Select(p => (Eff: p.Time - advance, Point: p))
                .OrderBy(p => p.Eff)
                .Select(p => new PointerTrack.Knot(p.Eff,
                    Math.Clamp(p.Point.X, 0, 1), Math.Clamp(p.Point.Y, 0, 1), p.Point.Click, p.Point.Cursor))
                .ToArray();
            var track = knots.Length == 0 ? null : new PointerTrack.Track(knots, fx.Motion, fx.SpringMass, fx.SpringStiffness, fx.SpringDamping);
            tracks[clip.Asset] = track;
            return track;
        }).ToArray();
    }

    // Focus follows the recorded pointer even when its visible sprite is hidden.
    public NormalizedPoint? PositionAt(double time)
    {
        for (var i = 0; i < _clips.Count; i++)
        {
            var clip = _clips[i];
            if (_tracks[i] is not { } track || time < clip.Start || time >= clip.End) continue;
            var p = track.AnchoredPositionAt(clip.Clip.SourceStart + (time - clip.Start) * clip.Clip.Speed);
            return new(p.X, p.Y);
        }
        return null;
    }

    public CursorFrame At(double time)
    {
        var fx = _project.Canvas.Cursor;
        var asset = CursorCatalog.ResolveAsset(fx);
        if (asset == "system.hidden") return default;
        (double X, double Y)? pointer = null;
        string? resolved = asset;
        double rotation = 0;
        for (var i = 0; i < _clips.Count; i++)
        {
            var clip = _clips[i];
            var track = _tracks[i];
            if (track is null || time < clip.Start || time >= clip.End) continue;
            var source = clip.Clip.SourceStart + (time - clip.Start) * clip.Clip.Speed;
            var knots = track.Knots;
            // 静止隐藏：source 时刻距最近一个轨迹点的空闲时长超过 IdleDelay。
            var idx = UpperBound(knots, source);
            var before = knots[idx - 1];
            // A recorded hidden point also marks leaving or obscuring the target
            // window. Authored cursor styles must respect that visibility.
            if (before.Cursor == "system.hidden") break;
            var idleFor = Math.Max(0, source - before.Time);
            if (fx.IdleHide && idleFor / clip.Clip.Speed > fx.IdleDelay) break;
            var pos = track.AnchoredPositionAt(source);
            pointer = (pos.X, pos.Y);
            resolved = asset == "system.automatic"
                ? CursorCatalog.NormalizeRecorded(before.Cursor)
                : asset;
            rotation = track.RotationAt(source, fx.MotionTilt);
            break;
        }
        var clickStyle = CursorCatalog.ResolveClick(fx);
        if (clickStyle == "none" || _clicks.Length == 0)
            return new(pointer, null, 0, resolved, rotation, clickStyle);
        var click = LastClickBefore(time);
        if (click is null) return new(pointer, null, 0, resolved, rotation, clickStyle);
        var progress = (time - click.Value.Time) / CursorCatalog.ClickDuration(clickStyle);
        return new(pointer, progress < 1 ? (click.Value.X, click.Value.Y) : null, progress,
            resolved, rotation, clickStyle);
    }

    (double Time, double X, double Y)? LastClickBefore(double time)
    {
        var lo = 0;
        var hi = _clicks.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_clicks[mid].Time <= time) lo = mid + 1; else hi = mid;
        }
        return lo == 0 ? null : _clicks[lo - 1];
    }

    static int UpperBound(PointerTrack.Knot[] knots, double source)
    {
        var lo = 0;
        var hi = knots.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (knots[mid].Time <= source) lo = mid + 1; else hi = mid;
        }
        return Math.Max(1, lo);
    }

    public (double X, double Y) InFrame((double X, double Y) point, double time)
    {
        var crop = _project.Canvas.Crop;
        var x = (point.X - crop.X) / crop.Width;
        var y = (point.Y - crop.Y) / crop.Height;
        var pose = Motion.At(time);
        x = x * pose.Scale - (pose.Scale - 1) * pose.X;
        y = y * pose.Scale - (pose.Scale - 1) * pose.Y;
        return (x, y);
    }
}
