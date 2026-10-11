using System.Globalization;

namespace DogSC.Core;

/// <summary>把共享运动姿态转换成逐帧 FFmpeg 表达式。</summary>
public static class MotionExport
{
    private static string F(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    /// <summary>
    /// 把采样点连成平衡分段线性表达式；空采样返回 outsideValue，
    /// 首样本前取首值、末样本后取末值。
    /// </summary>
    public static string Piecewise(
        IReadOnlyList<(double Time, double Value)> samples,
        string outsideValue, string timeVar = "t", bool escapeCommas = false)
    {
        if (samples.Count == 0) return outsideValue;
        var comma = escapeCommas ? "\\," : ",";
        // Drop only points on the same line; values at output frame timestamps stay exact.
        var points = new List<(double Time, double Value)>();
        foreach (var point in samples) AddPoint(points, point);
        if (points.Count == 1) return F(points[0].Value);
        string Between(int first, int last)
        {
            if (last - first == 1)
            {
                var a = points[first]; var b = points[last];
                if (Math.Abs(a.Value - b.Value) < 1e-8) return F(a.Value);
                return "(" + F(a.Value) + "+" + F(b.Value - a.Value) + "*(" + timeVar + "-" + F(a.Time) + ")/" + F(b.Time - a.Time) + ")";
            }
            var mid = (first + last) / 2;
            return "if(lt(" + timeVar + comma + F(points[mid].Time) + ")" + comma + Between(first, mid) + comma + Between(mid, last) + ")";
        }
        // A balanced tree also handles long animated tracks without FFmpeg's parser depth limit.
        return "if(lte(" + timeVar + comma + F(points[0].Time) + ")" + comma + F(points[0].Value)
            + comma + "if(gte(" + timeVar + comma + F(points[^1].Time) + ")" + comma + F(points[^1].Value)
            + comma + Between(0, points.Count - 1) + "))";
    }

    static void AddPoint(List<(double Time, double Value)> points, (double Time, double Value) point)
    {
        while (points.Count > 1)
        {
            var a = points[^2]; var b = points[^1];
            var span = point.Time - a.Time;
            if (span <= 0 || Math.Abs(b.Value - (a.Value + (point.Value - a.Value) * (b.Time - a.Time) / span)) > 1e-8) break;
            points.RemoveAt(points.Count - 1);
        }
        points.Add(point);
    }

    /// <summary>Fixed-size transparent projection, evaluated at every output frame.</summary>
    public static string Projection(int width, int height, double duration, int fps,
        Func<double, (double X, double Y)[]> cornersAt, Func<double, double>? opacityAt = null)
    {
        var count = Math.Max(1, (int)Math.Ceiling(duration * fps - .0001));
        var coordinates = Enumerable.Range(0, 8).Select(_ => new List<(double, double)>()).ToArray();
        var opacity = new List<(double, double)>();
        var source = new[] { (0d, 0d), ((double)width, 0d), (0d, (double)height), ((double)width, (double)height) };
        // Perspective clamps source coordinates. A transparent one-pixel rim prevents
        // the outermost coloured pixel from smearing across an opening's empty space.
        var rim = new[] { (-1d, -1d), (width + 1d, -1d), (-1d, height + 1d), (width + 1d, height + 1d) };
        for (var frame = 0; frame < count; frame++)
        {
            var time = frame / (double)fps;
            var matrix = Homography.FromQuad(source, cornersAt(time));
            for (var corner = 0; corner < 4; corner++)
            {
                var p = matrix.Apply(rim[corner].Item1, rim[corner].Item2);
                AddPoint(coordinates[corner * 2], (frame, p.X));
                AddPoint(coordinates[corner * 2 + 1], (frame, p.Y));
            }
            if (opacityAt is not null) AddPoint(opacity, (frame, opacityAt(time)));
        }
        var result = "pad=" + (width + 2) + ":" + (height + 2) + ":1:1:color=black@0,format=gbrap,perspective="
            + string.Join(":", coordinates.Select(points => "'" + Piecewise(points, "0", "(in-1)") + "'"))
            + ":sense=destination:eval=frame,crop=" + width + ":" + height + ":0:0,format=rgba";
        if (opacityAt is not null && opacity.Any(p => p.Item2 < .999999))
            result += ",geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*(" + Piecewise(opacity, "1", "N") + ")'";
        return result;
    }

    /// <summary>轨道按帧率等距采样（两端裁到 [start,end)）。</summary>
    public static List<(double Time, T Value)> SampleTrack<T>(
        double start, double end, double fps, int maxSamples,
        Func<double, T> sample)
    {
        var span = Math.Max(0, end - start);
        var count = Math.Clamp((int)Math.Ceiling(span * Math.Min(fps, 15)) + 1, 2, maxSamples);
        var list = new List<(double, T)>(count);
        for (var i = 0; i < count; i++)
        {
            var t = start + span * i / (count - 1);
            list.Add((t, sample(t)));
        }
        return list;
    }

    /// <summary>3x3 单应矩阵（行主序），供逐帧透视角点采样。</summary>
    public readonly record struct Homography(double M00, double M01, double M02,
        double M10, double M11, double M12, double M20, double M21, double M22)
    {
        public static Homography Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

        public (double X, double Y) Apply(double x, double y)
        {
            var w = M20 * x + M21 * y + M22;
            if (Math.Abs(w) < 1e-9) w = w < 0 ? -1e-9 : 1e-9;
            return ((M00 * x + M01 * y + M02) / w, (M10 * x + M11 * y + M12) / w);
        }

        public Homography Then(Homography next) => next * this;

        public static Homography operator *(Homography a, Homography b) =>
            new(a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
                a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
                a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
                a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
                a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
                a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
                a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
                a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
                a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22);

        /// <summary>anchor 为不动点的 缩放+平面旋转+位移 仿射。</summary>
        public static Homography AnchorScaleRot(
            double anchorX, double anchorY, double scale, double rotationZDeg, double dx, double dy)
        {
            var r = rotationZDeg * Math.PI / 180;
            var c = scale * Math.Cos(r);
            var s = scale * Math.Sin(r);
            // p' = anchor + M(p-anchor) + d
            return new(c, -s, anchorX - c * anchorX + s * anchorY + dx,
                s, c, anchorY - s * anchorX - c * anchorY + dy,
                0, 0, 1);
        }

        /// <summary>四点对应求单应：src[i] -> dst[i]，顺序 TL,TR,BL,BR。</summary>
        public static Homography FromQuad(
            (double X, double Y)[] src, (double X, double Y)[] dst)
        {
            var m = new double[8, 8];
            var b = new double[8];
            for (var i = 0; i < 4; i++)
            {
                var (x, y) = src[i];
                var (u, v) = dst[i];
                m[2 * i, 0] = x; m[2 * i, 1] = y; m[2 * i, 2] = 1;
                m[2 * i, 6] = -u * x; m[2 * i, 7] = -u * y;
                b[2 * i] = u;
                m[2 * i + 1, 3] = x; m[2 * i + 1, 4] = y; m[2 * i + 1, 5] = 1;
                m[2 * i + 1, 6] = -v * x; m[2 * i + 1, 7] = -v * y;
                b[2 * i + 1] = v;
            }
            // 高斯消元
            for (var col = 0; col < 8; col++)
            {
                var piv = col;
                for (var r = col + 1; r < 8; r++)
                    if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
                for (var c = 0; c < 8; c++) (m[col, c], m[piv, c]) = (m[piv, c], m[col, c]);
                (b[col], b[piv]) = (b[piv], b[col]);
                var d = m[col, col];
                if (Math.Abs(d) < 1e-12) return Identity;
                for (var r = col + 1; r < 8; r++)
                {
                    var f = m[r, col] / d;
                    for (var c = col; c < 8; c++) m[r, c] -= f * m[col, c];
                    b[r] -= f * b[col];
                }
            }
            var h = new double[8];
            for (var r = 7; r >= 0; r--)
            {
                var acc = b[r];
                for (var c = r + 1; c < 8; c++) acc -= m[r, c] * h[c];
                h[r] = acc / m[r, r];
            }
            return new(h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], 1);
        }
    }

    /// <summary>
    /// 屏幕运动采样 → 全画布四角的目标坐标（ffmpeg perspective sense=destination 顺序 TL,TR,BL,BR）。
    /// 变换 = 卡片 tilt(rotX/rotY 透视) → anchor 缩放+rotZ → manualOffset 位移。
    /// </summary>
    public static (double X, double Y)[] ScreenCorners(
        ScreenMotionSample sample, ScreenMotionSample baseSample,
        double canvasW, double canvasH,
        double cardX, double cardY, double cardW, double cardH, OpeningPose? opening = null)
    {
        var s = sample.State;
        // 1. tilt：卡片局部四角 → tilt 后四角（平移到画布坐标）
        var tiltQuad = PerspectiveGeometry.MotionCorners(cardW, cardH, s.RotationX, s.RotationY,
            s.Perspective, sample.ProjectionAnchor.X, sample.ProjectionAnchor.Y);
        var srcCard = new[] { (cardX, cardY), (cardX + cardW, cardY), (cardX, cardY + cardH), (cardX + cardW, cardY + cardH) };
        var dstTilt = tiltQuad
            .Select(p => (p.X + cardX, p.Y + cardY)).ToArray();
        var hTilt = Homography.FromQuad(srcCard, dstTilt);

        // 2. anchor 缩放 + rotZ + 位移（manualOffset 差量，内容中心 vs 画布中心）
        var mo = sample.ManualOffset ?? new CompositionPoint(0, 0);
        var moBase = baseSample.ManualOffset ?? new CompositionPoint(0, 0);
        var anchorPx = cardX + sample.ProjectionAnchor.X * cardW;
        var anchorPy = cardY + sample.ProjectionAnchor.Y * cardH;
        var hMove = Homography.AnchorScaleRot(anchorPx, anchorPy,
            Math.Max(s.Scale, 0.05), s.RotationZ, mo.X - moBase.X, mo.Y - moBase.Y);

        var h = hTilt.Then(hMove);
        if (opening is { } pose)
        {
            var quad = PerspectiveGeometry.MotionCorners(cardW, cardH, pose.RotationX, pose.RotationY, pose.Perspective, .5, .5);
            var projection = Homography.FromQuad(srcCard, quad.Select(p => (p.X + cardX, p.Y + cardY)).ToArray());
            var affine = Homography.AnchorScaleRot(cardX + cardW / 2, cardY + cardH / 2, pose.Scale, 0, pose.X, pose.Y);
            h = h.Then(projection).Then(affine);
        }
        return new[]
        {
            h.Apply(0, 0), h.Apply(canvasW, 0), h.Apply(0, canvasH), h.Apply(canvasW, canvasH),
        };
    }

    /// <summary>摄像头轨道的画布像素几何（对齐 CanvasLayout.Bubble 语义 + Mac fullscreen 渐变）。</summary>
    public readonly record struct CameraFrame(
        double X, double Y, double W, double H, double Opacity);

    public static CameraFrame CameraPixels(
        CameraMotionSample s, int canvasW, int canvasH)
    {
        // size = 画布宽占比；fullscreenProgress 把矩形过渡到全画布。
        var w = canvasW * (s.Size * (1 - s.FullscreenProgress) + s.FullscreenProgress);
        var aspect = Math.Max(s.AspectRatio, 0.05);
        var h = w / aspect;
        // 超画布时等比收回（与 Bubble 的 fit 相同）。
        var fit = Math.Min(1, Math.Min(canvasW / w, canvasH / h));
        w *= fit; h *= fit;
        var x = s.Position.X * canvasW - w / 2;
        var y = s.Position.Y * canvasH - h / 2;
        return new CameraFrame(x, y, w, h, s.Opacity);
    }
}
