using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class GapRestoreTests
{
    private static ProjectDocument ProjectWithCut()
    {
        var project = new ProjectDocument();
        project.Assets.Add(new MediaAsset { Id = "a", Video = "screen.mp4" });
        // 源素材 10s，剪掉 [3, 6) → 保留 [0,3) + [6,10)
        project.Clips.Add(new ClipSegment { Id = "first", AssetId = "a", SourceStart = 0, SourceEnd = 3 });
        project.Clips.Add(new ClipSegment { Id = "second", AssetId = "a", SourceStart = 6, SourceEnd = 10 });
        return project;
    }

    [Fact]
    public void RestoreGap_InsertsDeletedSource_AndShiftsLaterEffects()
    {
        var project = ProjectWithCut();
        // 输出时间轴：first 占 [0,3)，second 占 [3,7)
        project.Zooms.Add(new ZoomSpan { Start = 4, Duration = 1 });   // 插入点之后 → 平移
        project.ScreenMotions.Add(new ScreenMotionClip { Timing = new TransitionTiming { StartTime = 5, Duration = 1 } });
        project.Canvas.Stickers.Add(new StickerImage { Timing = new EffectWindow { Start = 1, Duration = 4 } }); // 跨插入点 → 拉长
        project.Markers.Add(new SourceMarker { AssetId = "a", SourceTime = 8 }); // 源时间 → 不动

        var restored = GapRestore.RestoreGap(project, "first", "second");

        Assert.True(restored);
        Assert.Equal(3, project.Clips.Count);
        var inserted = project.Clips[1];
        Assert.Equal(3, inserted.SourceStart);
        Assert.Equal(6, inserted.SourceEnd);
        Assert.Equal("a", inserted.AssetId);

        Assert.Equal(7, project.Zooms[0].Start);
        Assert.Equal(8, project.ScreenMotions[0].Timing.StartTime);
        var sticker = project.Canvas.Stickers[0];
        Assert.Equal(1, sticker.Timing.Start);        // 起点不动
        Assert.Equal(7, sticker.Timing.Duration);     // 结束点随插入内容右移
        Assert.Equal(8, project.Markers[0].SourceTime);
        // 时间轴总长 7+3=10
        Assert.Equal(10, TimelineMath.Duration(project));
    }

    [Fact]
    public void RestoreGap_NoSourceGap_ReturnsFalse()
    {
        var project = new ProjectDocument();
        project.Assets.Add(new MediaAsset { Id = "a", Video = "x.mp4" });
        project.Clips.Add(new ClipSegment { Id = "a1", AssetId = "a", SourceStart = 0, SourceEnd = 5 });
        project.Clips.Add(new ClipSegment { Id = "a2", AssetId = "a", SourceStart = 5, SourceEnd = 9 });
        Assert.False(GapRestore.RestoreGap(project, "a1", "a2"));
        Assert.Equal(2, project.Clips.Count);
    }

    [Fact]
    public void RestoreLeading_InsertsAtZero_AndShiftsEverything()
    {
        var project = new ProjectDocument();
        project.Assets.Add(new MediaAsset { Id = "a", Video = "x.mp4" });
        project.Clips.Add(new ClipSegment { AssetId = "a", SourceStart = 2, SourceEnd = 8 });
        project.Zooms.Add(new ZoomSpan { Start = 0.5, Duration = 1 });

        Assert.True(GapRestore.RestoreLeading(project));
        Assert.Equal(0, project.Clips[0].SourceStart);
        Assert.Equal(2, project.Clips[0].SourceEnd);
        Assert.Equal(2.5, project.Zooms[0].Start);
    }

    [Fact]
    public void RestoreTrailing_AppendsToEnd_WithoutShifting()
    {
        var project = ProjectWithCut();
        project.Zooms.Add(new ZoomSpan { Start = 1, Duration = 1 });

        Assert.True(GapRestore.RestoreTrailing(project, sourceDuration: 12));
        var last = project.Clips[^1];
        Assert.Equal(10, last.SourceStart);
        Assert.Equal(12, last.SourceEnd);
        Assert.Equal(1, project.Zooms[0].Start);
    }

    [Fact]
    public void RestorableGap_ReportsSourceSeconds()
    {
        var project = ProjectWithCut();
        Assert.Equal(3, GapRestore.RestorableGap(project, "first"));
        Assert.Equal(0, GapRestore.RestorableGap(project, "second"));
    }
}
