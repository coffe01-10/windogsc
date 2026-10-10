using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class OpeningMotionTests
{
    static ProjectDocument Project()
    {
        var asset = new MediaAsset { Video = "screen.mp4", Camera = "camera.mp4", Duration = 3, Width = 640, Height = 360 };
        return new ProjectDocument { Width = 640, Height = 360, Assets = [asset],
            Clips = [new() { AssetId = asset.Id, SourceEnd = 3 }], Canvas = new() { Opening = "light3D", OpeningDuration = 2,
                OpeningInterval = .4, Stickers = [new() { Path = "sticker.png", Timing = new() { Start = 0, Duration = 3 } }] } };
    }

    [Fact]
    public void OrderAndExcludedElementsChangeEntryTimes()
    {
        var project = Project(); project.Canvas.OpeningOrder = ["stickers", "camera", "screen"];
        Assert.True(OpeningMotion.Sample(project, "stickers", .2, 640, 360).Opacity > 0);
        Assert.Equal(0, OpeningMotion.Sample(project, "screen", .2, 640, 360).Opacity);
        project.Canvas.OpeningIncluded.Remove("screen");
        Assert.Equal(OpeningPose.Identity, OpeningMotion.Sample(project, "screen", .2, 640, 360));
    }

    [Fact]
    public void AbsentCameraDoesNotDelayStickersAndAllElementsSettleByEnd()
    {
        var project = Project(); project.Assets[0].Camera = null;
        Assert.Equal(new[] { "screen", "stickers" }, OpeningMotion.ActiveElements(project));
        Assert.True(OpeningMotion.Sample(project, "stickers", .5, 640, 360).Opacity > 0);
        Assert.Equal(OpeningPose.Identity, OpeningMotion.Sample(project, "stickers", 2, 640, 360));
    }

    [Fact]
    public void Light3DIncludesTiltAndMacImportPreservesChoreography()
    {
        var pose = OpeningMotion.Sample(Project(), "screen", .3, 640, 360);
        Assert.True(pose.RotationX > 0); Assert.True(pose.RotationY < 0); Assert.True(pose.Perspective > 0);
        var imported = MacProjectImporter.Import("""
            {"media":{"screen":{"relativePath":"screen.mp4"}},"openingSequence":{"isEnabled":true,"preset":"侧滑","duration":3,"stagger":0.3,
              "elementOrder":["摄像头","屏幕","贴图"],"includedElements":["屏幕","贴图"]}}
            """);
        Assert.Equal("sideSlide", imported.Project.Canvas.Opening);
        Assert.Equal(new[] { "camera", "screen", "stickers" }, imported.Project.Canvas.OpeningOrder);
        Assert.Equal(new[] { "screen", "stickers" }, imported.Project.Canvas.OpeningIncluded);
        Assert.DoesNotContain("openingSequence", imported.DroppedFields);
    }
}
