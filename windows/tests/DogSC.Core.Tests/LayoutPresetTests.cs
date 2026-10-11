using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class LayoutPresetTests
{
    static ProjectDocument Project()
    {
        var asset = new MediaAsset { Video = "screen.mp4", Camera = "camera.mp4", Duration = 10 };
        return new() { Width = 320, Height = 180, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 10 }] };
    }

    [Fact]
    public void SavedLayoutSurvivesSettingsReloadAndCreatesAnIndependentPair()
    {
        var root = Path.Combine(Path.GetTempPath(), "DogSC-layout-preset-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.json");
        try
        {
            var p = Project(); p.Camera.Docked = true; p.Camera.Shape = "vertical"; p.Camera.CenterX = .9; p.Camera.Size = .3; p.Camera.Corner = .2;
            p.Canvas.CenterX = .3; p.Canvas.CenterY = .6; p.Canvas.Scale = .8;
            var captured = LayoutPreset.Capture(p, "  Interview  ");
            SettingsFile.Save(path, new() { LayoutPresets = [captured] });
            var reloaded = SettingsFile.Load(path).LayoutPresets.Single();
            Assert.Equal("Interview", reloaded.Name); Assert.True(reloaded.CameraDocked); Assert.True(reloaded.Screen.CanvasPlacement);
            p.Canvas.Scale = 1.6; p.Camera.Shape = "circle";
            var camera = reloaded.Apply(p, 2)!; var screen = p.ScreenMotions.Single();
            Assert.Equal(camera.GroupId, screen.GroupId); Assert.Equal(camera.Timing.StartTime, screen.Timing.StartTime);
            Assert.Equal("vertical", camera.Target.Layout); Assert.Equal(.4, camera.Target.Roundness);
            Assert.Equal(.8, screen.Target.Scale); Assert.Equal(1.6, p.Canvas.Scale); Assert.Equal("circle", p.Camera.Shape);
            screen.Target.X = .8; camera.Target.Size = .9;
            Assert.Equal(.3, reloaded.Screen.X); Assert.Equal(.3, reloaded.Camera.Size);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LayoutScreenScaleIsAbsoluteAndPositionDoesNotDriftOnAnotherBase()
    {
        var target = new ScreenMotionState { CanvasPlacement = true, X = .6, Y = .35, Scale = .8 };
        var viewport = new ScreenAnchorViewport(1000, 600, 1280, 640, BaseCenterX: .8, BaseCenterY: .7, BaseScale: 1.6);
        var clip = new ScreenMotionClip { Timing = new() { StartTime = 1, Duration = 2, LeadInDuration = 1, ReturnDuration = 1, Easing = "linear" }, Target = target };
        var track = new ScreenMotionTrack([clip]); var rest = track.SampleLayout(0, null, viewport);
        var halfway = track.SampleLayout(1.5, null, viewport); var full = track.SampleLayout(2, null, viewport);
        Assert.Equal(.75, halfway.State.Scale, 6); Assert.Equal(.5, full.State.Scale, 6);
        Assert.Equal(-100, halfway.ManualOffset!.Value.X, 6); Assert.Equal(-200, full.ManualOffset!.Value.X, 6);
        Assert.Equal(-210, full.ManualOffset.Value.Y, 6);
        var corners = MotionExport.ScreenCorners(full, rest, 1000, 600, 160, 100, 1280, 640);
        var map = MotionExport.Homography.FromQuad([(0, 0), (1000, 0), (0, 600), (1000, 600)], corners);
        var center = map.Apply(800, 420);
        Assert.Equal(600, center.X, 5); Assert.Equal(210, center.Y, 5);
        Assert.Equal(.8, target.Scale); Assert.True(target.CanvasPlacement);
        var returned = track.SampleLayout(4, null, viewport);
        Assert.Equal(1, returned.State.Scale); Assert.Equal(rest.ManualOffset, returned.ManualOffset);
        // Ordinary focus targets keep their previous relative semantics.
        Assert.Equal(.8, ScreenMotionTrack.TargetLayout(new() { Scale = .8 }, viewport).State.Scale);
    }

    [Theory]
    [InlineData(false, true, .3, "horizontal", 1.3333333333333333)]
    [InlineData(true, false, .3, "horizontal", 1.3333333333333333)]
    [InlineData(true, false, .01, "circle", 1.3333333333333333)]
    [InlineData(true, false, 1, "original", 3)]
    public void CameraLayoutAdaptsBetweenLegacyAndDockedCoordinates(bool sourceDocked, bool targetDocked, double size, string shape, double aspect)
    {
        var source = Project(); source.Camera = new() { Docked = sourceDocked, Shape = shape, Size = size, CenterX = .8, CenterY = .4 };
        var preset = LayoutPreset.Capture(source, "Layout"); var destination = Project(); destination.Camera.Docked = targetDocked;
        var clip = preset.Apply(destination, 2)!; ProjectFile.Normalize(destination);
        var expected = MotionExport.CameraPixels(CameraMotionTrack.Resolve(preset.Camera, aspect, 16d / 9), 320, 180, source.Camera);
        var actual = new CameraSceneTrack(destination, 320, 180, aspect).LayoutAt(clip.Timing.EndTime).Frame;
        Assert.Equal(expected.X, actual.X, 5); Assert.Equal(expected.Y, actual.Y, 5);
        Assert.Equal(expected.W, actual.W, 5); Assert.Equal(expected.H, actual.H, 5);
        Assert.Equal(targetDocked, destination.Camera.Docked);
    }
}
