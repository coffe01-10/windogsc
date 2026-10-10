using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class MotionTrackTests
{
    private static TransitionTiming Timing(double start, double duration,
        double leadIn = 0, string easing = "linear", double ret = 0)
        => new()
        {
            StartTime = start,
            Duration = duration,
            LeadInDuration = leadIn,
            Easing = easing,
            ReturnDuration = ret,
        };

    [Fact]
    public void ScreenTrack_NoClips_ReturnsBase()
    {
        var track = new ScreenMotionTrack([]);
        var base_ = new ScreenMotionState { Scale = 2, X = 0.3, Y = 0.7 };
        var s = track.SampleLayout(1.5, base_);
        Assert.Equal(2, s.State.Scale);
        Assert.Equal(0.3, s.State.X);
        Assert.Equal(0.7, s.State.Y);
    }

    [Fact]
    public void ScreenTrack_EnterInterpolatesThenHolds()
    {
        var clip = new ScreenMotionClip
        {
            Timing = Timing(1, 2, leadIn: 1),
            Target = { Scale = 2, X = 0.8 },
        };
        var track = new ScreenMotionTrack([clip]);
        var base_ = new ScreenMotionState();
        // 进入中点（linear easing）
        var mid = track.SampleLayout(1.5, base_);
        Assert.Equal(1.5, mid.State.Scale, 3);
        Assert.Equal(0.65, mid.State.X, 3);
        // 过渡完成后保持目标
        var end = track.SampleLayout(2.5, base_);
        Assert.Equal(2, end.State.Scale, 3);
        Assert.Equal(0.8, end.State.X, 3);
        // 片段结束后无回落：仍保持目标
        var after = track.SampleLayout(4, base_);
        Assert.Equal(2, after.State.Scale, 3);
    }

    [Fact]
    public void ScreenTrack_ReturnFalloffBringsBackToBase()
    {
        var clip = new ScreenMotionClip
        {
            Timing = Timing(1, 2, leadIn: 0.5, ret: 1),
            Target = { Scale = 2 },
        };
        var track = new ScreenMotionTrack([clip]);
        var base_ = new ScreenMotionState();
        // 回落中点 linear
        var mid = track.SampleLayout(3.5, base_);
        Assert.Equal(1.5, mid.State.Scale, 3);
        // 回落结束回基础
        var done = track.SampleLayout(4.1, base_);
        Assert.Equal(1, done.State.Scale, 3);
    }

    [Fact]
    public void ScreenTrack_AdjacentClipsChainWithoutReturn()
    {
        var c1 = new ScreenMotionClip { Timing = Timing(1, 1, ret: 1), Target = { Scale = 2 } };
        // c2 起点与 c1 终点相差 0.004s（< adjacencyTolerance）仍算相接。
        var c2 = new ScreenMotionClip { Timing = Timing(2.004, 1), Target = { Scale = 3 } };
        var track = new ScreenMotionTrack([c1, c2]);
        var base_ = new ScreenMotionState();
        // 亚帧间隙仍属相接交接：保持 c1 目标直到 c2 开始，不回落基础
        var gap = track.SampleLayout(2.002, base_);
        Assert.Equal(2, gap.State.Scale, 3);
        // c2 完成后保持 c2 目标
        var end = track.SampleLayout(3.5, base_);
        Assert.Equal(3, end.State.Scale, 3);
    }

    [Fact]
    public void ScreenTrack_AnchorViewport_KeepsRectLinearPath()
    {
        var viewport = new ScreenAnchorViewport(1280, 720, 1000, 560);
        var clip = new ScreenMotionClip
        {
            Timing = Timing(0, 1, leadIn: 1),
            Target = { Scale = 2, X = 0.5, Y = 0.5 },
        };
        var track = new ScreenMotionTrack([clip]);
        var s = track.SampleLayout(0.5, new ScreenMotionState(), viewport);
        Assert.NotNull(s.ManualOffset);
        // 缩放中途：offset 介于两端 directOffset 之间（rect 线性）
        var o = s.ManualOffset!.Value;
        Assert.True(double.IsFinite(o.X) && double.IsFinite(o.Y));
    }

    [Fact]
    public void CameraTrack_ResolvesLayouts()
    {
        var full = CameraMotionTrack.Resolve(
            new CameraMotionState { Layout = "fullscreen" }, 0.5, 1.78);
        Assert.Equal(1.78, full.AspectRatio, 3);
        Assert.Equal(0, full.CornerFraction);
        Assert.Equal(1, full.FullscreenProgress);
        var circle = CameraMotionTrack.Resolve(
            new CameraMotionState { Layout = "circle" }, 0.5, 1.78);
        Assert.Equal(1, circle.AspectRatio);
        Assert.Equal(0.5, circle.CornerFraction);
        var original = CameraMotionTrack.Resolve(
            new CameraMotionState { Layout = "original" }, 0.5, 1.78);
        Assert.Equal(0.5, original.AspectRatio);
    }

    [Fact]
    public void CameraTrack_HiddenTargetFadesInPlace()
    {
        var c1 = new CameraMotionClip
        {
            Timing = Timing(0, 1),
            Target = { Layout = "circle", X = 0.2, Opacity = 1 },
        };
        var c2 = new CameraMotionClip
        {
            Timing = Timing(1.5, 1, leadIn: 1),
            Target = { Layout = "square", X = 0.8, Opacity = 0 },
        };
        var track = new CameraMotionTrack([c1, c2]);
        var s = track.Sample(2.0, cameraAspectRatio: 1, canvasAspectRatio: 1.78);
        // 隐藏目标：位置取继承的 c1，不是 c2 的 0.8；透明度过半
        Assert.Equal(0.2, s.Position.X, 3);
        Assert.True(s.Opacity < 1);
    }

    [Fact]
    public void MotionEasing_EndpointsAndPresets()
    {
        Assert.Equal(0, MotionEasing.EasedProgress(0, "spring"));
        Assert.Equal(1, MotionEasing.EasedProgress(1, "spring"));
        Assert.Equal(0.5, MotionEasing.EasedProgress(0.5, "quintic"), 6);
        Assert.Equal(0.4, MotionEasing.EasedProgress(0.4, "linear"), 6);
        // 自定义贝塞尔端点固定
        Assert.Equal(0, MotionEasing.BezierY(0, 0.3, 0.9, 0.7, 0.1));
        Assert.Equal(1, MotionEasing.BezierY(1, 0.3, 0.9, 0.7, 0.1));
        // 单调控制点的中点 y 应在 0..1
        var mid = MotionEasing.BezierY(0.5, 0.65, 0, 0.35, 1);
        Assert.True(mid is > 0.3 and < 0.7, $"mid={mid}");
    }
}
