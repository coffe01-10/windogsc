using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class MediaSwapTests
{
    private static ProjectDocument Project()
    {
        var project = new ProjectDocument();
        project.Assets.Add(new MediaAsset { Id = "a", Video = "screen.mp4", Camera = "cam.mp4", CameraOffset = 0.5 });
        project.Clips.Add(new ClipSegment { AssetId = "a", SourceStart = 0, SourceEnd = 5 });
        return project;
    }

    [Fact]
    public void Replace_Camera_SwapsPath_KeepsClips()
    {
        var project = Project();
        Assert.True(MediaSwap.Replace(project, "a", MediaSwap.Track.Camera, "new-cam.mp4", offset: -0.2));
        var asset = project.Asset("a")!;
        Assert.Equal("new-cam.mp4", asset.Camera);
        Assert.Equal(-0.2, asset.CameraOffset);
        Assert.Equal(5, project.Clips[0].SourceEnd);
    }

    [Fact]
    public void Replace_UnknownAsset_ReturnsFalse()
    {
        var project = Project();
        Assert.False(MediaSwap.Replace(project, "zzz", MediaSwap.Track.Camera, "x.mp4"));
    }

    [Fact]
    public void Clear_Camera_ResetsOffset_VideoRefused()
    {
        var project = Project();
        Assert.True(MediaSwap.Clear(project, "a", MediaSwap.Track.Camera));
        Assert.Null(project.Asset("a")!.Camera);
        Assert.False(MediaSwap.Clear(project, "a", MediaSwap.Track.Video));
        Assert.Equal("screen.mp4", project.Asset("a")!.Video);
    }
}
