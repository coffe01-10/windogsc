using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class CameraSyncTests
{
    [Fact]
    public void Curve_Empty_IsZero()
    {
        Assert.Equal(0, SyncAnchorCurve.Offset([], 5));
    }

    [Fact]
    public void Curve_RampsFromImplicitOrigin_BeforeFirstAnchor()
    {
        var anchors = new List<SyncAnchor> { new() { SourceTime = 4, Offset = 0.2 } };
        Assert.Equal(0.1, SyncAnchorCurve.Offset(anchors, 2), 6);
        Assert.Equal(0.2, SyncAnchorCurve.Offset(anchors, 4), 6);
    }

    [Fact]
    public void Curve_LerpsBetweenAnchors_HoldsAfterLast()
    {
        var anchors = new List<SyncAnchor>
        {
            new() { SourceTime = 2, Offset = 0.1 },
            new() { SourceTime = 6, Offset = 0.5 },
        };
        Assert.Equal(0.3, SyncAnchorCurve.Offset(anchors, 4), 6);
        Assert.Equal(0.5, SyncAnchorCurve.Offset(anchors, 20), 6);
        Assert.Equal(0.1, SyncAnchorCurve.Offset(anchors, 2), 6);
    }

    [Fact]
    public void Curve_UnsortedInput_IsNormalized()
    {
        var anchors = new List<SyncAnchor>
        {
            new() { SourceTime = 6, Offset = 0.5 },
            new() { SourceTime = 2, Offset = 0.1 },
        };
        Assert.Equal(0.3, SyncAnchorCurve.Offset(anchors, 4), 6);
    }

    [Fact]
    public void IndexNear_FindsWithinTolerance()
    {
        var anchors = new List<SyncAnchor> { new() { SourceTime = 3.0, Offset = 0.2 } };
        Assert.Equal(0, SyncAnchorCurve.IndexNear(anchors, 3.015, 0.02));
        Assert.Equal(-1, SyncAnchorCurve.IndexNear(anchors, 3.05, 0.02));
        Assert.Equal(-1, SyncAnchorCurve.IndexNear(anchors, 10, 0.02));
    }

    [Fact]
    public void Export_AnchorOffsetsShiftCameraTrimWindowAndRate()
    {
        // 锚点让末端正偏移 0.4s → 校正窗口 0..4.4 → trim end=4.4、setpts 除数 1.1（源走更快）。
        var anchored = Plan(withAnchor: true);
        var plain = Plan(withAnchor: false);
        Assert.Contains("trim=start=0:end=4", plain.FilterScript, StringComparison.Ordinal);
        Assert.Contains("trim=start=0:end=4.4", anchored.FilterScript, StringComparison.Ordinal);
        Assert.Contains("(PTS-STARTPTS)/1.1", anchored.FilterScript, StringComparison.Ordinal);
    }

    private static ExportPlan Plan(bool withAnchor)
    {
        var asset = new MediaAsset
        {
            Video = "media/screen.mp4",
            Camera = "media/cam.mp4",
            Duration = 10,
            Width = 640,
            Height = 360,
        };
        if (withAnchor)
        {
            asset.CameraSyncAnchors.Add(new SyncAnchor { SourceTime = 2, Offset = 0.4 });
        }

        var project = new ProjectDocument
        {
            Width = 640,
            Height = 360,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = 4 }],
        };
        var media = new ResolvedMedia
        {
            Videos = new Dictionary<string, string> { [asset.Id] = "C:/screen.mp4" },
            Microphones = new Dictionary<string, string>(),
            Systems = new Dictionary<string, string>(),
            Cameras = new Dictionary<string, string> { [asset.Id] = "C:/cam.mp4" },
        };
        return ExportGraph.Build(project, new ExportOptions(), media);
    }
}
