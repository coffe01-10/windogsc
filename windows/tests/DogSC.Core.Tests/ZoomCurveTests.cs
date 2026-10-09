using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class ZoomCurveTests
{
    static ProjectDocument Sample(string easing) => new()
    {
        Width = 640, Height = 360,
        Assets = [new() { Id = "a", Video = "screen.mp4", Duration = 10, Width = 640, Height = 360 }],
        Clips = [new() { Id = "c", AssetId = "a", SourceEnd = 10 }],
        Zooms = [new() { Start = 0, Duration = 2, Scale = 2, FocusX = .6, FocusY = .4,
            Easing = easing, EnterDuration = 1, ExitDuration = 0 }],
    };

    [Theory]
    [InlineData("quadratic")]
    [InlineData("cubic")]
    [InlineData("custom")]
    public void BezierEasingsReachTargetAndStartAtRest(string easing)
    {
        var motion = new ZoomMotion(Sample(easing));
        Assert.Equal(1, motion.At(0).Scale, 3);
        Assert.Equal(2, motion.At(1.999).Scale, 3);
        Assert.Equal(.6, motion.At(1.999).X, 3);
        // 缓入段中点应落在两端之间。
        Assert.InRange(motion.At(.5).Scale, 1.0, 2.0);
    }

    [Fact]
    public void CustomCurveUsesSpanControlPoints()
    {
        var project = Sample("custom");
        project.Zooms[0].CurveX1 = 0;
        project.Zooms[0].CurveY1 = 1;
        project.Zooms[0].CurveX2 = 0;
        project.Zooms[0].CurveY2 = 1;
        var motion = new ZoomMotion(project);
        // (0,1)(0,1) 曲线前段猛冲：u=.3 时进度远超线性。
        Assert.True(motion.At(.3).Scale > 1.3, "custom curve should ease fast at start");
    }

    [Fact]
    public void BezierExpressionExportsPiecewise()
    {
        var motion = new ZoomMotion(Sample("cubic"));
        var (scale, _, _) = motion.Expressions(0, 4, "t");
        Assert.Contains("if(lt(", scale);
        Assert.InRange(ZoomMotion.Progress(Sample("quadratic").Zooms[0], .25), .08, .18);
    }
}
