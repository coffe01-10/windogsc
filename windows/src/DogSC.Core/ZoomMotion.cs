using System.Globalization;

namespace DogSC.Core;

public readonly record struct ZoomPose(double Scale, double X, double Y)
{
    public static ZoomPose Rest => new(1, .5, .5);
}

/// <summary>一次编辑后编译运动轨迹；随机定位、预览、光标和 ffmpeg 使用相同多项式。</summary>
public sealed class ZoomMotion
{
    readonly List<(ZoomSpan Span, Track Scale, Track X, Track Y, AutomaticFocusTrack? Follow, double Enter, double HoldEnd)> _spans = [];

    public ZoomMotion(ProjectDocument project)
    {
        var clicks = Events(project).Where(p => p.Click).ToArray();
        var ordered = project.Zooms.OrderBy(z => z.Start).ToArray();
        var previousTarget = ZoomPose.Rest;
        for (var i = 0; i < ordered.Length; i++)
        {
            var span = ordered[i];
            var joinedBefore = i > 0 && Math.Abs(ordered[i - 1].Start + ordered[i - 1].Duration - span.Start) < .001;
            var joinedAfter = i + 1 < ordered.Length && Math.Abs(span.Start + span.Duration - ordered[i + 1].Start) < .001;
            var start = joinedBefore ? previousTarget : ZoomPose.Rest;
            var scale = new Track(start.Scale, span);
            var x = new Track(start.X, span);
            var y = new Track(start.Y, span);
            var (enter, exit) = Windows(span, joinedAfter);
            var endHold = span.Duration - exit;
            var follow = span.Origin == "automatic" && project.Motion.AutomaticFollow == "pointer"
                ? new AutomaticFocusTrack(project, span, enter, endHold) : null;
            var focuses = span.Origin == "automatic" && follow is null
                ? FocusGroups(clicks.Where(p => p.Time >= span.Start && p.Time < span.Start + endHold), span.Scale, project.Canvas.Crop)
                : [];
            var first = follow is not null ? (Time: span.Start, X: follow.Initial.X, Y: follow.Initial.Y)
                : focuses.Count > 0 ? focuses[0] : (Time: span.Start, X: span.FocusX, Y: span.FocusY);
            scale.To(0, enter, span.Scale, span.Easing);
            x.To(0, enter, first.X, span.Easing);
            y.To(0, enter, first.Y, span.Easing);
            var previousClick = first.Time - span.Start;
            foreach (var focus in focuses.Skip(1))
            {
                var at = Math.Max(previousClick, focus.Time - span.Start - enter);
                at = Math.Min(at, endHold);
                var duration = Math.Min(Math.Max(0, focus.Time - span.Start - at), endHold - at);
                // 中途换目标继承当前位置与速度，避免每次点击把镜头停住再启动。
                x.To(at, duration, focus.X, span.Easing);
                y.To(at, duration, focus.Y, span.Easing);
                previousClick = focus.Time - span.Start;
            }
            var held = follow?.At(endHold) ?? (x.At(endHold), y.At(endHold));
            if (follow is not null) { x.To(endHold, 0, held.Item1, span.Easing); y.To(endHold, 0, held.Item2, span.Easing); }
            previousTarget = new(span.Scale, held.Item1, held.Item2);
            if (exit > 0)
            {
                scale.To(endHold, exit, 1, span.Easing);
                x.To(endHold, exit, .5, span.Easing);
                y.To(endHold, exit, .5, span.Easing);
            }
            _spans.Add((span, scale, x, y, follow, enter, endHold));
        }
    }

    public ZoomPose At(double time)
    {
        foreach (var motion in _spans)
            if (time >= motion.Span.Start && time < motion.Span.Start + motion.Span.Duration)
            {
                var local = time - motion.Span.Start;
                var focus = local >= motion.Enter && local <= motion.HoldEnd && motion.Follow is not null
                    ? motion.Follow.At(local) : (motion.X.At(local), motion.Y.At(local));
                return new(Math.Clamp(motion.Scale.At(local), 1, 6), Math.Clamp(focus.Item1, 0, 1), Math.Clamp(focus.Item2, 0, 1));
            }
        return ZoomPose.Rest;
    }

    public ZoomPose TargetAt(ZoomSpan span, double time)
    {
        if (time <= span.Start) return new(span.Scale, span.FocusX, span.FocusY);
        var motion = _spans.First(m => m.Span.Id == span.Id);
        var joined = _spans.Any(m => m.Span.Id != span.Id && Math.Abs(m.Span.Start - span.Start - span.Duration) < .001);
        var (enter, exit) = Windows(span, joined);
        var local = Math.Clamp(time - span.Start, enter, span.Duration - exit);
        var focus = motion.Follow?.At(local) ?? (motion.X.At(local), motion.Y.At(local));
        return new(span.Scale, Math.Clamp(focus.Item1, 0, 1), Math.Clamp(focus.Item2, 0, 1));
    }

    public bool HasMotion(double start, double duration) => _spans.Any(m => m.Span.Scale > 1.01
        && m.Span.Start < start + duration && m.Span.Start + m.Span.Duration > start);

    public (string Scale, string X, string Y) Expressions(double start, double duration, string time, int fps = 60)
    {
        string scale = "1", x = "0.5", y = "0.5";
        foreach (var motion in _spans.Where(m => m.Span.Start < start + duration && m.Span.Start + m.Span.Duration > start))
        {
            var span = motion.Span;
            var local = "(" + time + "-" + F(span.Start) + ")";
            var active = "gte(" + time + "," + F(span.Start) + ")*lt(" + time + "," + F(span.Start + span.Duration) + ")";
            scale = "if(" + active + "," + motion.Scale.Expression(local) + "," + scale + ")";
            var fx = motion.X.Expression(local); var fy = motion.Y.Expression(local);
            if (motion.Follow is not null)
            {
                var from = Math.Max(start, span.Start); var to = Math.Min(start + duration, span.Start + span.Duration);
                var times = new List<double> { from };
                // Match this source slice's output-frame clock, including cuts
                // whose start is not an integer multiple of the export cadence.
                for (var frame = (int)Math.Floor((from - start) * fps) + 1; start + frame / (double)fps < to; frame++)
                    times.Add(start + frame / (double)fps);
                times.Add(to);
                var poses = times.Select(t => At(Math.Min(t, span.Start + span.Duration - 1e-9))).ToArray();
                fx = SampleExpression(times, poses.Select(p => p.X).ToArray(), time);
                fy = SampleExpression(times, poses.Select(p => p.Y).ToArray(), time);
            }
            x = "if(" + active + "," + fx + "," + x + ")";
            y = "if(" + active + "," + fy + "," + y + ")";
        }
        return ("clip(" + scale + ",1,6)", "clip(" + x + ",0,1)", "clip(" + y + ",0,1)");
    }

    // A balanced selection tree avoids FFmpeg's nested-expression limit on
    // long trajectories. Each leaf interpolates two actual shared motion poses.
    static string SampleExpression(IReadOnlyList<double> times, double[] values, string time)
    {
        string Range(int left, int right)
        {
            if (values.Skip(left).Take(right - left + 1).All(v => Math.Abs(v - values[left]) < 1e-10)) return F(values[left]);
            if (right - left == 1)
                return "(" + F(values[left]) + "+" + F((values[right] - values[left]) / (times[right] - times[left]))
                    + "*(" + time + "-" + F(times[left]) + "))";
            var mid = (left + right) / 2;
            return "if(lt(" + time + "," + F(times[mid]) + ")," + Range(left, mid) + "," + Range(mid, right) + ")";
        }
        return Range(0, times.Count - 1);
    }

    public static List<(double Time, double X, double Y, bool Click)> Events(ProjectDocument project)
    {
        var advance = project.Canvas.Cursor.TimingAdvance;
        return TimelineMath.Place(project).SelectMany(clip => clip.Asset.Cursor
            .Where(p => p.Time - advance >= clip.Clip.SourceStart && p.Time - advance < clip.Clip.SourceEnd)
            .Select(p => (Time: clip.Start + (p.Time - advance - clip.Clip.SourceStart) / clip.Clip.Speed, p.X, p.Y, p.Click)))
            .OrderBy(p => p.Time).ToList();
    }

    static List<(double Time, double X, double Y)> FocusGroups(IEnumerable<(double Time, double X, double Y, bool Click)> clicks,
        double scale, SourceCrop crop)
    {
        var groups = new List<(double Time, double X, double Y)>();
        double minX = 0, maxX = 0, minY = 0, maxY = 0;
        foreach (var click in clicks)
        {
            var px = Math.Clamp((click.X - crop.X) / crop.Width, 0, 1);
            var py = Math.Clamp((click.Y - crop.Y) / crop.Height, 0, 1);
            if (groups.Count == 0 || Math.Max(maxX, px) - Math.Min(minX, px) > .5 / scale
                || Math.Max(maxY, py) - Math.Min(minY, py) > .7 / scale)
            {
                minX = maxX = px; minY = maxY = py;
                groups.Add((click.Time, 0, 0));
            }
            else
            {
                minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
            }
            var time = groups[^1].Time;
            groups[^1] = (time, Focus((minX + maxX) / 2, scale), Focus((minY + maxY) / 2, scale));
        }
        return groups;
    }

    public static double Focus(double position, double scale) => scale <= 1.02 ? .5
        : Math.Clamp((position - .5 / scale) / (1 - 1 / scale), 0, 1);

    public static (double Enter, double Exit) Windows(ZoomSpan span, bool joinedAfter = false)
    {
        var enter = span.EnterDuration ?? Math.Min(.4, span.Duration / 3);
        var exit = joinedAfter ? 0 : span.ExitDuration ?? Math.Min(.4, span.Duration / 3);
        var factor = enter + exit > span.Duration ? span.Duration / (enter + exit) : 1;
        return (enter * factor, exit * factor);
    }

    /// <summary>easing → 三次贝塞尔控制点（与 Mac ZoomBezierCurve 预设一致）。</summary>
    public static (double X1, double Y1, double X2, double Y2)? BezierOf(ZoomSpan span) => span.Easing switch
    {
        "quadratic" => (0.45, 0.0, 0.55, 1.0),
        "cubic" => (0.65, 0.0, 0.35, 1.0),
        "custom" => (span.CurveX1, span.CurveY1, span.CurveX2, span.CurveY2),
        _ => null,
    };

    /// <summary>CSS 式 cubic-bezier：给定 x=u 求 y（牛顿迭代解 x(t)=u）。</summary>
    public static double BezierY(double x, double x1, double y1, double x2, double y2)
    {
        x = Math.Clamp(x, 0, 1);
        if (x is 0 or 1) return x;
        var t = x;
        double lo = 0, hi = 1;
        for (var i = 0; i < 32; i++)
        {
            var cx = B(t, x1, x2) - x;
            var dx = Bd(t, x1, x2);
            if (Math.Abs(cx) < 1e-12) break;
            if (cx < 0) lo = t; else hi = t;
            var next = Math.Abs(dx) < 1e-9 ? double.NaN : t - cx / dx;
            t = double.IsFinite(next) && next > lo && next < hi ? next : (lo + hi) / 2;
        }
        return B(t, y1, y2);

        static double B(double t, double p1, double p2)
        {
            var u = 1 - t;
            return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
        }
        static double Bd(double t, double p1, double p2)
        {
            var u = 1 - t;
            return 3 * u * u * p1 + 6 * u * t * (p2 - p1) + 3 * t * t * (1 - p2);
        }
    }

    public static double Progress(ZoomSpan span, double u)
    {
        u = Math.Clamp(u, 0, 1);
        if (span.Easing == "linear") return u;
        if (BezierOf(span) is { } bz) return BezierY(u, bz.X1, bz.Y1, bz.X2, bz.Y2);
        if (span.Easing != "spring") return u * u * u * (10 + u * (-15 + 6 * u));
        var omega = Math.Sqrt(span.SpringStiffness / span.SpringMass);
        var zeta = Math.Min(span.SpringDamping / (2 * Math.Sqrt(span.SpringStiffness * span.SpringMass)), .9999);
        var a = zeta * omega * .72;
        var b = omega * Math.Sqrt(1 - zeta * zeta) * .72;
        double Response(double t) => 1 - Math.Exp(-a * t) * (Math.Cos(b * t) + a / b * Math.Sin(b * t));
        return Math.Clamp(Response(u) / Response(1), 0, 1);
    }

    static string F(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    sealed class Track(double initial, ZoomSpan span)
    {
        readonly List<Segment> _segments = [];
        public double At(double time) => _segments.LastOrDefault(s => s.Start <= time)?.At(time) ?? initial;
        public void To(double start, double duration, double target, string easing)
        {
            var previous = _segments.LastOrDefault(s => s.Start <= start);
            var from = previous?.At(start) ?? initial;
            var velocity = previous?.Velocity(start) ?? 0;
            _segments.RemoveAll(s => s.Start >= start);
            _segments.Add(new(start, duration, from, target, velocity, easing, span));
        }
        public string Expression(string time)
        {
            var expression = F(initial);
            foreach (var segment in _segments)
                expression = "if(gte(" + time + "," + F(segment.Start) + ")," + segment.Expression(time) + "," + expression + ")";
            return expression;
        }
    }

    sealed class Segment
    {
        public double Start { get; }
        readonly double _duration;
        readonly double[] _c;
        readonly bool _spring;
        readonly double _a, _b, _normalizer, _delta, _v;
        readonly (double X1, double Y1, double X2, double Y2)? _bezier;
        public Segment(double start, double duration, double from, double target, double velocity, string easing, ZoomSpan span)
        {
            Start = start; _duration = duration;
            var delta = target - from;
            var v = velocity * duration;
            _delta = delta; _v = v;
            _spring = easing == "spring" && duration > .000001;
            var omega = Math.Sqrt(span.SpringStiffness / span.SpringMass);
            var zeta = Math.Min(span.SpringDamping / (2 * Math.Sqrt(span.SpringStiffness * span.SpringMass)), .9999);
            _a = zeta * omega * .72;
            _b = omega * Math.Sqrt(1 - zeta * zeta) * .72;
            _normalizer = Response(1);
            _bezier = duration > .000001 && BezierOf(span) is { } bz ? bz : null;
            _c = duration <= .000001 ? [target] : easing == "linear" ? [from, delta]
                : [from, v, 0, 10 * delta - 6 * v, -15 * delta + 8 * v, 6 * delta - 3 * v];
        }
        double Response(double u) => 1 - Math.Exp(-_a * u) * (Math.Cos(_b * u) + _a / _b * Math.Sin(_b * u));
        public double At(double time)
        {
            var u = _duration <= .000001 ? 1 : Math.Clamp((time - Start) / _duration, 0, 1);
            if (_spring) return _c[0] + _delta * Math.Clamp(Response(u) / _normalizer, 0, 1) + _v * u * (1 - u) * (1 - u);
            if (_bezier is { } b) return _c[0] + _delta * BezierY(u, b.X1, b.Y1, b.X2, b.Y2) + _v * u * (1 - u) * (1 - u);
            var value = _c[^1];
            for (var i = _c.Length - 2; i >= 0; i--) value = value * u + _c[i];
            return value;
        }
        public double Velocity(double time)
        {
            if (_duration <= .000001 || time >= Start + _duration) return 0;
            var u = Math.Clamp((time - Start) / _duration, 0, 1);
            if (_spring)
            {
                var progress = Response(u) / _normalizer;
                var derivative = progress is > 0 and < 1 ? Math.Exp(-_a * u) * (_a * _a + _b * _b) / _b * Math.Sin(_b * u) / _normalizer : 0;
                return (_delta * derivative + _v * (1 - 4 * u + 3 * u * u)) / _duration;
            }
            if (_bezier is { } b)
            {
                var h = 0.001;
                var derivative = (BezierY(Math.Min(u + h, 1), b.X1, b.Y1, b.X2, b.Y2)
                    - BezierY(Math.Max(u - h, 0), b.X1, b.Y1, b.X2, b.Y2)) / (2 * h);
                return (_delta * derivative + _v * (1 - 4 * u + 3 * u * u)) / _duration;
            }
            double value = 0;
            for (var i = _c.Length - 1; i > 0; i--) value = value * u + i * _c[i];
            return value / _duration;
        }
        public string Expression(string time)
        {
            if (_c.Length == 1) return F(_c[0]);
            var u = "clip((" + time + "-" + F(Start) + ")/" + F(_duration) + ",0,1)";
            if (_spring)
            {
                var response = "clip((1-exp(-" + F(_a) + "*(" + u + "))*(cos(" + F(_b) + "*(" + u + "))+"
                    + F(_a / _b) + "*sin(" + F(_b) + "*(" + u + "))))/" + F(_normalizer) + ",0,1)";
                return "(" + F(_c[0]) + "+(" + F(_delta) + ")*(" + response + ")+(" + F(_v) + ")*(" + u + ")*pow(1-(" + u + "),2))";
            }
            if (_bezier is { } b)
            {
                // ffmpeg 表达式不支持求逆，按 16 段线性逼近贝塞尔。
                const int pieces = 16;
                var vterm = "+(" + F(_v) + ")*(" + u + ")*pow(1-(" + u + "),2)";
                var expr = "";
                for (var i = pieces - 1; i >= 0; i--)
                {
                    var y0 = BezierY(i / (double)pieces, b.X1, b.Y1, b.X2, b.Y2);
                    var y1 = BezierY((i + 1) / (double)pieces, b.X1, b.Y1, b.X2, b.Y2);
                    var piece = "(" + F(_c[0]) + "+(" + F(_delta) + ")*(" + F(y0)
                        + "+(" + F(y1 - y0) + ")*((" + u + ")-(" + F(i / (double)pieces) + "))*" + F(pieces) + "))";
                    expr = i == pieces - 1 ? piece + vterm : "if(lt(" + u + "," + F((i + 1) / (double)pieces) + ")," + piece + vterm + "," + expr + ")";
                }
                return "(" + expr + ")";
            }
            // 稀疏幂式避免嵌套 Horner 展开重复长时间表达式。
            return "(" + string.Join("+", _c.Select((c, i) => c == 0 ? null : "(" + F(c) + ")" + (i == 0 ? "" : "*pow(" + u + "," + i + ")")).Where(s => s is not null)) + ")";
        }
    }
}
