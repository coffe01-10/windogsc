namespace DogSC.Core;

/// <summary>
/// 指针运动解算（移植 macOS 版 PointerTrack.swift）：
/// smooth=240Hz 弹簧模拟+点击锚定，medium/rapid=PCHIP 单调埃尔米特，
/// none=线性；运动倾斜按速度方向产生光标旋转。
/// </summary>
public static class PointerTrack
{
    public const double MaximumContinuousInputGap = 0.12;
    public const double MaximumSlowMoveGap = 0.50;
    public const double MaximumSlowMoveDistance = 0.03;
    public const double ClickAnchorLeadDuration = 0.18;
    public const double ClickAnchorTrailDuration = 0.18;
    const double SpringRate = 240.0;
    const double JitterThreshold = 0.00035;

    public sealed record Knot(double Time, double X, double Y, bool Click, string? Cursor);

    /// <summary>单个片段的运动采样器；knots 已是修剪、按时间排序后的（含 TimingAdvance 换算）。</summary>
    public sealed class Track
    {
        public Knot[] Knots { get; }
        readonly double[]? _springT, _springX, _springY;
        readonly double[]? _tx, _ty;
        readonly Knot[] _clicks;
        readonly string _motion;
        readonly double _mass, _stiffness, _damping;

        public Track(Knot[] knots, string motion, double mass = 2.8, double stiffness = 450, double damping = 66)
        {
            Knots = knots;
            _motion = motion;
            _mass = mass; _stiffness = stiffness; _damping = damping;
            _clicks = knots.Where(k => k.Click).ToArray();
            if (motion == "smooth" && knots.Length > 1)
            {
                (_springT, _springX, _springY) = MakeSpringSamples(knots);
            }
            else if (motion is "medium" or "rapid" && knots.Length > 1)
            {
                var scale = motion == "medium" ? 0.78 : 1.0;
                (_tx, _ty) = MakePchipTangents(knots, scale);
            }
        }

        static bool Continuous(Knot a, Knot b)
        {
            var gap = b.Time - a.Time;
            if (gap <= MaximumContinuousInputGap) return true;
            if (gap <= MaximumSlowMoveGap)
            {
                var d = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
                return d <= MaximumSlowMoveDistance;
            }
            return false;
        }

        /// <summary>source 时刻的无锚定位置（弹簧或插值），含连续性空档判断。</summary>
        public (double X, double Y) PositionAt(double source)
        {
            var knots = Knots;
            if (knots.Length == 0) return (0.5, 0.5);
            if (source <= knots[0].Time) return (knots[0].X, knots[0].Y);
            if (source >= knots[^1].Time) return (knots[^1].X, knots[^1].Y);
            var i = UpperBound(knots, source);
            var a = knots[i - 1];
            var b = knots[i];
            if (!Continuous(a, b)) return (a.X, a.Y);
            var ratio = Math.Clamp((source - a.Time) / (b.Time - a.Time), 0, 1);
            if (_springT is not null)
            {
                // 弹簧：对采样序列线性取值。
                var t = Math.Clamp((source - _springT[0]) * SpringRate, 0, _springT.Length - 1.0001);
                var j = (int)t;
                var f = t - j;
                return (_springX![j] + (_springX[j + 1] - _springX[j]) * f,
                        _springY![j] + (_springY[j + 1] - _springY[j]) * f);
            }
            if (_tx is not null)
            {
                // PCHIP 单调埃尔米特。
                var h = b.Time - a.Time;
                var u = ratio;
                var u2 = u * u;
                var u3 = u2 * u;
                double Eval(double p0, double p1, double m0, double m1) =>
                    (2 * u3 - 3 * u2 + 1) * p0 + (u3 - 2 * u2 + u) * h * m0
                    + (-2 * u3 + 3 * u2) * p1 + (u3 - u2) * h * m1;
                return (Eval(a.X, b.X, _tx![i - 1], _tx![i]), Eval(a.Y, b.Y, _ty![i - 1], _ty![i]));
            }
            return (a.X + (b.X - a.X) * ratio, a.Y + (b.Y - a.Y) * ratio);
        }

        /// <summary>带点击锚定的位置（smooth 档在点击前后 0.18s 内把位置拉向点击点）。</summary>
        public (double X, double Y) AnchoredPositionAt(double source)
        {
            var pos = PositionAt(source);
            if (_motion != "smooth" || _clicks.Length == 0) return pos;
            var idx = NearestClick(source);
            if (idx < 0) return pos;
            var c = _clicks[idx];
            var lo = Math.Max(c.Time - ClickAnchorLeadDuration,
                idx > 0 ? (_clicks[idx - 1].Time + c.Time) / 2 : double.NegativeInfinity);
            var hi = Math.Min(c.Time + ClickAnchorTrailDuration,
                idx < _clicks.Length - 1 ? (c.Time + _clicks[idx + 1].Time) / 2 : double.PositiveInfinity);
            if (source <= lo || source >= hi) return pos;
            if (Math.Abs(source - c.Time) < 1e-9) return (c.X, c.Y);
            double w;
            if (source < c.Time)
            {
                w = SmootherStep(Math.Clamp((source - lo) / Math.Max(c.Time - lo, 1e-6), 0, 1));
            }
            else
            {
                w = SmootherStep(Math.Clamp((hi - source) / Math.Max(hi - c.Time, 1e-6), 0, 1));
            }
            return (pos.X + (c.X - pos.X) * w, pos.Y + (c.Y - pos.Y) * w);
        }

        int NearestClick(double source)
        {
            var best = -1;
            var bestD = double.MaxValue;
            for (var i = 0; i < _clicks.Length; i++)
            {
                var d = Math.Abs(_clicks[i].Time - source);
                if (d < bestD) { bestD = d; best = i; }
            }
            return bestD <= ClickAnchorLeadDuration + ClickAnchorTrailDuration ? best : -1;
        }

        /// <summary>运动倾斜角（弧度），按速度方向；none 或 MotionTilt=0 时为 0。</summary>
        public double RotationAt(double source, double strength)
        {
            if (strength <= 0 || _motion == "none" || Knots.Length < 2) return 0;
            var (window, maxDeg, prevW, span) = _motion switch
            {
                "smooth" => (0.08, 9.0, 0.38, 0.18),
                "medium" => (0.065, 8.0, 0.24, 0.22),
                _ => (0.05, 7.0, 0.12, 0.28),
            };
            var p0 = AnchoredPositionAt(source);
            var p1 = AnchoredPositionAt(source - window);
            var p2 = AnchoredPositionAt(source - window * 2);
            var vx = (p0.X - p1.X) / window;
            var vy = (p0.Y - p1.Y) / window;
            var px = (p1.X - p2.X) / window;
            var py = (p1.Y - p2.Y) / window;
            vx = vx * (1 - prevW) + px * prevW;
            vy = vy * (1 - prevW) + py * prevW;
            var speed = Math.Sqrt(vx * vx + vy * vy);
            var torque = speed < 1e-9 ? 0 : Math.Clamp((vx - vy) / speed / Math.Sqrt(2), -1, 1);
            var response = SmoothStep(Math.Clamp((speed - 0.006) / span, 0, 1));
            var idleDecay = 1 - Math.Clamp((source - IdleStartAt(source)) / 0.18, 0, 1);
            var gain = 0.35 + 0.65 * Math.Clamp(strength, 0, 4) / 4;
            return -maxDeg * Math.PI / 180 * gain * response * torque * idleDecay;
        }

        /// <summary>光标转入静止的起点：末点之后/非连续空档内 = 空档起点；移动中 = source 本身。</summary>
        double IdleStartAt(double source)
        {
            var knots = Knots;
            if (source <= knots[0].Time) return source;
            if (source >= knots[^1].Time) return knots[^1].Time;
            var i = UpperBound(knots, source);
            return Continuous(knots[i - 1], knots[i]) ? source : knots[i - 1].Time;
        }

        static int UpperBound(Knot[] knots, double source)
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

        /// <summary>240Hz 弹簧积分：半隐式欧拉；目标抖动小于阈值时保持，稳定超时后冻结。</summary>
        (double[] T, double[] X, double[] Y) MakeSpringSamples(Knot[] knots)
        {
            var start = knots[0].Time;
            var end = knots[^1].Time;
            var settle = Math.Max(0.65, 6 * Math.Sqrt(_mass / _stiffness));
            var count = Math.Max(2, (int)Math.Ceiling((end - start) * SpringRate) + 1);
            var ts = new double[count];
            var xs = new double[count];
            var ys = new double[count];
            var px = knots[0].X;
            var py = knots[0].Y;
            double vx = 0, vy = 0;
            var tx = px;
            var ty = py;
            var ki = 0;
            var targetSince = 0.0;
            var frozen = false;
            var dt = 1 / SpringRate;
            for (var i = 0; i < count; i++)
            {
                var t = start + i * dt;
                while (ki + 1 < knots.Length && knots[ki + 1].Time <= t)
                {
                    ki++;
                    if (tx != knots[ki].X || ty != knots[ki].Y) frozen = false;
                    tx = knots[ki].X;
                    ty = knots[ki].Y;
                    targetSince = t;
                }
                ts[i] = t;
                if (frozen)
                {
                    xs[i] = tx; ys[i] = ty;
                    continue;
                }
                var ax = (_stiffness * (tx - px) - _damping * vx) / _mass;
                var ay = (_stiffness * (ty - py) - _damping * vy) / _mass;
                vx += ax * dt;
                vy += ay * dt;
                px += vx * dt;
                py += vy * dt;
                if (Math.Abs(tx - px) < JitterThreshold && Math.Abs(ty - py) < JitterThreshold
                    && Math.Abs(vx) < JitterThreshold && Math.Abs(vy) < JitterThreshold)
                {
                    px = tx; py = ty; vx = 0; vy = 0;
                }
                if (t - targetSince > settle
                    && Math.Abs(tx - px) < JitterThreshold * 10 && Math.Abs(ty - py) < JitterThreshold * 10)
                {
                    frozen = true;
                    px = tx; py = ty;
                }
                xs[i] = px; ys[i] = py;
            }
            return (ts, xs, ys);
        }

        /// <summary>Fritsch–Carlson PCHIP 切线 × 风格系数。</summary>
        static (double[] Tx, double[] Ty) MakePchipTangents(Knot[] knots, double scale)
        {
            var n = knots.Length;
            var tx = new double[n];
            var ty = new double[n];
            if (n == 1) return (tx, ty);
            for (var d = 0; d < 2; d++)
            {
                var tangents = d == 0 ? tx : ty;
                double V(Knot k) => d == 0 ? k.X : k.Y;
                for (var i = 0; i < n; i++)
                {
                    if (i == 0)
                    {
                        var h0 = knots[1].Time - knots[0].Time;
                        var d0 = (V(knots[1]) - V(knots[0])) / Math.Max(h0, 1e-9);
                        tangents[0] = n == 2 ? d0 : EndpointTangent(knots, V, 0, 1, 2);
                    }
                    else if (i == n - 1)
                    {
                        tangents[i] = EndpointTangent(knots, V, n - 1, n - 2, n - 3);
                    }
                    else
                    {
                        var hp = knots[i].Time - knots[i - 1].Time;
                        var hn = knots[i + 1].Time - knots[i].Time;
                        var dp = (V(knots[i]) - V(knots[i - 1])) / Math.Max(hp, 1e-9);
                        var dn = (V(knots[i + 1]) - V(knots[i])) / Math.Max(hn, 1e-9);
                        tangents[i] = dp * dn <= 0 ? 0 : (hp + hn) / (hp / dp + hn / dn);
                    }
                    tangents[i] *= scale;
                }
            }
            return (tx, ty);
        }

        static double EndpointTangent(Knot[] k, Func<Knot, double> v, int i, int j, int m)
        {
            var h0 = k[j].Time - k[i].Time;
            var d0 = (v(k[j]) - v(k[i])) / (h0 == 0 ? 1 : h0);
            if (m < 0 || m >= k.Length) return d0;
            var h1 = k[m].Time - k[j].Time;
            var d1 = (v(k[m]) - v(k[j])) / (h1 == 0 ? 1 : h1);
            var t = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
            if (t * d0 < 0) return 0;
            if (d0 * d1 < 0 && Math.Abs(t) > Math.Abs(3 * d0)) return 3 * d0;
            return t;
        }
    }

    static double SmootherStep(double t) => t * t * t * (t * (6 * t - 15) + 10);

    static double SmoothStep(double t) => t * t * (3 - 2 * t);
}
