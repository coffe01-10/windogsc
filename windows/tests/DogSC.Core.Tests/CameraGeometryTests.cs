using System.Text.Json;
using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class CameraGeometryTests
{
    [Fact]
    public void DockedCameraMatchesMacReferenceGeometryAndZoom()
    {
        var style = new CameraBubble { Docked = true, ScaleDuringZoom = .7 };
        var sample = CameraMotionTrack.Resolve(new() { Layout = "horizontal", Size = .29, X = .96, Y = .47 }, 4d / 3, 16d / 9);
        var rest = MotionExport.CameraPixels(sample, 1920, 1080, style);
        Assert.Equal(469.8, rest.W, 5); Assert.Equal(313.2, rest.H, 5);
        Assert.Equal(1392.192, rest.X, 5); Assert.Equal(360.396, rest.Y, 5);
        var zoomed = MotionExport.CameraPixels(sample, 1920, 1080, style, 1.565);
        Assert.Equal(328.86, zoomed.W, 5); Assert.Equal(219.24, zoomed.H, 5);
        Assert.Equal(1527.4944, zoomed.X, 5); Assert.Equal(404.5572, zoomed.Y, 5);
        var full = MotionExport.CameraPixels(sample with { FullscreenProgress = 1 }, 1920, 1080, style, 1.565);
        Assert.Equal(new MotionExport.CameraFrame(0, 0, 1920, 1080, 1), full);
    }

    [Fact]
    public void OversizedDockedCameraCoversTheCornerWithoutBeingShrunk()
    {
        var style = new CameraBubble { Docked = true, ScaleDuringZoom = 2 };
        var sample = CameraMotionTrack.Resolve(new() { Layout = "horizontal", Size = 1, X = 1, Y = 1 }, 4d / 3, 16d / 9);
        var rect = MotionExport.CameraPixels(sample, 1920, 1080, style, 1.565);
        Assert.Equal(1944, rect.W, 5); Assert.Equal(1296, rect.H, 5);
        Assert.Equal(-24, rect.X, 5); Assert.Equal(-216, rect.Y, 5);
        var box = new OverlayBox(rect.X + rect.W / 2, rect.Y + rect.H / 2, rect.W, rect.H);
        Assert.Equal(box, CanvasLayout.FitCamera(box, 1920, 1080, style));
    }

    [Fact]
    public void LegacyWindowsProjectsKeepTheirWidthAndCenterContract()
    {
        var style = JsonSerializer.Deserialize<CameraBubble>("{\"size\":0.2,\"centerX\":0.84,\"centerY\":0.78}", ProjectSchema.Json)!;
        Assert.False(style.Docked); Assert.Equal(1, style.ScaleDuringZoom);
        var rect = CanvasLayout.Bubble(1920, 1080, style);
        Assert.Equal(384, rect.Width); Assert.Equal(1421, rect.X); Assert.Equal(650, rect.Y);
    }

    [Fact]
    public void DockedDragAndResizeRoundTripDuringScreenZoom()
    {
        var style = new CameraBubble { Docked = true, ScaleDuringZoom = .7, Size = .3, CenterX = .8, CenterY = .4, Shape = "horizontal" };
        var sample = CameraMotionTrack.Resolve(new() { Layout = "horizontal", Size = .3, X = .8, Y = .4 }, 4d / 3, 16d / 9);
        var original = MotionExport.CameraPixels(sample, 1920, 1080, style, 1.565);
        var box = new OverlayBox(original.X + original.W / 2 + 20, original.Y + original.H / 2 - 10, original.W, original.H);
        CanvasLayout.WriteCameraBox(style, box, 1920, 1080, 1.565, resize: false);
        Assert.Equal(.3, style.Size);
        var moved = MotionExport.CameraPixels(sample with { Position = new(style.CenterX, style.CenterY) }, 1920, 1080, style, 1.565);
        Assert.Equal(original.X + 20, moved.X, 5); Assert.Equal(original.Y - 10, moved.Y, 5);
        CanvasLayout.WriteCameraBox(style, box with { Width = box.Width * 1.2, Height = box.Height * 1.2 }, 1920, 1080, 1.565, resize: true);
        Assert.Equal(.36, style.Size, 6);
        var tiny = new CameraBubble { Docked = true, Size = .01, CenterX = .9, CenterY = .2 };
        CanvasLayout.WriteCameraBox(tiny, new(320 / 2d, 90, 320, 180), 320, 180, 1, resize: false);
        Assert.Equal(.9, tiny.CenterX); Assert.Equal(.2, tiny.CenterY); Assert.Equal(.01, tiny.Size);
    }

    [Fact]
    public void ImportRetainsCameraZoomAndMacDefaults()
    {
        const string json = """
        {"media":{"screen":{"relativePath":"screen.mp4"}},"camera":{"size":0.01,"scaleDuringZoom":0.4}}
        """;
        var imported = MacProjectImporter.Import(json);
        var camera = imported.Project.Camera;
        Assert.True(camera.Docked); Assert.Equal(.4, camera.ScaleDuringZoom); Assert.Equal(.01, camera.Size);
        Assert.Equal(.96, camera.CenterX); Assert.Equal(.47, camera.CenterY); Assert.True(camera.Mirrored);
        Assert.Equal("square", camera.Shape); Assert.Equal(.25, camera.Corner);
        Assert.DoesNotContain("camera.scaleDuringZoom", imported.DroppedFields);
        var scene = new CameraSceneTrack(imported.Project, 320, 180, 4d / 3).LayoutAt(0);
        Assert.Equal(12, scene.Frame.H, 6);
    }

    [Fact]
    public void MacGlobalCameraHiddenRemainsHiddenAcrossMotionTargets()
    {
        var p = new ProjectDocument { Camera = new() { Docked = true, Hidden = true },
            CameraMotions = [new() { Timing = new() { Duration = 2 }, Target = new() { Layout = "fullscreen", Opacity = 1 } }] };
        Assert.Equal(0, new CameraSceneTrack(p, 320, 180, 4d / 3).At(1, 0, 2).Frame.Opacity);
    }

    [Fact]
    public void CameraDecorationImportKeepsStyleAndFullscreenFadesOnlyTheBorder()
    {
        var imported = MacProjectImporter.Import("""
        {"media":{"screen":{"relativePath":"screen.mp4"}},"camera":{"borderWidth":12,"shadowStrength":0.65}}
        """);
        Assert.DoesNotContain("camera.borderWidth", imported.DroppedFields);
        Assert.DoesNotContain("camera.shadowStrength", imported.DroppedFields);
        var p = imported.Project;
        Assert.Equal(12, p.Camera.BorderWidth); Assert.Equal(.65, p.Camera.ShadowStrength);
        var copy = JsonSerializer.Deserialize<ProjectDocument>(JsonSerializer.Serialize(p, ProjectSchema.Json), ProjectSchema.Json)!;
        Assert.Equal(12, copy.Camera.BorderWidth); Assert.Equal(.65, copy.Camera.ShadowStrength);
        p.CameraMotions = [new() { Timing = new() { StartTime = .5, Duration = 1, LeadInDuration = .5 }, Target = new() { Layout = "fullscreen" } }];
        var track = new CameraSceneTrack(p, 1920, 1080, 4d / 3);
        var rest = track.LayoutAt(0); var full = track.LayoutAt(1);
        Assert.Equal(12, rest.BorderWidth); Assert.Equal(0, full.BorderWidth);
        Assert.Equal(.65, full.ShadowStrength); Assert.Equal(14, full.ShadowRadius); Assert.Equal(8, full.ShadowOffsetY);
        p.Canvas.Opening = "light3D";
        var opening = OpeningMotion.Sample(p, "camera", .1, 1920, 1080);
        var entered = track.At(.1, 0, 2);
        Assert.Equal(track.LayoutAt(.1).BorderWidth * opening.Scale, entered.BorderWidth, 5);
        Assert.Equal(14, entered.ShadowRadius); Assert.Equal(8, entered.ShadowOffsetY);
    }
}
