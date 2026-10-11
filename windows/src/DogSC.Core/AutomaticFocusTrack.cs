namespace DogSC.Core;

// ProjectPointerTrack.swift: a central rest region changes only the axis that
// the raw pointer leaves. A single 120 Hz spring retains velocity between moves.
internal sealed class AutomaticFocusTrack
{
    readonly List<(double Time, double X, double Y)> _samples = [];
    readonly double _scale;
    public (double X, double Y) Initial => Focus(_samples[0]);

    public AutomaticFocusTrack(ProjectDocument project, ZoomSpan span, double enter, double holdEnd)
    {
        _scale = span.Scale;
        var events = ZoomMotion.Events(project);
        var clips = TimelineMath.Place(project);
        var sources = clips.Select(c => c.Asset).Distinct().ToDictionary(a => a, a => a.Cursor.OrderBy(p => p.Time).ToArray());
        var entryClip = clips.FirstOrDefault(c => c.Start <= span.Start && c.End > span.Start);
        var entryEnd = Math.Min(span.Start + enter, entryClip?.End ?? span.Start + enter);
        var click = events.FirstOrDefault(p => p.Click && p.Time >= span.Start && p.Time < entryEnd);
        var crop = project.Canvas.Crop;
        (double X, double Y) Local(double x, double y) =>
            (Math.Clamp((x - crop.X) / crop.Width, 0, 1), Math.Clamp((y - crop.Y) / crop.Height, 0, 1));
        (double X, double Y)? Raw(double time)
        {
            // Sample each retained recording source independently. Deleted source
            // motion and unrelated coordinates never interpolate across a cut.
            var clip = clips.FirstOrDefault(c => time >= c.Start && time < c.End);
            if (clip is null || clip.Asset.Cursor.Count == 0) return null;
            var points = sources[clip.Asset];
            var source = clip.Clip.SourceStart + (time - clip.Start) * clip.Clip.Speed + project.Canvas.Cursor.TimingAdvance;
            var lo = 0; var hi = points.Length;
            while (lo < hi) { var mid = (lo + hi) / 2; if (points[mid].Time <= source) lo = mid + 1; else hi = mid; }
            if (lo == 0) return Local(points[0].X, points[0].Y);
            var a = points[lo - 1];
            if (lo == points.Length) return Local(a.X, a.Y);
            var b = points[lo]; var u = Math.Clamp((source - a.Time) / Math.Max(b.Time - a.Time, 1e-9), 0, 1);
            return Local(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
        }
        var fallback = (X: .5 / span.Scale + span.FocusX * (1 - 1 / span.Scale),
            Y: .5 / span.Scale + span.FocusY * (1 - 1 / span.Scale));
        var position = click.Click ? Local(click.X, click.Y) : Raw(span.Start) ?? fallback;
        var target = position; double vx = 0, vy = 0, time = 0;
        var rest = Math.Max(.28 / Math.Max(span.Scale, 1), .01);
        _samples.Add((0, position.X, position.Y));
        while (time < holdEnd)
        {
            var next = Math.Min(time + 1d / 120, holdEnd);
            if (next >= enter && Raw(span.Start + next) is { } pointer)
            {
                if (Math.Abs(pointer.X - target.X) > rest) target.X = pointer.X;
                if (Math.Abs(pointer.Y - target.Y) > rest) target.Y = pointer.Y;
            }
            var dt = next - time;
            vx += (span.SpringStiffness * (target.X - position.X) - span.SpringDamping * vx) / span.SpringMass * dt;
            vy += (span.SpringStiffness * (target.Y - position.Y) - span.SpringDamping * vy) / span.SpringMass * dt;
            position = (Math.Clamp(position.X + vx * dt, 0, 1), Math.Clamp(position.Y + vy * dt, 0, 1));
            time = next; _samples.Add((time, position.X, position.Y));
        }
    }

    (double X, double Y) Focus((double Time, double X, double Y) sample) =>
        (ZoomMotion.Focus(sample.X, _scale), ZoomMotion.Focus(sample.Y, _scale));

    public (double X, double Y) At(double time)
    {
        var lo = 0; var hi = _samples.Count;
        while (lo < hi) { var mid = (lo + hi) / 2; if (_samples[mid].Time <= time) lo = mid + 1; else hi = mid; }
        if (lo == 0) return Initial;
        var a = _samples[lo - 1];
        if (lo == _samples.Count) return Focus(a);
        var b = _samples[lo]; var u = Math.Clamp((time - a.Time) / (b.Time - a.Time), 0, 1);
        return Focus((time, a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u));
    }
}
