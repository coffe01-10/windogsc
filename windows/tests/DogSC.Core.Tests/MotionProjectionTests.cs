using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class MotionProjectionTests
{
    [Fact]
    public void Normalize_PreservesNativePerspectiveRange()
    {
        var project = new ProjectDocument();
        project.ScreenMotions.Add(new ScreenMotionClip { Target = new ScreenMotionState { Perspective = 1.8 } });
        ProjectFile.Normalize(project);
        Assert.Equal(1.8, project.ScreenMotions[0].Target.Perspective);
    }

    [Fact]
    public void RestingMotion_DoesNotTranslateLetterboxedCard()
    {
        var rest = new ScreenMotionTrack([]).SampleLayout(-1, null, new ScreenAnchorViewport(1920, 1080, 1200, 700));
        var corners = MotionExport.ScreenCorners(rest, rest, 1920, 1080, 360, 190, 1200, 700);
        Assert.Equal(0, corners[0].X, 6); Assert.Equal(0, corners[0].Y, 6);
        Assert.Equal(1920, corners[3].X, 6); Assert.Equal(1080, corners[3].Y, 6);
    }

    [Fact]
    public void PerspectiveStrength_ChangesDepthWithoutChangingUnrotatedCard()
    {
        var flat = PerspectiveGeometry.MotionCorners(800, 450, 0, 0, 2, .25, .7);
        Assert.Equal(0, flat[0].X, 6); Assert.Equal(800, flat[3].X, 6);
        Assert.Equal(0, flat[0].Y, 6); Assert.Equal(450, flat[3].Y, 6);
        var orthographic = PerspectiveGeometry.MotionCorners(800, 450, 0, 35, 0);
        var perspective = PerspectiveGeometry.MotionCorners(800, 450, 0, 35, 2);
        Assert.All(orthographic, p => Assert.Equal(1, p.Weight));
        Assert.NotEqual(perspective[0].Weight, perspective[1].Weight);
        Assert.NotEqual(perspective[2].Y - perspective[0].Y, perspective[3].Y - perspective[1].Y);
        var original = PerspectiveGeometry.SourcePoint(perspective,
            PerspectiveGeometry.DestinationPoint(perspective, .3, .7).X,
            PerspectiveGeometry.DestinationPoint(perspective, .3, .7).Y);
        Assert.Equal(.3, original.X, 6); Assert.Equal(.7, original.Y, 6);
    }

    [Fact]
    public void MotionExport_UsesAuthoredPerspective()
    {
        var target = new ScreenMotionState { RotationX = 30, RotationY = 20, Perspective = 0 };
        var viewport = new ScreenAnchorViewport(1280, 720, 960, 540);
        var rest = new ScreenMotionTrack([]).SampleLayout(-1, null, viewport);
        var orthographic = MotionExport.ScreenCorners(ScreenMotionTrack.TargetLayout(target, viewport), rest, 1280, 720, 160, 90, 960, 540);
        target.Perspective = 2;
        var perspective = MotionExport.ScreenCorners(ScreenMotionTrack.TargetLayout(target, viewport), rest, 1280, 720, 160, 90, 960, 540);
        Assert.True(Math.Abs(orthographic[0].X - perspective[0].X) > 1);
    }
}
