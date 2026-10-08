using Xunit;

namespace DogSC.Core.Tests;

public class EffectTransferTests
{
    [Fact]
    public void CopyZoom_IsASnapshotWithAnIndependentIdentity()
    {
        var source = new ZoomSpan { Start = 1, Duration = 2, Scale = 2.4, FocusX = 0.2, FocusY = 0.8 };
        var copy = TimelineEdits.CopyZoom(source);
        source.Scale = 3;
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal(2.4, copy.Scale);
        Assert.Equal(0.2, copy.FocusX);
        Assert.Equal(0.8, copy.FocusY);
        Assert.Equal(2, copy.Duration);
    }

    [Theory]
    [InlineData(3.04, 3)] // 起点接上前一个缩放的终点。
    [InlineData(6.96, 7)] // 终点接上下一个缩放的起点。
    public void PasteZoom_SnapsEitherEdgeAndKeepsTheFullDuration(double time, double expected)
    {
        var project = Project();
        var source = new ZoomSpan { Start = 1, Duration = 2, Scale = 2.2, FocusX = 0.3 };
        project.Zooms = [source, new ZoomSpan { Start = 9, Duration = 1 }];
        var pasted = TimelineEdits.PasteZoom(project, source, time, 0.1, out var error);
        Assert.Null(error);
        Assert.NotNull(pasted);
        Assert.Equal(expected, pasted.Start);
        Assert.Equal(2, pasted.Duration);
        Assert.Equal(2.2, pasted.Scale);
        Assert.Equal(0.3, pasted.FocusX);
        Assert.NotEqual(source.Id, pasted.Id);
    }

    [Theory]
    [InlineData(2)] // 与已有缩放重叠。
    [InlineData(9)] // 片尾容不下完整副本。
    public void FailedPasteZoom_DoesNotChangeTheTimeline(double time)
    {
        var project = Project();
        var source = new ZoomSpan { Start = 1, Duration = 2 };
        project.Zooms.Add(source);
        Assert.Null(TimelineEdits.PasteZoom(project, source, time, 0, out var error));
        Assert.NotNull(error);
        Assert.Same(source, Assert.Single(project.Zooms));
        Assert.Equal(1, source.Start);
        Assert.Equal(2, source.Duration);
    }

    [Fact]
    public void PasteZoomAttributes_PreservesIdentityPlacementAndOtherSettings()
    {
        var project = Project();
        var target = new ZoomSpan { Start = 5, Duration = 3 };
        var id = target.Id;
        project.Zooms.Add(target);
        var source = new ZoomSpan { Start = 1, Duration = 1, Scale = 2.8, FocusX = 0.1, FocusY = 0.9 };
        Assert.True(TimelineEdits.PasteZoomAttributes(project, id, source));
        Assert.Equal(id, target.Id);
        Assert.Equal(5, target.Start);
        Assert.Equal(3, target.Duration);
        Assert.Equal(2.8, target.Scale);
        Assert.Equal(0.1, target.FocusX);
        Assert.Equal(0.9, target.FocusY);
        Assert.Equal(1, project.Clips[0].Speed);
        Assert.Equal("#F3F1EC", project.Canvas.Background);
    }

    [Fact]
    public void CopyEffect_ClipsZoomsToTheCopiedMaterial()
    {
        var project = Project();
        TimelineEdits.TrySplit(project, 3);
        TimelineEdits.TrySplit(project, 6);
        project.Zooms = [new ZoomSpan { Start = 2, Duration = 5 }];
        var bundle = TimelineEdits.CopyEffect(project, project.Clips[1].Id)!;
        var copied = Assert.Single(bundle.Zooms);
        Assert.Equal(0, copied.Start);
        Assert.Equal(3, copied.Duration);
        Assert.Equal(5, project.Zooms[0].Duration);
    }

    [Theory]
    [InlineData(0.5, 4)]
    [InlineData(2, 1)]
    public void PasteEffect_RipplesLaterZoomsWithTheTargetMaterial(double speed, double laterStart)
    {
        var project = Project();
        TimelineEdits.TrySplit(project, 2);
        var later = new ZoomSpan { Start = 2, Duration = 2 };
        project.Zooms.Add(later);
        var bundle = new EffectBundle { Speed = speed, SourceLength = 2 };
        Assert.True(TimelineEdits.PasteEffect(project, project.Clips[0].Id, bundle, true));
        Assert.Equal(laterStart, later.Start);
        Assert.Equal(2, later.Duration);
        Assert.Same(later, Assert.Single(project.Zooms));
    }

    [Fact]
    public void PasteAttributes_PreservesZoomPartsOutsideTheTargetAndClipsNewEffects()
    {
        var project = Project();
        TimelineEdits.TrySplit(project, 3);
        TimelineEdits.TrySplit(project, 5);
        project.Zooms.Add(new ZoomSpan { Start = 2, Duration = 5, Scale = 1.8 });
        var bundle = new EffectBundle
        {
            Zooms = [new ZoomSpan { Start = 0.5, Duration = 4, Scale = 2.5 }],
        };
        Assert.True(TimelineEdits.PasteEffect(project, project.Clips[1].Id, bundle, false));
        Assert.Collection(project.Zooms,
            before => { Assert.Equal(2, before.Start); Assert.Equal(1, before.Duration); Assert.Equal(1.8, before.Scale); },
            inserted => { Assert.Equal(3.5, inserted.Start); Assert.Equal(1.5, inserted.Duration); Assert.Equal(2.5, inserted.Scale); },
            after => { Assert.Equal(5, after.Start); Assert.Equal(2, after.Duration); Assert.Equal(1.8, after.Scale); });
        Assert.Equal(3, project.Zooms.Select(zoom => zoom.Id).Distinct().Count());
        Assert.Equal(2, TimelineMath.ClipDuration(project.Clips[1]));
    }

    static ProjectDocument Project()
    {
        var asset = new MediaAsset { Duration = 10, Width = 640, Height = 360, Video = "media/screen.mp4" };
        return new ProjectDocument
        {
            Width = 640, Height = 360,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceEnd = 10 }],
        };
    }
}
