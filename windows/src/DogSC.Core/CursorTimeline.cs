namespace DogSC.Core;

public readonly record struct CursorFrame((double X, double Y)? Pointer, (double X, double Y)? Click, double Progress);

/// <summary>预览和导出共同使用的源时钟采样器；轨迹保留全部点，随机定位使用二分查找。</summary>
public sealed class CursorTimeline
{
    readonly ProjectDocument _project;
    public ZoomMotion Motion { get; }
    readonly IReadOnlyList<PlacedClip> _clips;
    readonly (double Time, double X, double Y)[] _clicks;

    public CursorTimeline(ProjectDocument project)
    {
        _project = project;
        Motion = new ZoomMotion(project);
        _clips = TimelineMath.Place(project);
        var advance = project.Canvas.Cursor.TimingAdvance;
        _clicks = _clips.SelectMany(clip => clip.Asset.Cursor
            .Where(p => p.Click && p.Time - advance >= clip.Clip.SourceStart && p.Time - advance < clip.Clip.SourceEnd)
            .Select(p => (Time: clip.Start + (p.Time - advance - clip.Clip.SourceStart) / clip.Clip.Speed, p.X, p.Y)))
            .OrderBy(p => p.Time).ToArray();
    }

    public CursorFrame At(double time)
    {
        var fx = _project.Canvas.Cursor;
        if (fx.Mode == "hidden") return default;
        var clip = _clips.FirstOrDefault(clip => time >= clip.Start && time < clip.End);
        (double X, double Y)? pointer = null;
        if (clip is not null && clip.Asset.Cursor.Count > 0)
        {
            var source = clip.Clip.SourceStart + (time - clip.Start) * clip.Clip.Speed + fx.TimingAdvance;
            var points = clip.Asset.Cursor;
            var low = 0;
            var high = points.Count;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (points[mid].Time <= source) low = mid + 1; else high = mid;
            }
            var before = points[Math.Max(0, low - 1)];
            var after = points[Math.Min(points.Count - 1, low)];
            var gap = after.Time - before.Time;
            var distance = Math.Sqrt(Math.Pow(after.X - before.X, 2) + Math.Pow(after.Y - before.Y, 2));
            var continuous = gap <= 0.12 || gap <= 0.5 && distance <= 0.03;
            var ratio = continuous && gap > 0 ? Math.Clamp((source - before.Time) / gap, 0, 1) : 0;
            if (!fx.IdleHide || (source - before.Time) / clip.Clip.Speed <= fx.IdleDelay)
                pointer = (before.X + (after.X - before.X) * ratio, before.Y + (after.Y - before.Y) * ratio);
        }
        if (!fx.ClickRipple || _clicks.Length == 0) return new(pointer, null, 0);
        var a = 0;
        var b = _clicks.Length;
        while (a < b)
        {
            var mid = (a + b) / 2;
            if (_clicks[mid].Time <= time) a = mid + 1; else b = mid;
        }
        if (a == 0) return new(pointer, null, 0);
        var click = _clicks[a - 1];
        var progress = (time - click.Time) / CursorTrack.ClickDuration;
        return new(pointer, progress < 1 ? (click.X, click.Y) : null, progress);
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
