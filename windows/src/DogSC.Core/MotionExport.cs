using System.Globalization;
using System.Text;

namespace DogSC.Core;

/// <summary>把运动轨道采样成分段线性 ffmpeg 表达式（eval=frame 用 t）。</summary>
public static class MotionExport
{
    private static string F(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    /// <summary>
    /// 把采样点连成 if(lt(t,T),lerp...) 嵌套表达式；空采样返回 outsideValue，
    /// 首样本前取首值、末样本后取末值。
    /// </summary>
    public static string Piecewise(
        IReadOnlyList<(double Time, double Value)> samples,
        string outsideValue, string timeVar = "t", bool escapeCommas = false)
    {
        if (samples.Count == 0) return outsideValue;
        var comma = escapeCommas ? "\\," : ",";
        var sb = new StringBuilder("if(lt(").Append(timeVar).Append(comma).Append(F(samples[0].Time)).Append(")")
            .Append(comma).Append(F(samples[0].Value)).Append(comma);
        for (var i = 1; i < samples.Count; i++)
        {
            var (t0, v0) = samples[i - 1];
            var (t1, v1) = samples[i];
            var dt = t1 - t0;
            sb.Append("if(lt(").Append(timeVar).Append(comma).Append(F(t1)).Append(")").Append(comma);
            if (dt <= 0.000_001 || Math.Abs(v1 - v0) < 0.000_001)
            {
                sb.Append(F(v1));
            }
            else
            {
                sb.Append('(').Append(F(v0)).Append('+').Append(F(v1 - v0)).Append('*')
                    .Append("((").Append(timeVar).Append('-').Append(F(t0)).Append(")/").Append(F(dt)).Append("))");
            }
            sb.Append(comma);
        }
        sb.Append(F(samples[^1].Value));
        for (var i = 0; i < samples.Count; i++) sb.Append(')');
        return sb.ToString();
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
