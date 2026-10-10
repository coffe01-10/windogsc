namespace DogSC.Core;

/// <summary>运动片段通用缓动：对齐 Mac ZoomInterpolator.easedProgress。</summary>
public static class MotionEasing
{
    public const double AdjacencyTolerance = 1.0 / 120.0;

    /// <summary>预设曲线（与 Mac ZoomBezierCurve 同参）。</summary>
    public static double[] CurveOf(string easing) => easing switch
    {
        "linear" => [0, 0, 1, 1],
        "quadratic" => [0.45, 0, 0.55, 1],
        "cubic" => [0.65, 0, 0.35, 1],
        "quintic" => [0.5, 0, 0.5, 1],
        _ => [0.65, 0, 0.35, 1],
    };

    public static double EasedProgress(
        double value, string easing, double[]? customCurve = null,
        double springMass = 2.4, double springStiffness = 210, double springDamping = 42)
    {
        var t = Math.Clamp(value, 0, 1);
        if (easing == "spring")
            return SpringProgress(t, springMass, springStiffness, springDamping);
        var c = easing == "custom" && customCurve is { Length: >= 4 } ? customCurve : CurveOf(easing);
        return BezierY(t, c[0], c[1], c[2], c[3]);
    }

    /// <summary>三次贝塞尔 y(x)：x(t) 用牛顿迭代反解后取 y。</summary>
    public static double BezierY(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var cx = 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t - x;
            var dx = 3 * x1 * (1 - t) * (1 - t) + 6 * (x2 - x1) * t * (1 - t) + 3 * (1 - x2) * t * t;
            if (Math.Abs(dx) < 1e-9) break;
            t -= cx / dx;
            if (Math.Abs(cx) < 1e-9) break;
        }
        t = Math.Clamp(t, 0, 1);
        return 3 * y1 * t * (1 - t) * (1 - t) + 3 * y2 * t * t * (1 - t) + t * t * t;
    }

    /// <summary>归一化欠阻尼弹簧（Mac springProgress，duration=0.72）。</summary>
    public static double SpringProgress(double value, double mass, double stiffness, double damping)
    {
        var t = Math.Clamp(value, 0, 1);
        if (t is <= 0 or >= 1) return t;
        var safeMass = Math.Max(mass, 0.01);
        var safeStiffness = Math.Max(stiffness, 0.01);
        var safeDamping = Math.Max(damping, 0.01);
        var omega0 = Math.Sqrt(safeStiffness / safeMass);
        var zeta = Math.Min(safeDamping / (2 * Math.Sqrt(safeStiffness * safeMass)), 0.9999);
        var omegaD = omega0 * Math.Sqrt(Math.Max(1 - zeta * zeta, 1e-9));
        const double duration = 0.72;
        double Response(double seconds)
        {
            var envelope = Math.Exp(-zeta * omega0 * seconds);
            var oscillation = Math.Cos(omegaD * seconds)
                + (zeta * omega0 / omegaD) * Math.Sin(omegaD * seconds);
            return 1 - envelope * oscillation;
        }
        return Math.Clamp(Response(t * duration) / Response(duration), 0, 1);
    }

    internal static double Mix(double from, double to, double p) => from + (to - from) * p;

    internal static double MixAngle(double from, double to, double p)
    {
        var delta = (to - from) % 360;
        if (delta > 180) delta -= 360;
        if (delta < -180) delta += 360;
        return from + delta * p;
    }
}

/// <summary>片段序列求值骨架：找最后一个已开始的片段、判定相接、回落窗口。两种轨道共用。</summary>
public abstract class MotionClipTrack<TClip> where TClip : class
{
    protected readonly List<TClip> Clips;
    private readonly bool _indexed;

    protected MotionClipTrack(IEnumerable<TClip> clips, Func<TClip, TransitionTiming> timingOf, Func<TClip, string> idOf)
    {
        var list = clips.ToList();
        var needsRepair = list.Zip(list.Skip(1)).Any(pair =>
            timingOf(pair.First).StartTime > timingOf(pair.Second).StartTime);
        if (needsRepair)
        {
            list = list.OrderBy(c => timingOf(c).StartTime)
                .ThenBy(c => idOf(c), StringComparer.Ordinal).ToList();
        }
        Clips = list;
        var timingsValid = Clips.All(c =>
        {
            var t = timingOf(c);
            return double.IsFinite(t.StartTime) && double.IsFinite(t.Duration) && t.Duration > 0;
        });
        var nonOverlapping = Clips.Zip(Clips.Skip(1)).All(pair =>
        {
            var prev = timingOf(pair.First);
            var next = timingOf(pair.Second);
            var gap = next.StartTime - prev.EndTime;
            var touches = gap <= MotionEasing.AdjacencyTolerance;
            var clears = next.StartTime >= prev.EffectEndTime - 0.0001;
            return next.StartTime >= prev.EndTime - 0.0001 && (touches || clears);
        });
        _indexed = timingsValid && nonOverlapping;
        Timing = timingOf;
    }

    protected Func<TClip, TransitionTiming> Timing { get; }

    public IReadOnlyList<TClip> ClipList => Clips;
    protected bool UsesIndexedEvaluation => _indexed;

    /// <summary>二分：最后一个 startTime <= time 的片段索引。</summary>
    protected int? LastStartedClipIndex(double time)
    {
        var lower = 0;
        var upper = Clips.Count;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (Timing(Clips[middle]).StartTime <= time) lower = middle + 1;
            else upper = middle;
        }
        return lower > 0 ? lower - 1 : null;
    }

    protected bool TouchesNextClip(int index)
        => index + 1 < Clips.Count
           && Math.Abs(Timing(Clips[index + 1]).StartTime - Timing(Clips[index]).EndTime)
               <= MotionEasing.AdjacencyTolerance;

    /// <summary>进入过渡的线性→缓动进度。</summary>
    protected static double EnterProgress(TransitionTiming timing, double time,
        double springMass, double springStiffness, double springDamping)
    {
        var leadIn = Math.Min(timing.LeadInDuration, timing.Duration);
        if (leadIn <= 0.0001) return 1;
        var local = Math.Min((time - timing.StartTime) / leadIn, 1);
        var linear = timing.LeadInProgressOffset + (1 - timing.LeadInProgressOffset) * local;
        return MotionEasing.EasedProgress(linear, timing.Easing, timing.CustomCurve,
            springMass, springStiffness, springDamping);
    }

    /// <summary>回落窗口的线性→缓动进度（目标→基础）。</summary>
    protected static double ReturnProgress(TransitionTiming timing, double time,
        double springMass, double springStiffness, double springDamping)
    {
        var local = Math.Clamp((time - timing.EndTime) / timing.ReturnDuration, 0, 1);
        var linear = timing.ReturnProgressOffset + (1 - timing.ReturnProgressOffset) * local;
        return MotionEasing.EasedProgress(linear, timing.Easing, timing.CustomCurve,
            springMass, springStiffness, springDamping);
    }
}

/// <summary>屏幕运动轨道：片段从上一目标出发到 endTime 达目标；相接串联，断开带回落回基础。</summary>
public sealed class ScreenMotionTrack : MotionClipTrack<ScreenMotionClip>
{
    private enum Framing { Placement, Focus }

    public ScreenMotionTrack(IEnumerable<ScreenMotionClip> clips)
        : base(clips, c => c.Timing, c => c.Id) { }

    /// <summary>回落默认时长 0.7s（Mac requestedReturnDuration defaultTransition）。</summary>
    private static double ReturnOf(TransitionTiming t) =>
        t.PreferredReturnDuration ?? (t.ReturnDuration > 0.0001 ? t.ReturnDuration : 0);

    public ScreenMotionSample SampleLayout(
        double time, ScreenMotionState? baseState = null,
        ScreenAnchorViewport? viewport = null,
        double springMass = 2.4, double springStiffness = 210, double springDamping = 42)
    {
        var base_ = baseState ?? new ScreenMotionState();
        if (!UsesIndexedEvaluation || !double.IsFinite(time))
            return SampleScanning(time, base_, viewport, springMass, springStiffness, springDamping);
        var index = LastStartedClipIndex(time);
        if (index is null)
            return LayoutSample(base_, viewport, Framing.Placement);
        var clip = ClipList[index.Value];
        var timing = clip.Timing;
        if (time < timing.EndTime)
        {
            ScreenMotionState inherited;
            Framing inheritedFraming;
            if (index.Value > 0)
            {
                var previous = ClipList[index.Value - 1];
                var touches = Math.Abs(previous.Timing.EndTime - timing.StartTime)
                    <= MotionEasing.AdjacencyTolerance;
                if (!touches && ReturnOf(previous.Timing) > 0.0001)
                {
                    inherited = base_;
                    inheritedFraming = Framing.Placement;
                }
                else
                {
                    inherited = previous.Target;
                    inheritedFraming = Framing.Focus;
                }
            }
            else
            {
                inherited = base_;
                inheritedFraming = Framing.Placement;
            }
            var leadIn = Math.Min(timing.LeadInDuration, timing.Duration);
            if (leadIn <= 0.0001)
                return LayoutSample(clip.Target, viewport, Framing.Focus);
            var progress = EnterProgress(timing, time, springMass, springStiffness, springDamping);
            if (progress >= 1)
                return LayoutSample(clip.Target, viewport, Framing.Focus);
            return InterpolateLayout(inherited, clip.Target, progress, viewport,
                inheritedFraming, Framing.Focus,
                inheritedFraming == Framing.Focus ? new NormalizedPoint(inherited.AnchorX, inherited.AnchorY) : (NormalizedPoint?)null,
                new NormalizedPoint(clip.Target.AnchorX, clip.Target.AnchorY));
        }
        if (TouchesNextClip(index.Value))
            return LayoutSample(clip.Target, viewport, Framing.Focus);
        var falling = ReturnFalloff(index.Value, base_, time, viewport, springMass, springStiffness, springDamping);
        if (falling is not null) return falling.Value;
        return ReturnOf(timing) > 0.0001
            ? LayoutSample(base_, viewport, Framing.Placement)
            : LayoutSample(clip.Target, viewport, Framing.Focus);
    }

    /// <summary>当前生效片段（回落窗口外或无片段 = null）。</summary>
    public ScreenMotionClip? ActiveClip(double time)
    {
        if (!double.IsFinite(time)) return null;
        var index = LastStartedClipIndex(time);
        if (index is null) return null;
        var clip = ClipList[index.Value];
        return ReturnOf(clip.Timing) > 0 && time >= clip.Timing.EffectEndTime ? null : clip;
    }

    private ScreenMotionSample SampleScanning(
        double time, ScreenMotionState base_, ScreenAnchorViewport? viewport,
        double springMass, double springStiffness, double springDamping)
    {
        var inherited = base_;
        var inheritedFraming = Framing.Placement;
        int? previousIndex = null;
        for (var index = 0; index < ClipList.Count; index++)
        {
            var clip = ClipList[index];
            var timing = clip.Timing;
            if (!double.IsFinite(timing.StartTime) || !double.IsFinite(timing.Duration) || timing.Duration <= 0)
                continue;
            if (time < timing.StartTime)
            {
                if (previousIndex is not null)
                {
                    var falling = ReturnFalloff(previousIndex.Value, base_, time, viewport,
                        springMass, springStiffness, springDamping);
                    if (falling is not null) return falling.Value;
                }
                return LayoutSample(inherited, viewport, inheritedFraming);
            }
            if (time < timing.EndTime)
            {
                var leadIn = Math.Min(timing.LeadInDuration, timing.Duration);
                if (leadIn <= 0.0001)
                    return LayoutSample(clip.Target, viewport, Framing.Focus);
                var progress = EnterProgress(timing, time, springMass, springStiffness, springDamping);
                if (progress >= 1)
                    return LayoutSample(clip.Target, viewport, Framing.Focus);
                return InterpolateLayout(inherited, clip.Target, progress, viewport,
                    inheritedFraming, Framing.Focus,
                    inheritedFraming == Framing.Focus ? new NormalizedPoint(inherited.AnchorX, inherited.AnchorY) : (NormalizedPoint?)null,
                    new NormalizedPoint(clip.Target.AnchorX, clip.Target.AnchorY));
            }
            var touchesNext = TouchesNextClip(index);
            if (!touchesNext && ReturnOf(timing) > 0.0001)
            {
                inherited = base_;
                inheritedFraming = Framing.Placement;
            }
            else
            {
                inherited = clip.Target;
                inheritedFraming = Framing.Focus;
            }
            previousIndex = index;
        }
        if (previousIndex is not null)
        {
            var falling = ReturnFalloff(previousIndex.Value, base_, time, viewport,
                springMass, springStiffness, springDamping);
            if (falling is not null) return falling.Value;
            return ReturnOf(ClipList[previousIndex.Value].Timing) > 0.0001
                ? LayoutSample(base_, viewport, Framing.Placement)
                : LayoutSample(inherited, viewport, inheritedFraming);
        }
        return LayoutSample(inherited, viewport, inheritedFraming);
    }

    private ScreenMotionSample? ReturnFalloff(
        int index, ScreenMotionState base_, double time, ScreenAnchorViewport? viewport,
        double springMass, double springStiffness, double springDamping)
    {
        var clip = ClipList[index];
        var timing = clip.Timing;
        if (ReturnOf(timing) <= 0.0001 || TouchesNextClip(index)) return null;
        if (time > timing.EndTime + ReturnOf(timing)) return null;
        var progress = ReturnProgressWithDuration(timing, ReturnOf(timing), time,
            springMass, springStiffness, springDamping);
        return InterpolateLayout(clip.Target, base_, progress, viewport,
            Framing.Focus, Framing.Placement,
            inheritedProjectionAnchor: null,
            projectionAnchor: new NormalizedPoint(clip.Target.AnchorX, clip.Target.AnchorY));
    }

    private static double ReturnProgressWithDuration(TransitionTiming timing, double returnDuration,
        double time, double springMass, double springStiffness, double springDamping)
    {
        var local = Math.Clamp((time - timing.EndTime) / returnDuration, 0, 1);
        var linear = timing.ReturnProgressOffset + (1 - timing.ReturnProgressOffset) * local;
        return MotionEasing.EasedProgress(linear, timing.Easing, timing.CustomCurve,
            springMass, springStiffness, springDamping);
    }

    private static ScreenMotionSample LayoutSample(
        ScreenMotionState state, ScreenAnchorViewport? viewport, Framing framing)
        => new(state, viewport?.Let(v => DirectOffset(state, v, framing)),
            framing == Framing.Focus ? new NormalizedPoint(state.AnchorX, state.AnchorY) : new NormalizedPoint(state.X, state.Y));

    private static ScreenMotionSample InterpolateLayout(
        ScreenMotionState from, ScreenMotionState to, double progress,
        ScreenAnchorViewport? viewport,
        Framing fromFraming, Framing toFraming,
        NormalizedPoint? inheritedProjectionAnchor,
        NormalizedPoint projectionAnchor)
    {
        var t = Math.Clamp(progress, 0, 1);
        NormalizedPoint position;
        if (viewport is { } v)
        {
            // rect 空间线性插值：锚点全程钉在两端 rect 连线上。
            var scaledW0 = from.Scale * v.FittedWidth;
            var scaledW1 = to.Scale * v.FittedWidth;
            var scaledW = MotionEasing.Mix(scaledW0, scaledW1, t);
            var ux = MotionEasing.Mix(
                from.X * (v.CanvasWidth - scaledW0),
                to.X * (v.CanvasWidth - scaledW1), t);
            var scaledH0 = from.Scale * v.FittedHeight;
            var scaledH1 = to.Scale * v.FittedHeight;
            var scaledH = MotionEasing.Mix(scaledH0, scaledH1, t);
            var uy = MotionEasing.Mix(
                from.Y * (v.CanvasHeight - scaledH0),
                to.Y * (v.CanvasHeight - scaledH1), t);
            var denomX = v.CanvasWidth - scaledW;
            var denomY = v.CanvasHeight - scaledH;
            position = new NormalizedPoint(
                Math.Abs(denomX) > 0.001 ? ux / denomX : MotionEasing.Mix(from.X, to.X, t),
                Math.Abs(denomY) > 0.001 ? uy / denomY : MotionEasing.Mix(from.Y, to.Y, t));
        }
        else
        {
            position = new NormalizedPoint(
                MotionEasing.Mix(from.X, to.X, t),
                MotionEasing.Mix(from.Y, to.Y, t));
        }
        var state = new ScreenMotionState
        {
            X = position.X,
            Y = position.Y,
            Scale = MotionEasing.Mix(from.Scale, to.Scale, t),
            RotationX = MotionEasing.MixAngle(from.RotationX, to.RotationX, t),
            RotationY = MotionEasing.MixAngle(from.RotationY, to.RotationY, t),
            RotationZ = MotionEasing.MixAngle(from.RotationZ, to.RotationZ, t),
            Perspective = MotionEasing.Mix(from.Perspective, to.Perspective, t),
        };
        CompositionPoint? manualOffset = viewport is { } vp
            ? new CompositionPoint(
                MotionEasing.Mix(DirectOffset(from, vp, fromFraming).X, DirectOffset(to, vp, toFraming).X, t),
                MotionEasing.Mix(DirectOffset(from, vp, fromFraming).Y, DirectOffset(to, vp, toFraming).Y, t))
            : null;
        var anchor = inheritedProjectionAnchor is { } inherited
            ? new NormalizedPoint(
                MotionEasing.Mix(inherited.X, projectionAnchor.X, t),
                MotionEasing.Mix(inherited.Y, projectionAnchor.Y, t))
            : projectionAnchor;
        return new ScreenMotionSample(state, manualOffset, anchor);
    }

    /// <summary>装饰卡片的像素位移（中心原点），对齐 Mac directOffset。</summary>
    private static CompositionPoint DirectOffset(
        ScreenMotionState state, ScreenAnchorViewport viewport, Framing framing)
    {
        var cardScale = state.Scale;
        var contentWidth = viewport.FittedWidth * cardScale;
        var contentHeight = viewport.FittedHeight * cardScale;
        var border = viewport.BorderWidthAtScaleOne * cardScale;
        var left = border + viewport.DecorationLeftAtScaleOne * cardScale;
        var right = border + viewport.DecorationRightAtScaleOne * cardScale;
        var top = border + viewport.DecorationTopAtScaleOne * cardScale;
        var bottom = border + viewport.DecorationBottomAtScaleOne * cardScale;
        var decoratedWidth = contentWidth + left + right;
        var decoratedHeight = contentHeight + top + bottom;
        double contentCenterX, contentCenterY;
        if (framing == Framing.Placement)
        {
            contentCenterX = state.X * (viewport.CanvasWidth - decoratedWidth)
                + contentWidth / 2 + left;
            contentCenterY = state.Y * (viewport.CanvasHeight - decoratedHeight)
                + contentHeight / 2 + top;
        }
        else
        {
            var focusX = Math.Clamp(state.X, 0, 1);
            var focusY = Math.Clamp(state.Y, 0, 1);
            // 大缩放把目标点收进中央安全区（约 31%/69%），点本身仍是投影枢轴。
            var recenter = Math.Clamp((cardScale - 1) / 2, 0, 1) * 0.62;
            var boundedX = left + focusX * Math.Max(viewport.CanvasWidth - left - right, 0);
            var boundedY = top + focusY * Math.Max(viewport.CanvasHeight - top - bottom, 0);
            var targetX = MotionEasing.Mix(boundedX, (viewport.CanvasWidth + left - right) / 2, recenter);
            var targetY = MotionEasing.Mix(boundedY, (viewport.CanvasHeight + top - bottom) / 2, recenter);
            contentCenterX = targetX + (0.5 - focusX) * contentWidth;
            contentCenterY = targetY + (0.5 - focusY) * contentHeight;
        }
        return new CompositionPoint(
            contentCenterX - viewport.CanvasWidth / 2,
            contentCenterY - viewport.CanvasHeight / 2);
    }
}

internal static class NullableExt
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> f) => f(value);
}

/// <summary>摄像头运动轨道：离散布局先解析为连续宽高比/圆角/全屏进度再插值。</summary>
public sealed class CameraMotionTrack : MotionClipTrack<CameraMotionClip>
{
    public CameraMotionTrack(IEnumerable<CameraMotionClip> clips)
        : base(clips, c => c.Timing, c => c.Id) { }

    private static double ReturnOf(TransitionTiming t) =>
        t.PreferredReturnDuration ?? (t.ReturnDuration > 0.0001 ? t.ReturnDuration : 0);

    public CameraMotionSample Sample(
        double time, CameraMotionState? baseState = null,
        double cameraAspectRatio = 1, double canvasAspectRatio = 1.78,
        double springMass = 2.4, double springStiffness = 210, double springDamping = 42)
    {
        var base_ = baseState ?? new CameraMotionState();
        var resolvedBase = Resolve(base_, cameraAspectRatio, canvasAspectRatio);
        if (!UsesIndexedEvaluation || !double.IsFinite(time))
            return SampleScanning(time, resolvedBase, cameraAspectRatio, canvasAspectRatio,
                springMass, springStiffness, springDamping);
        var index = LastStartedClipIndex(time);
        if (index is null) return resolvedBase;
        var clip = ClipList[index.Value];
        var timing = clip.Timing;
        var target = Resolve(clip.Target, cameraAspectRatio, canvasAspectRatio);
        if (time < timing.EndTime)
        {
            CameraMotionSample inherited;
            if (index.Value > 0)
            {
                var previous = ClipList[index.Value - 1];
                var touches = Math.Abs(previous.Timing.EndTime - timing.StartTime)
                    <= MotionEasing.AdjacencyTolerance;
                inherited = !touches && ReturnOf(previous.Timing) > 0.0001
                    ? resolvedBase
                    : Resolve(previous.Target, cameraAspectRatio, canvasAspectRatio);
            }
            else
            {
                inherited = resolvedBase;
            }
            var leadIn = Math.Min(timing.LeadInDuration, timing.Duration);
            if (leadIn <= 0.0001) return target;
            var progress = EnterProgress(timing, time, springMass, springStiffness, springDamping);
            return TransitionSample(inherited, target, progress);
        }
        if (TouchesNextClip(index.Value)) return target;
        var falling = ReturnFalloff(index.Value, resolvedBase, time,
            cameraAspectRatio, canvasAspectRatio, springMass, springStiffness, springDamping);
        if (falling is not null) return falling.Value;
        return ReturnOf(timing) > 0.0001 ? resolvedBase : target;
    }

    private CameraMotionSample SampleScanning(
        double time, CameraMotionSample resolvedBase,
        double cameraAspectRatio, double canvasAspectRatio,
        double springMass, double springStiffness, double springDamping)
    {
        var inherited = resolvedBase;
        int? previousIndex = null;
        for (var index = 0; index < ClipList.Count; index++)
        {
            var clip = ClipList[index];
            var timing = clip.Timing;
            if (!double.IsFinite(timing.StartTime) || !double.IsFinite(timing.Duration) || timing.Duration <= 0)
                continue;
            if (time < timing.StartTime)
            {
                if (previousIndex is not null)
                {
                    var falling = ReturnFalloff(previousIndex.Value, resolvedBase, time,
                        cameraAspectRatio, canvasAspectRatio, springMass, springStiffness, springDamping);
                    if (falling is not null) return falling.Value;
                }
                return inherited;
            }
            var target = Resolve(clip.Target, cameraAspectRatio, canvasAspectRatio);
            if (time < timing.EndTime)
            {
                var leadIn = Math.Min(timing.LeadInDuration, timing.Duration);
                if (leadIn <= 0.0001) return target;
                var progress = EnterProgress(timing, time, springMass, springStiffness, springDamping);
                return TransitionSample(inherited, target, progress);
            }
            inherited = !TouchesNextClip(index) && ReturnOf(timing) > 0.0001 ? resolvedBase : target;
            previousIndex = index;
        }
        if (previousIndex is not null)
        {
            var falling = ReturnFalloff(previousIndex.Value, resolvedBase, time,
                cameraAspectRatio, canvasAspectRatio, springMass, springStiffness, springDamping);
            if (falling is not null) return falling.Value;
            return ReturnOf(ClipList[previousIndex.Value].Timing) > 0.0001 ? resolvedBase : inherited;
        }
        return inherited;
    }

    private CameraMotionSample? ReturnFalloff(
        int index, CameraMotionSample resolvedBase, double time,
        double cameraAspectRatio, double canvasAspectRatio,
        double springMass, double springStiffness, double springDamping)
    {
        var clip = ClipList[index];
        var timing = clip.Timing;
        if (ReturnOf(timing) <= 0.0001 || TouchesNextClip(index)) return null;
        if (time > timing.EndTime + ReturnOf(timing)) return null;
        var local = Math.Clamp((time - timing.EndTime) / ReturnOf(timing), 0, 1);
        var linear = timing.ReturnProgressOffset + (1 - timing.ReturnProgressOffset) * local;
        var progress = MotionEasing.EasedProgress(linear, timing.Easing, timing.CustomCurve,
            springMass, springStiffness, springDamping);
        var target = Resolve(clip.Target, cameraAspectRatio, canvasAspectRatio);
        return TransitionSample(target, resolvedBase, progress);
    }

    /// <summary>隐藏目标原位淡出；隐藏源在目标位置淡入；其余完整几何插值。</summary>
    private static CameraMotionSample TransitionSample(
        CameraMotionSample inherited, CameraMotionSample target, double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        if (target.Opacity < 0.01)
            return inherited with { Opacity = MotionEasing.Mix(inherited.Opacity, 0, p) };
        if (inherited.Opacity < 0.01)
            return target with { Opacity = MotionEasing.Mix(0, target.Opacity, p) };
        return Interpolate(inherited, target, p);
    }

    /// <summary>形状→连续参数，对齐 Mac CameraMotionTrack.resolve。</summary>
    public static CameraMotionSample Resolve(
        CameraMotionState state, double cameraAspectRatio, double canvasAspectRatio)
    {
        double aspect, cornerFraction, fullscreen;
        switch (state.Layout)
        {
            case "fullscreen":
                aspect = Math.Max(canvasAspectRatio, 0.01);
                cornerFraction = 0;
                fullscreen = 1;
                break;
            case "circle":
                aspect = 1; cornerFraction = 0.5; fullscreen = 0; break;
            case "horizontal":
                aspect = 1.5; cornerFraction = 0.5 * state.Roundness; fullscreen = 0; break;
            case "vertical":
                aspect = 0.72; cornerFraction = 0.5 * state.Roundness; fullscreen = 0; break;
            case "original":
                aspect = Math.Max(cameraAspectRatio, 0.01);
                cornerFraction = 0.5 * state.Roundness; fullscreen = 0; break;
            default: // square
                aspect = 1; cornerFraction = 0.5 * state.Roundness; fullscreen = 0; break;
        }
        return new CameraMotionSample(
            new NormalizedPoint(state.X, state.Y), state.Size,
            aspect, cornerFraction, fullscreen, state.Opacity);
    }

    private static CameraMotionSample Interpolate(
        CameraMotionSample from, CameraMotionSample to, double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        return new CameraMotionSample(
            new NormalizedPoint(
                MotionEasing.Mix(from.Position.X, to.Position.X, t),
                MotionEasing.Mix(from.Position.Y, to.Position.Y, t)),
            MotionEasing.Mix(from.Size, to.Size, t),
            MotionEasing.Mix(from.AspectRatio, to.AspectRatio, t),
            MotionEasing.Mix(from.CornerFraction, to.CornerFraction, t),
            MotionEasing.Mix(from.FullscreenProgress, to.FullscreenProgress, t),
            MotionEasing.Mix(from.Opacity, to.Opacity, t));
    }
}
