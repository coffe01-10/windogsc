using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class AutomaticFocusTests
{
    static ProjectDocument Project()
    {
        var asset = new MediaAsset { Width = 320, Height = 180, Duration = 4, Video = "screen.mp4", Cursor =
            [new() { Time = 0, X = .5, Y = .5 }, new() { Time = .6, X = .5, Y = .5, Click = true },
             new() { Time = 1, X = .55, Y = .55 }, new() { Time = 1.2, X = .55, Y = .55 },
             new() { Time = 1.8, X = .9, Y = .55 }, new() { Time = 2.2, X = .9, Y = .55 },
             new() { Time = 2.8, X = .2, Y = .55 }, new() { Time = 3.3, X = .2, Y = .55 }] };
        return new() { Width = 320, Height = 180, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 4 }],
            Zooms = [new() { Start = .2, Duration = 3.2, Scale = 2, Origin = "automatic", EnterDuration = .4, ExitDuration = .3 }] };
    }

    [Fact]
    public void CentralMotionDoesNotShakeCameraAndLeavingRestRegionUpdatesOnlyOneAxis()
    {
        var motion = new ZoomMotion(Project());
        Assert.Equal(.5, motion.At(1.2).X, 8);
        Assert.Equal(.5, motion.At(1.2).Y, 8);
        Assert.True(motion.At(2.2).X > .85);
        Assert.Equal(.5, motion.At(2.2).Y, 8);
        Assert.True(motion.At(3).X < .3);
    }

    [Fact]
    public void RandomSeekingUsesSameSpringAndSpringParametersChangeActualFollowing()
    {
        var project = Project(); var motion = new ZoomMotion(project); var before = motion.At(1.75);
        _ = motion.At(3.1); _ = motion.At(.3);
        Assert.Equal(before, motion.At(1.75));
        project.Zooms[0].SpringMass = 6;
        var slower = new ZoomMotion(project);
        Assert.True(Math.Abs(slower.At(1.75).X - before.X) > .01);
        const double h = .0001;
        var leftVelocity = (motion.At(1.4).X - motion.At(1.4 - h).X) / h;
        var rightVelocity = (motion.At(1.4 + h).X - motion.At(1.4).X) / h;
        Assert.InRange(Math.Abs(leftVelocity - rightVelocity), 0, .08);
    }

    [Fact]
    public void RetainedSourceCropSpeedAndTimingAdvanceReachTheSameComposition()
    {
        var normal = Project(); var changed = Project();
        changed.Clips[0].SourceStart = 2; changed.Clips[0].SourceEnd = 10; changed.Clips[0].Speed = 2;
        changed.Assets[0].Duration = 10;
        changed.Canvas.Crop = new() { X = .2, Y = .1, Width = .6, Height = .8 };
        changed.Canvas.Cursor.TimingAdvance = .1;
        foreach (var p in changed.Assets[0].Cursor) { p.Time = 2.1 + p.Time * 2; p.X = .2 + p.X * .6; p.Y = .1 + p.Y * .8; }
        var a = new ZoomMotion(normal); var b = new ZoomMotion(changed);
        foreach (var t in new[] { .2, .4, 1.2, 1.75, 2.2, 2.9, 3.25 })
        {
            Assert.Equal(a.At(t).X, b.At(t).X, 7);
            Assert.Equal(a.At(t).Y, b.At(t).Y, 7);
        }
    }

    [Fact]
    public void ManualTargetsRemainIndependentOfPointerAndTouchingSpansHaveNoPositionJump()
    {
        var project = Project();
        project.Zooms.Add(new() { Start = 3.4, Duration = .6, Scale = 1.5, FocusX = .8, FocusY = .3 });
        var motion = new ZoomMotion(project);
        Assert.InRange(Math.Abs(motion.At(3.4 - 1e-8).X - motion.At(3.4).X), 0, 1e-6);
        Assert.Equal(.8, motion.TargetAt(project.Zooms[1], 3.7).X, 7);
    }

    [Fact]
    public void ImportRetainsCursorSpringAndFollowingPolicy()
    {
        var imported = MacProjectImporter.Import("""
            { "title":"Pointer settings", "media":{"screen":{"relativePath":"screen.mp4"}},
              "timeline":{"sourceSequence":{"kind":"edited","segments":[{"sourceStart":0,"sourceDuration":4}]}},
              "motion":{"cursor":"适中","cursorSpringMass":4,"cursorSpringStiffness":320,"cursorSpringDamping":75},
              "cursorStyle":{"assetID":"system.arrow"} }
            """);
        Assert.Equal("pointer", imported.Project.Motion.AutomaticFollow);
        Assert.Equal("medium", imported.Project.Canvas.Cursor.Motion);
        Assert.Equal(4, imported.Project.Canvas.Cursor.SpringMass);
        Assert.Equal(320, imported.Project.Canvas.Cursor.SpringStiffness);
        Assert.Equal(75, imported.Project.Canvas.Cursor.SpringDamping);
        Assert.DoesNotContain(imported.DroppedFields, f => f.StartsWith("motion.cursor"));
        var restored = ScenePreset.Clone(imported.Project);
        Assert.Equal(4, restored.Canvas.Cursor.SpringMass);
        Assert.Equal("pointer", restored.Motion.AutomaticFollow);
    }
}
