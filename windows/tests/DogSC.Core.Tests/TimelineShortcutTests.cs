using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class TimelineShortcutTests
{
    static ProjectDocument Project()
    {
        var asset = new MediaAsset { Duration = 10 };
        return new() { Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 10 }] };
    }

    [Fact]
    public void SelectedZoomSplitPreservesTargetAndHandsOffWithoutReturn()
    {
        var p = Project(); var z = new ZoomSpan { Start = 1, Duration = 4, Scale = 2, FocusX = .2, EnterDuration = .3, ExitDuration = .4 };
        p.Zooms.Add(z);
        var id = TimelineShortcutEdits.SplitEffect(p, z.Id, 3);
        var right = p.Zooms.Single(x => x.Id == id);
        Assert.Equal(2, z.Duration); Assert.Equal(.4, z.ExitDuration);
        Assert.Equal(4, z.AnimationDuration); Assert.Equal(2, right.AnimationOffset);
        Assert.Equal(3, right.Start); Assert.Equal(2, right.Duration); Assert.Equal(.4, right.ExitDuration);
        Assert.Equal(.3, right.EnterDuration); Assert.Equal(.2, right.FocusX);
        Assert.Single(p.Clips); Assert.Equal(10, TimelineMath.Duration(p));
        Assert.Equal(2, new ZoomMotion(p).At(3.1).Scale, 8);
    }

    [Fact]
    public void SelectedMotionSplitCarriesRemainingEntryAndReturn()
    {
        var p = Project(); var clip = new ScreenMotionClip { GroupId = "layout", Timing = new() { StartTime = 1, Duration = 3, LeadInDuration = 1, ReturnDuration = .4 } };
        p.ScreenMotions.Add(clip);
        var id = TimelineShortcutEdits.SplitEffect(p, clip.Id, 1.5);
        var right = p.ScreenMotions.Single(x => x.Id == id);
        Assert.Equal(.5, clip.Timing.Duration); Assert.Equal(0, clip.Timing.ReturnDuration);
        Assert.Equal(.5, right.Timing.LeadInDuration); Assert.Equal(.4, right.Timing.ReturnDuration);
        Assert.NotEqual(clip.Timing.Id, right.Timing.Id); Assert.NotEqual(clip.GroupId, right.GroupId);
        Assert.Single(p.Clips);
    }

    [Fact]
    public void TrimmingSelectedCameraUpdatesLinkedScreenWithoutRipple()
    {
        var p = Project();
        p.ScreenMotions.Add(new() { GroupId = "layout", Timing = new() { StartTime = 1, Duration = 3 } });
        var camera = new CameraMotionClip { GroupId = "layout", Timing = new() { StartTime = 1, Duration = 3, LeadInDuration = .7 } };
        p.CameraMotions.Add(camera);
        Assert.True(TimelineShortcutEdits.TrimEffect(p, camera.Id, 2, true));
        Assert.Equal(2, camera.Timing.StartTime); Assert.Equal(2, camera.Timing.Duration);
        Assert.Equal(2, p.ScreenMotions[0].Timing.StartTime); Assert.Equal(2, p.ScreenMotions[0].Timing.Duration);
        Assert.Equal(10, TimelineMath.Duration(p));
    }

    [Fact]
    public void FullDurationStickerSplitsWithoutAnOpacityGap()
    {
        var p = Project(); var sticker = new StickerImage { Timing = new() { Duration = 0 } }; p.Canvas.Stickers.Add(sticker);
        var id = TimelineShortcutEdits.SplitEffect(p, sticker.Id, 3);
        var right = p.Canvas.Stickers.Single(x => x.Id == id);
        Assert.Equal(3, sticker.Timing.Duration); Assert.Equal(7, right.Timing.Duration);
        Assert.Equal(1, EffectTiming.OpacityAt(sticker.Timing, 2.99)); Assert.Equal(1, EffectTiming.OpacityAt(right.Timing, 3));
        Assert.True(TimelineShortcutEdits.TrimEffect(p, right.Id, 5, false)); Assert.Equal(2, right.Timing.Duration);
    }

    [Fact]
    public void BoundarySplitDoesNotMutateOrFallBackToPrimaryTrack()
    {
        var p = Project(); var zoom = new ZoomSpan { Start = 1, Duration = 2 }; p.Zooms.Add(zoom);
        Assert.Null(TimelineShortcutEdits.SplitEffect(p, zoom.Id, 1.01));
        Assert.False(TimelineShortcutEdits.TrimEffect(p, zoom.Id, 7, false));
        Assert.Single(p.Zooms); Assert.Single(p.Clips); Assert.Equal(2, zoom.Duration);
    }
}
