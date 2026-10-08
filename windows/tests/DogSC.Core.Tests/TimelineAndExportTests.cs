using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class TimelineAndExportTests
{
    [Fact]
    public void OutputSize_UsesShortEdgePresets()
    {
        Assert.Equal((1920, 1080), CanvasLayout.OutputSize(1280, 720, "16:9", "1080"));
        Assert.Equal((3840, 2160), CanvasLayout.OutputSize(1920, 1080, "16:9", "4k"));
        Assert.Equal((1080, 1920), CanvasLayout.OutputSize(1920, 1080, "9:16", "1080"));
        Assert.Equal((640, 360), CanvasLayout.OutputSize(640, 360, "source", "source"));
    }

    [Fact]
    public void Placement_CentersAPaddedFrame()
    {
        var placed = CanvasLayout.Place(1920, 1080, 1920, 1080, new CanvasSettings
        {
            Padding = 0,
            Scale = 1,
            CenterX = 0.5,
            CenterY = 0.5,
        });
        Assert.Equal(1920, placed.Width);
        Assert.Equal(1080, placed.Height);
        Assert.Equal(0, placed.X);
        Assert.Equal(0, placed.Y);
    }

    [Fact]
    public void SplitAndSpeed_KeepAContinuousTimeline()
    {
        var project = Sample(10);
        Assert.True(TimelineEdits.TrySplit(project, 4));
        Assert.Equal(2, project.Clips.Count);
        Assert.Equal(10d, TimelineMath.Duration(project), 2);
        Assert.True(TimelineEdits.TrySetSpeed(project, project.Clips[1].Id, 2));
        Assert.Equal(7d, TimelineMath.Duration(project), 2);
    }

    [Fact]
    public void DeleteClip_ShiftsLaterZooms()
    {
        var project = Sample(10);
        Assert.True(TimelineEdits.TrySplit(project, 4));
        project.Zooms.Add(new ZoomSpan { Start = 6, Duration = 2 });
        Assert.True(TimelineEdits.TryDeleteClip(project, project.Clips[0].Id));
        Assert.Equal(2d, project.Zooms[0].Start, 2);
        Assert.Equal(6d, TimelineMath.Duration(project), 2);
    }

    [Fact]
    public void Marker_StaysOnTheSourceFrameAfterAHeadTrim()
    {
        var project = Sample(10);
        var marker = TimelineEdits.AddMarker(project, 5, "口误");
        Assert.NotNull(marker);
        Assert.True(TimelineEdits.TryTrim(project, project.Clips[0].Id, 2, 10));
        Assert.Equal(3d, TimelineMath.MarkerTime(project, marker!)!.Value, 2);
    }

    [Fact]
    public void Zoom_DoesNotOverlap()
    {
        var project = Sample(8);
        var first = TimelineEdits.AddZoom(project, 1);
        Assert.NotNull(first);
        Assert.Null(TimelineEdits.AddZoom(project, 1.4));
        var second = TimelineEdits.AddZoom(project, 4);
        Assert.NotNull(second);
    }

    [Fact]
    public void Export_IncludesConcatCropAndCanvasSize()
    {
        var project = Sample(6);
        project.Width = 640;
        project.Height = 360;
        project.Canvas.Aspect = "16:9";
        project.Canvas.Padding = 40;
        project.Canvas.Corner = 24;
        project.Canvas.Shadow = 30;
        Assert.True(TimelineEdits.TrySplit(project, 3));
        TimelineEdits.AddZoom(project, 0.2);
        var plan = ExportGraph.Build(project, new ExportOptions { Resolution = "1080", Fps = 30 }, Media(project));
        Assert.Equal((1920, 1080), (plan.Width, plan.Height));
        Assert.Contains("concat=n=", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("crop=w=", plan.FilterScript, StringComparison.Ordinal);
        Assert.True(plan.FrameMaskIndex >= 0);
        Assert.Equal("vout", plan.VideoLabel);
        Assert.Null(plan.AudioLabel);
    }

    [Fact]
    public void Export_MixesMicrophoneAndSystemAudio()
    {
        var project = Sample(4);
        project.Canvas.Corner = 0;
        project.Canvas.Shadow = 0;
        project.Assets[0].Microphone = "media/mic.wav";
        project.Assets[0].SystemAudio = "media/system.wav";
        var media = new ResolvedMedia
        {
            Videos = new Dictionary<string, string> { [project.Assets[0].Id] = "C:/video.mp4" },
            Microphones = new Dictionary<string, string> { [project.Assets[0].Id] = "C:/mic.wav" },
            Systems = new Dictionary<string, string> { [project.Assets[0].Id] = "C:/system.wav" },
            Cameras = new Dictionary<string, string>(),
        };
        var plan = ExportGraph.Build(project, new ExportOptions(), media);
        Assert.Contains("amix=inputs=2", plan.FilterScript, StringComparison.Ordinal);
        Assert.Equal("a0", plan.AudioLabel);
        Assert.Equal(3, plan.Inputs.Count);
    }

    [Fact]
    public void Mask_KeepsTheCenterAndClearsTheCorner()
    {
        var pixels = MaskImage.CreateBgra(32, 32, 8, circle: false);
        Assert.Equal(255, pixels[((16 * 32) + 16) * 4 + 3]);
        Assert.Equal(0, pixels[3]);
        var circle = MaskImage.CreateBgra(32, 32, 0, circle: true);
        Assert.Equal(0, circle[3]);
        Assert.Equal(255, circle[((16 * 32) + 16) * 4 + 3]);
    }

    [Fact]
    public void ProjectRoundTrip_RejectsMacPackages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dogsc-test-" + Guid.NewGuid().ToString("N"));
        ProjectFile.Save(directory, Sample(3));
        var loaded = ProjectFile.Load(directory);
        Assert.Equal("演示", loaded.Name);
        loaded.Platform = "macos";
        ProjectFile.Save(directory, loaded);
        var error = Assert.Throws<InvalidDataException>(() => ProjectFile.Load(directory));
        Assert.Contains("macOS", error.Message, StringComparison.Ordinal);
        Directory.Delete(directory, recursive: true);
    }

    private static ProjectDocument Sample(double seconds)
    {
        var asset = new MediaAsset
        {
            Video = "media/screen.mp4",
            Duration = seconds,
            Width = 640,
            Height = 360,
        };
        return new ProjectDocument
        {
            Name = "演示",
            Width = 640,
            Height = 360,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = seconds }],
        };
    }

    private static ResolvedMedia Media(ProjectDocument project) => new()
    {
        Videos = project.Assets.ToDictionary(asset => asset.Id, _ => "C:/screen.mp4"),
        Microphones = new Dictionary<string, string>(),
        Systems = new Dictionary<string, string>(),
        Cameras = new Dictionary<string, string>(),
    };
}
