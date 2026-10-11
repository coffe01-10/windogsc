using System.Text.Json;
using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class LayoutMotionTests
{
    static ProjectDocument Project()
    {
        var asset = new MediaAsset { Video = "screen.mp4", Camera = "camera.mp4", Duration = 10 };
        return new() { Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 10 }] };
    }

    [Fact]
    public void ImportedPairSurvivesSaveAndEditingEitherTrack()
    {
        const string json = """
        {"media":{"screen":{"relativePath":"screen.mp4"}},"timeline":{"sourceSequence":{"kind":"edited","segments":[{"sourceStart":0,"sourceDuration":10}]},
          "screenMotionClips":[{"id":"screen","groupID":"layout-a","timing":{"startTime":1,"duration":2,"leadInDuration":0.5,"returnDuration":0.5},"target":{"scale":0.8}}],
          "cameraMotionClips":[{"id":"camera","groupID":"layout-a","timing":{"startTime":1,"duration":2,"leadInDuration":0.5,"returnDuration":0.5},"target":{"layout":{"kind":"fullscreen"},"size":1}}]}}
        """;
        var imported = MacProjectImporter.Import(json).Project;
        Assert.Equal("layout-a", imported.ScreenMotions[0].GroupId);
        Assert.Equal("layout-a", imported.CameraMotions[0].GroupId);
        var p = JsonSerializer.Deserialize<ProjectDocument>(JsonSerializer.Serialize(imported, ProjectSchema.Json), ProjectSchema.Json)!;
        Assert.True(MotionTimingEdits.SetRange(p, "screen", 2, 3));
        Assert.Equal(2, p.CameraMotions[0].Timing.StartTime); Assert.Equal(3, p.CameraMotions[0].Timing.Duration);
        Assert.True(MotionTimingEdits.SetRange(p, "camera", 3, 2));
        Assert.Equal(3, p.ScreenMotions[0].Timing.StartTime); Assert.Equal(2, p.ScreenMotions[0].Timing.Duration);
        Assert.Equal(.8, p.ScreenMotions[0].Target.Scale);
        Assert.Equal("fullscreen", p.CameraMotions[0].Target.Layout);
        Assert.Equal(.5, p.ScreenMotions[0].Timing.ReturnDuration);
    }

    [Fact]
    public void LinkedResizeKeepsEntryPhaseAndStopsPartnerAtItsNeighbour()
    {
        var p = Project();
        p.ScreenMotions = [new() { Id = "screen", GroupId = "pair", Timing = new() { StartTime = 2, Duration = 2 } }];
        p.CameraMotions = [new() { Id = "camera", GroupId = "pair", Timing = new() { StartTime = 2, Duration = 2, LeadInDuration = 1, LeadInProgressOffset = .25 } },
            new() { Id = "next", Timing = new() { StartTime = 6, Duration = 2 } }];
        MotionTimingEdits.SetRange(p, "screen", 3, 4);
        var camera = p.CameraMotions[0].Timing;
        Assert.Equal(3, camera.StartTime); Assert.Equal(6, camera.EndTime);
        Assert.Equal(.25, camera.LeadInProgressOffset); Assert.Equal(1, camera.LeadInDuration);
        Assert.Equal(6, p.CameraMotions[1].Timing.StartTime);
        p.CameraMotions[0].GroupId = null;
        MotionTimingEdits.SetRange(p, "screen", 1, 2);
        Assert.Equal(3, camera.StartTime); Assert.Equal(6, camera.EndTime);
    }

    [Fact]
    public void PairCreationUsesBothTrackNeighboursAndOneSharedWindow()
    {
        var p = Project();
        p.ScreenMotions = [new() { Timing = new() { StartTime = 3, Duration = 1 } }];
        var camera = LayoutMotionEdits.Apply(p, 2.7, new() { Layout = "fullscreen" }, new() { Scale = .6 })!;
        var screen = p.ScreenMotions.Single(c => c.GroupId == camera.GroupId);
        Assert.NotNull(camera.GroupId);
        Assert.Equal(camera.Timing.StartTime, screen.Timing.StartTime);
        Assert.Equal(camera.Timing.EndTime, screen.Timing.EndTime);
        Assert.True(camera.Timing.EndTime <= 3);
        Assert.Equal(0, camera.Timing.ReturnDuration);
        Assert.Equal(2, p.ScreenMotions.Count);
        Assert.Single(p.CameraMotions);
        var updated = LayoutMotionEdits.Apply(p, camera.Timing.StartTime + .1, new() { Layout = "horizontal" }, new() { Scale = .9 });
        Assert.Same(camera, updated); Assert.Equal(2, p.ScreenMotions.Count);
        Assert.Equal(.9, screen.Target.Scale);
    }

    [Fact]
    public void ExistingScreenCanBePairedWithoutInsertingPastThePlayhead()
    {
        var p = Project();
        p.ScreenMotions = [new() { Timing = new() { StartTime = 1, Duration = 3 } }];
        var camera = LayoutMotionEdits.Apply(p, 2, new(), new())!;
        Assert.Equal(1, camera.Timing.StartTime); Assert.Equal(3, camera.Timing.Duration);
        Assert.Equal(camera.GroupId, p.ScreenMotions[0].GroupId);
        p.CameraMotions[0].Timing.StartTime = 2; p.CameraMotions[0].Timing.Duration = 4;
        LayoutMotionEdits.Apply(p, 3, new(), new());
        Assert.Equal(2, camera.Timing.StartTime); Assert.Equal(4, camera.Timing.EndTime);
        Assert.Equal(camera.Timing.StartTime, p.ScreenMotions[0].Timing.StartTime);
        Assert.Equal(camera.Timing.EndTime, p.ScreenMotions[0].Timing.EndTime);
    }
}
