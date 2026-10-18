using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class AnimationCutTests
{
    static ProjectDocument Project()
    {
        var asset = new MediaAsset { Duration = 10 };
        return new() { Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 10 }] };
    }

    [Theory]
    [InlineData(.3)]
    [InlineData(2)]
    [InlineData(3.7)]
    public void SplitZoomPreservesEveryRetainedFrame(double localCut)
    {
        var p = Project();
        var z = new ZoomSpan { Start = 1, Duration = 4, Scale = 2, FocusX = .15, FocusY = .8,
            EnterDuration = 1, ExitDuration = 1, Easing = "smooth" };
        p.Zooms.Add(z);
        var original = new ZoomMotion(ScenePreset.Clone(p));
        Assert.NotNull(TimelineShortcutEdits.SplitEffect(p, z.Id, 1 + localCut));
        var actual = new ZoomMotion(p);
        for (var frame = 0; frame < 300; frame++) Equal(original.At(frame / 60d), actual.At(frame / 60d));
    }

    [Theory]
    [InlineData(true, 1.3)]
    [InlineData(false, 1.3)]
    [InlineData(true, 4.7)]
    [InlineData(false, 4.7)]
    public void TrimZoomPreservesOriginalEntryAndExit(bool before, double cut)
    {
        var p = Project();
        var z = new ZoomSpan { Start = 1, Duration = 4, Scale = 2, FocusX = .2, EnterDuration = 1, ExitDuration = 1, Easing = "spring" };
        p.Zooms.Add(z);
        var original = new ZoomMotion(ScenePreset.Clone(p));
        Assert.True(TimelineShortcutEdits.TrimEffect(p, z.Id, cut, before));
        var actual = new ZoomMotion(p);
        for (var t = z.Start; t < z.Start + z.Duration - 1e-8; t += 1d / 60) Equal(original.At(t), actual.At(t));
    }

    static void Equal(ZoomPose expected, ZoomPose actual)
    {
        Assert.InRange(Math.Abs(expected.Scale - actual.Scale), 0, 1e-8);
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, 1e-8);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, 1e-8);
    }

    static ProjectDocument Layout()
    {
        var p = Project();
        p.ScreenMotions.Add(new() { GroupId = "pair", Timing = new() { StartTime = 1, Duration = 3, LeadInDuration = 1, ReturnDuration = .7, PreferredReturnDuration = .7, Easing = "spring" },
            Target = new() { Scale = .6, X = .2, RotationY = 25 } });
        p.CameraMotions.Add(new() { GroupId = "pair", Timing = ScenePreset.Clone(p.ScreenMotions[0].Timing),
            Target = new() { Layout = "fullscreen", X = .5, Y = .5 } });
        return p;
    }

    [Theory]
    [InlineData(1.3)]
    [InlineData(2.5)]
    public void SplitLayoutPreservesBothTracksAndIndependentPairs(double cut)
    {
        var p = Layout(); var original = ScenePreset.Clone(p);
        var id = TimelineShortcutEdits.SplitEffect(p, p.ScreenMotions[0].Id, cut);
        Assert.NotNull(id); Assert.Equal(2, p.CameraMotions.Count);
        var right = p.ScreenMotions.Single(x => x.Id == id);
        Assert.NotEqual(p.ScreenMotions[0].GroupId, right.GroupId);
        Assert.Equal(right.GroupId, p.CameraMotions[1].GroupId);
        EqualLayout(original, p, 1, 4.7);
        var leftCamera = p.CameraMotions[0].Timing.Duration;
        MotionTimingEdits.SetRange(p, right.Id, cut + .2, right.Timing.Duration - .2);
        Assert.Equal(leftCamera, p.CameraMotions[0].Timing.Duration);
        Assert.Equal(cut + .2, p.CameraMotions[1].Timing.StartTime, 8);
    }

    [Theory]
    [InlineData(true, 1.3)]
    [InlineData(false, 1.3)]
    [InlineData(true, 2.5)]
    public void TrimLayoutPreservesBothTracks(bool before, double cut)
    {
        var p = Layout(); var original = ScenePreset.Clone(p);
        Assert.True(TimelineShortcutEdits.TrimEffect(p, p.CameraMotions[0].Id, cut, before));
        EqualLayout(original, p, before ? cut : 1, before ? 4.7 : cut);
    }

    static void EqualLayout(ProjectDocument original, ProjectDocument actual, double start, double end)
    {
        var a = new ScreenMotionTrack(original.ScreenMotions); var b = new ScreenMotionTrack(actual.ScreenMotions);
        var ca = new CameraMotionTrack(original.CameraMotions); var cb = new CameraMotionTrack(actual.CameraMotions);
        for (var t = start; t < end - 1e-8; t += 1d / 60)
        {
            var x = a.SampleLayout(t); var y = b.SampleLayout(t);
            Assert.Equal(x.State.Scale, y.State.Scale, 8); Assert.Equal(x.State.RotationY, y.State.RotationY, 8);
            Assert.Equal(ca.Sample(t).FullscreenProgress, cb.Sample(t).FullscreenProgress, 8);
        }
    }

    [Theory]
    [InlineData(0, 1.3)]
    [InlineData(1.3, 1.7)]
    [InlineData(2.2, 2.8)]
    [InlineData(4.1, 4.4)]
    public void RippleDeletionPreservesSurvivingEntryHoldAndReturn(double start, double end)
    {
        var p = Layout();
        p.Zooms.Add(new() { Start = 1, Duration = 4, Scale = 2, EnterDuration = 1, ExitDuration = 1, Easing = "smooth" });
        var original = ScenePreset.Clone(p);
        Assert.True(TimelineEdits.TrySplit(p, end));
        if (start > 0) Assert.True(TimelineEdits.TrySplit(p, start));
        var deleted = TimelineMath.Place(p).Single(c => Math.Abs(c.Start - start) < 1e-8).Clip;
        Assert.True(TimelineEdits.TryDeleteClip(p, deleted.Id));
        var zoom = new ZoomMotion(p); var originalZoom = new ZoomMotion(original);
        var screen = new ScreenMotionTrack(p.ScreenMotions); var originalScreen = new ScreenMotionTrack(original.ScreenMotions);
        var camera = new CameraMotionTrack(p.CameraMotions); var originalCamera = new CameraMotionTrack(original.CameraMotions);
        for (var frame = 0; frame / 60d < TimelineMath.Duration(p); frame++)
        {
            var t = frame / 60d;
            var source = t < start ? t : t + end - start;
            Equal(originalZoom.At(source), zoom.At(t));
            Assert.Equal(originalScreen.SampleLayout(source).State.Scale, screen.SampleLayout(t).State.Scale, 7);
            Assert.Equal(originalCamera.Sample(source).FullscreenProgress, camera.Sample(t).FullscreenProgress, 7);
        }
    }

    [Fact]
    public void PointerFollowingAndFocusSurviveRepeatedCutsAndSerialization()
    {
        var p = Project();
        p.Assets[0].Cursor = Enumerable.Range(0, 601).Select(i => new CursorPoint
            { Time = i / 60d, X = i < 120 ? .2 : .8, Y = .4 }).ToList();
        var z = new ZoomSpan { Start = 1, Duration = 4, Origin = "automatic", Scale = 2, EnterDuration = 1, ExitDuration = 1,
            FocusEffect = new() { Blur = .5 } };
        p.Zooms.Add(z);
        var original = ScenePreset.Clone(p);
        var right = TimelineShortcutEdits.SplitEffect(p, z.Id, 1.3)!;
        right = TimelineShortcutEdits.SplitEffect(p, right, 3.6)!;
        Assert.True(TimelineShortcutEdits.TrimEffect(p, right, 4.7, false));
        p = ScenePreset.Clone(p); ProjectFile.Normalize(p);
        var a = new ZoomMotion(original); var b = new ZoomMotion(p);
        var fa = new FocusTrack(original); var fb = new FocusTrack(p);
        for (var t = 1d; t < 4.7; t += 1d / 60)
        {
            Equal(a.At(t), b.At(t));
            Assert.InRange(Math.Abs((fa.At(t)?.Progress ?? 0) - (fb.At(t)?.Progress ?? 0)), 0, 1e-8);
        }
    }

    [Fact]
    public void DraggingEdgesUsesTheSameRetainedAnimationAsKeyboardTrim()
    {
        var p = Layout(); p.Zooms.Add(new() { Start = 1, Duration = 4, Scale = 2, EnterDuration = 1, ExitDuration = 1 });
        var original = ScenePreset.Clone(p);
        Assert.True(MotionTimingEdits.SetRange(p, p.CameraMotions[0].Id, 1.3, 2.7));
        Assert.True(TimelineEdits.TryMoveZoom(p, p.Zooms[0].Id, 1.3, 3.7));
        EqualLayout(original, p, 1.3, 4.7);
        var a = new ZoomMotion(original); var b = new ZoomMotion(p);
        for (var t = 1.3; t < 5; t += 1d / 60) Equal(a.At(t), b.At(t));
    }
}
