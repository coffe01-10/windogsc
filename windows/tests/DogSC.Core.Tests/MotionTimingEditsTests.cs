using DogSC.Core;
using Xunit;
namespace DogSC.Core.Tests;
public class MotionTimingEditsTests
{
    static ProjectDocument Project() => new()
    {
        ScreenMotions = [new() { Timing = new() { StartTime = 2, Duration = 4, LeadInDuration = 2, ReturnDuration = 2, PreferredReturnDuration = 2 } }],
        CameraMotions = [new() { Timing = new() { StartTime = 8, Duration = 2 } }],
    };
    [Fact] public void DeleteMovesBothTracksAndKeepsEntryPhase()
    {
        var p = Project();
        MotionTimingEdits.RemoveRange(p, 1, 3);
        var t = p.ScreenMotions[0].Timing;
        Assert.Equal(1, t.StartTime); Assert.Equal(3, t.Duration);
        Assert.Equal(1, t.LeadInDuration); Assert.Equal(.5, t.LeadInProgressOffset);
        Assert.Equal(6, p.CameraMotions[0].Timing.StartTime);
    }
    [Fact] public void SpeedMapsEntryHoldAndReturnTogether()
    {
        var p = Project();
        MotionTimingEdits.Retime(p, 0, 10, 5);
        var t = p.ScreenMotions[0].Timing;
        Assert.Equal(1, t.StartTime); Assert.Equal(2, t.Duration);
        Assert.Equal(1, t.LeadInDuration); Assert.Equal(1, t.ReturnDuration);
        Assert.Equal(1, t.PreferredReturnDuration);
        Assert.Equal(4, p.CameraMotions[0].Timing.StartTime);
    }
    [Fact] public void DeletingWholeEffectRemovesIt()
    {
        var p = Project(); MotionTimingEdits.RemoveRange(p, 0, 8);
        Assert.Empty(p.ScreenMotions); Assert.Equal(0, p.CameraMotions[0].Timing.StartTime);
    }
    [Fact] public void SyncCannotMakeCameraPlayBackwards()
    {
        SyncAnchor[] anchors = [new() { SourceTime = 1, Offset = 0 }, new() { SourceTime = 2, Offset = -4 }];
        var a = 1.5 + SyncAnchorCurve.Offset(anchors, 1.5);
        var b = 1.6 + SyncAnchorCurve.Offset(anchors, 1.6);
        Assert.True(b > a); Assert.Equal(.01, SyncAnchorCurve.SourceRate(anchors, 1.5), 6);
    }
}
