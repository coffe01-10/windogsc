using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class ZoomMotionTests
{
    static ProjectDocument Project(params CursorPoint[] points)
    {
        var asset = new MediaAsset { Duration = 10, Width = 640, Height = 360, Video = "media/screen.mp4", Cursor = points.ToList() };
        return new() { Width = 640, Height = 360, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 10 }] };
    }
    static CursorPoint Click(double t, double x, double y = .5) => new() { Time = t, X = x, Y = y, Click = true };

    [Fact]
    public void NearbyClicksKeepOneCompositionAndDistantClicksPanWithoutZoomingOut()
    {
        var project = Project(Click(1, .2), Click(1.5, .25), Click(2.4, .85), Click(2.7, .8));
        Assert.Equal(1, TimelineEdits.AutoZoom(project));
        var motion = new ZoomMotion(project);
        Assert.Equal(project.Zooms[0].FocusX, motion.TargetAt(project.Zooms[0], 0).X);
        Assert.Equal(1.6, motion.At(1.8).Scale, 6);
        Assert.Equal(motion.At(1.58).X, motion.At(1.68).X, 6);
        Assert.Equal(0, motion.At(1.6).X, 6);
        Assert.Equal(1, motion.At(2.8).X, 6);
        for (double t = 1.7; t < 3; t += .01) Assert.Equal(1.6, motion.At(t).Scale, 6);
    }

    [Fact]
    public void RapidRetargetPreservesPositionAndVelocity()
    {
        var project = Project(Click(1, .2), Click(1.3, .9), Click(2.3, .2));
        TimelineEdits.AutoZoom(project);
        var motion = new ZoomMotion(project);
        const double boundary = 1, h = .00001; // first zoom is still entering when the next target starts moving.
        var left = (motion.At(boundary).X - motion.At(boundary - h).X) / h;
        var right = (motion.At(boundary + h).X - motion.At(boundary).X) / h;
        Assert.InRange(Math.Abs(left - right), 0, .002);
        Assert.InRange(Math.Abs(motion.At(boundary - h).X - motion.At(boundary + h).X), 0, .001);
        Assert.True(motion.At(boundary).Scale > 1);
    }

    [Fact]
    public void QuickOppositeClicksRemainVisibleAtTheirClickTimes()
    {
        var points = new[] { Click(1, .12), Click(2.1, .86), Click(2.4, .12), Click(3.2, .79) };
        var project = Project(points);
        TimelineEdits.AutoZoom(project);
        var timeline = new CursorTimeline(project);
        foreach (var point in points)
        {
            var location = timeline.InFrame((point.X, point.Y), point.Time);
            Assert.InRange(location.X, .05, .95);
            Assert.InRange(location.Y, .05, .95);
        }
    }

    [Fact]
    public void TouchingManualSpansHandOffAtFullScaleAndReturnOnlyAtEnd()
    {
        var project = Project();
        project.Zooms = [new() { Start = 1, Duration = 2, Scale = 2, FocusX = .2, Easing = "smooth" },
            new() { Start = 3, Duration = 2, Scale = 1.5, FocusX = .8, Easing = "smooth" }];
        var motion = new ZoomMotion(project);
        Assert.Equal(2, motion.At(3).Scale, 8);
        Assert.Equal(.2, motion.At(3).X, 8);
        Assert.InRange(Math.Abs(motion.At(2.99999).Scale - motion.At(3.00001).Scale), 0, .00001);
        Assert.Equal(1.5, motion.At(4).Scale, 8);
        Assert.Equal(1, motion.At(5).Scale, 8);
    }

    [Fact]
    public void ActivityExtendsHoldButStationarySamplingDoesNot()
    {
        var project = Project(Click(1, .2));
        for (double t = 1.1; t < 5; t += .1) project.Assets[0].Cursor.Add(new() { Time = t, X = .2, Y = .5 });
        TimelineEdits.AutoZoom(project);
        Assert.Equal(2.6, project.Zooms[0].Start + project.Zooms[0].Duration, 6);
        foreach (var point in project.Assets[0].Cursor.Where(p => !p.Click)) point.X = .2 + point.Time / 20;
        TimelineEdits.AutoZoom(project);
        Assert.Single(project.Zooms);
        Assert.True(project.Zooms[0].Start + project.Zooms[0].Duration > 6);
    }

    [Fact]
    public void RegenerationPreservesManualSpansAndGroupsOnEitherSide()
    {
        var project = Project(Click(1, .2), Click(1.8, .4), Click(2.2, .8), Click(3, .9));
        var manual = new ZoomSpan { Start = 1.5, Duration = 1, Scale = 2.5 };
        project.Zooms.Add(manual);
        Assert.Equal(2, TimelineEdits.AutoZoom(project));
        Assert.Equal(2, TimelineEdits.AutoZoom(project));
        Assert.Equal(3, project.Zooms.Count);
        Assert.Same(manual, project.Zooms.Single(z => z.Origin == "manual"));
        Assert.Equal(2.5, manual.Scale);
        foreach (var z in project.Zooms.Where(z => z.Origin == "automatic"))
            Assert.True(z.Start + z.Duration <= manual.Start || z.Start >= manual.Start + manual.Duration);
    }

    [Fact]
    public void ClickFocusUsesCroppedFrameAndTrimmedSpeedAdjustedClock()
    {
        var project = Project(Click(1, .2), Click(4, .5));
        project.Clips[0].SourceStart = 2; project.Clips[0].SourceEnd = 8; project.Clips[0].Speed = 2;
        project.Canvas.Crop = new() { X = .4, Y = 0, Width = .5, Height = 1 };
        TimelineEdits.AutoZoom(project, scale: 2);
        Assert.Single(project.Zooms);
        Assert.Equal(.874, project.Zooms[0].Start, 6);
        Assert.Equal(0, new ZoomMotion(project).At(1.8).X, 6);
        var cursor = new CursorTimeline(project);
        var point = cursor.InFrame((.5, .5), 1.8);
        Assert.Equal(.4, point.X, 6);
    }

    [Fact]
    public void CopiesPreserveIndependentTransitionsAndCurves()
    {
        var original = new ZoomSpan { Origin = "automatic", Easing = "smooth", EnterDuration = .8, ExitDuration = .2 };
        var copy = TimelineEdits.CopyZoom(original);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original.Origin, copy.Origin);
        Assert.Equal(original.Easing, copy.Easing);
        Assert.Equal(original.EnterDuration, copy.EnterDuration);
        Assert.Equal(original.ExitDuration, copy.ExitDuration);
    }

    [Fact]
    public void SpringParametersChangeMotionAndKeepExactEndpoints()
    {
        var project = Project();
        var span = new ZoomSpan { Start = 1, Duration = 3, Scale = 2, FocusX = .2, Easing = "spring", EnterDuration = .7, ExitDuration = .3 };
        project.Zooms.Add(span);
        var motion = new ZoomMotion(project);
        Assert.Equal(CanvasLayout.ZoomScaleAt(span, 1.25), motion.At(1.25).Scale, 8);
        Assert.Equal(CanvasLayout.ZoomScaleAt(span, 3.85), motion.At(3.85).Scale, 8);
        Assert.Equal(1, motion.At(1).Scale, 8);
        Assert.Equal(2, motion.At(2).Scale, 8);
        Assert.Equal(1, motion.At(4).Scale, 8);
        span.SpringMass = 6;
        Assert.True(Math.Abs(new ZoomMotion(project).At(1.25).Scale - motion.At(1.25).Scale) > .05);
        var copy = TimelineEdits.CopyZoom(span);
        Assert.Equal(6, copy.SpringMass);
        Assert.Equal(210, copy.SpringStiffness);
        Assert.Equal(42, copy.SpringDamping);
    }

    [Fact]
    public void LongEntryAndShortExitRemainIndependentAndShortSpansFitBothWindows()
    {
        var project = Project();
        var span = new ZoomSpan { Start = 1, Duration = 3, Scale = 2, Easing = "linear", EnterDuration = 2, ExitDuration = .2 };
        project.Zooms.Add(span);
        Assert.Equal(1.875, new ZoomMotion(project).At(2.75).Scale, 8);
        span.Duration = 1.1;
        Assert.Equal((1, .1), ZoomMotion.Windows(span));
    }
}
