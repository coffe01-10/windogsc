using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class CursorMotionTests
{
    [Theory]
    [InlineData("system.automatic")]
    [InlineData("system.arrow")]
    public void TargetWindowVisibilityAlsoAppliesToAuthoredCursorStyles(string asset)
    {
        var project = Sample();
        project.Canvas.Cursor.Asset = asset;
        project.Assets[0].Cursor = [new() { Time = 0, X = .25, Y = .25, Cursor = "system.arrow" },
            new() { Time = 1, X = .25, Y = .25, Cursor = "system.hidden" },
            new() { Time = 2, X = .25, Y = .25, Cursor = "system.arrow" }];
        var timeline = new CursorTimeline(project);
        Assert.NotNull(timeline.At(.5).Pointer);
        Assert.Null(timeline.At(1.5).Pointer);
        Assert.NotNull(timeline.At(2.5).Pointer);
        // Invisible cursors can still drive a deliberately authored focus effect.
        Assert.NotNull(timeline.PositionAt(1.5));
    }

    [Fact]
    public void SpringResumesSmoothlyAfterAStationaryInterval()
    {
        var knots = Enumerable.Range(0, 11)
            .Select(i => new PointerTrack.Knot(2 + i * .05, .2 + i * .05, .5, false, null))
            .Prepend(new(0, .2, .5, false, null)).ToArray();
        var track = new PointerTrack.Track(knots, "smooth");
        var raw = new PointerTrack.Track(knots, "none");
        Assert.True(track.PositionAt(2.3).X < raw.PositionAt(2.3).X - .02);
    }

    [Theory]
    [InlineData("medium")]
    [InlineData("rapid")]
    public void HermiteMotionDoesNotReverseBeforeItsLastSample(string motion)
    {
        var track = new PointerTrack.Track([
            new(0, .1, .5, false, null), new(.1, .4, .5, false, null),
            new(.2, .7, .5, false, null)], motion);
        double previous = .1;
        for (var i = 0; i <= 200; i++)
        {
            var x = track.PositionAt(i * .001).X;
            Assert.InRange(x, previous - 1e-9, .7 + 1e-9);
            previous = x;
        }
    }

    [Fact]
    public void TrimmedClipStillSamplesTheRecordedCursorAcrossItsBoundary()
    {
        var project = Sample();
        project.Canvas.Cursor.Motion = "none";
        project.Clips[0].SourceStart = .02;
        project.Clips[0].SourceEnd = .04;
        project.Assets[0].Cursor = [new() { Time = 0, X = .1, Y = .5 },
            new() { Time = .1, X = .6, Y = .5 }];
        var sample = new CursorTimeline(project).At(.01);
        Assert.NotNull(sample.Pointer);
        Assert.Equal(.25, sample.Pointer.Value.X, 6);
    }

    [Fact]
    public void ClickColorUsesBgraChannelOrder()
    {
        var pixels = CursorCatalog.RenderClick(80, "burst", .1,
            CursorCatalog.ParseColor("#FF0000"), 1, 50);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0) continue;
            Assert.Equal(0, pixels[i]);
            Assert.Equal(0, pixels[i + 1]);
            Assert.Equal(255, pixels[i + 2]);
        }
    }

    static ProjectDocument Sample() => new()
    {
        Width = 640, Height = 360,
        Assets = [new() { Id = "a", Video = "screen.mp4", Duration = 10, Width = 640, Height = 360 }],
        Clips = [new() { Id = "c", AssetId = "a", SourceEnd = 10 }]
    };

    [Fact]
    public void SmoothSpringSettlesAndAnchorsClickPosition()
    {
        var project = Sample();
        project.Canvas.Cursor.Motion = "smooth";
        project.Assets[0].Cursor =
        [
            new() { Time = 0, X = .1, Y = .2 },
            new() { Time = .4, X = .9, Y = .8 },
            new() { Time = 2, X = .6, Y = .5, Click = true },
            new() { Time = 4, X = .6, Y = .5 },
        ];
        var track = new CursorTimeline(project);
        // 弹簧模拟收敛到目标点。
        Assert.Equal(.6, track.At(4.5).Pointer!.Value.X, 2);
        // 点击锚定：点击瞬间位置精确落在点击处。
        var anchored = track.At(2).Pointer!.Value;
        Assert.Equal(.6, anchored.X, 6);
        Assert.Equal(.5, anchored.Y, 6);
    }

    [Fact]
    public void MotionTiltRequiresMotionAndStrength()
    {
        var project = Sample();
        project.Canvas.Cursor.Motion = "none";
        project.Canvas.Cursor.MotionTilt = 2;
        // 80ms 步长的密集采样（模拟录制器）沿对角线移动，再停下。
        for (var i = 0; i <= 10; i++)
        {
            project.Assets[0].Cursor.Add(new() { Time = i * .08, X = .1 + .08 * i, Y = .9 - .08 * i });
        }
        project.Assets[0].Cursor.Add(new() { Time = 5, X = .9, Y = .1 });
        var track = new CursorTimeline(project);
        Assert.Equal(0, track.At(.25).Rotation);
        project.Canvas.Cursor.Motion = "rapid";
        var fast = new CursorTimeline(project);
        Assert.NotEqual(0, fast.At(.25).Rotation);
        project.Canvas.Cursor.MotionTilt = 0;
        Assert.Equal(0, new CursorTimeline(project).At(.25).Rotation);
    }

    [Fact]
    public void ClickStylesUseTheirOwnDurationsAndCanBeDisabled()
    {
        var project = Sample();
        project.Canvas.Cursor.ClickStyle = "burst";
        project.Assets[0].Cursor = [new() { Time = 1, X = .5, Y = .5, Click = true }];
        var track = new CursorTimeline(project);
        Assert.Equal("burst", track.At(1.1).ClickStyle);
        Assert.NotNull(track.At(1.15).Click);
        Assert.Null(track.At(1.25).Click);
        project.Canvas.Cursor.ClickStyle = "none";
        Assert.Null(new CursorTimeline(project).At(1.05).Click);
        // 旧字段兼容：ClickRipple=false 也等价于 none。
        project.Canvas.Cursor.ClickStyle = null;
        project.Canvas.Cursor.ClickRipple = false;
        Assert.Null(new CursorTimeline(project).At(1.05).Click);
    }

    [Fact]
    public void AutomaticAssetUsesRecordedCursorAndFallsBackToArrow()
    {
        var project = Sample();
        project.Canvas.Cursor.Asset = "system.automatic";
        project.Assets[0].Cursor =
        [
            new() { Time = 0, X = .2, Y = .2, Cursor = "system.ibeam" },
            new() { Time = .05, X = .4, Y = .4 },
        ];
        var track = new CursorTimeline(project);
        Assert.Equal("system.ibeam", track.At(0).Asset);
        Assert.Equal("system.arrow", track.At(3).Asset);
    }

    [Fact]
    public void MacImportMapsFullCursorStyle()
    {
        var json = """
        {"version":27,"cursorStyle":{"assetID":"system.pointing-hand","size":1.5,
        "hideWhenIdle":true,"idleDelay":2.5,"clickEffectStyle":"pulse","clickColor":"#FF0000",
        "clickOpacity":0.6,"clickScale":1.2,"motionSmoothing":"rapid","motionTiltStrength":3.5},
        "canvas":{},"timeline":{"clips":[]}}
        """;
        var project = new ProjectDocument();
        var dropped = new List<string>();
        var method = typeof(MacProjectImporter).GetMethod("MapCursor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        method!.Invoke(null, [doc.RootElement, project, dropped]);
        Assert.Equal("system.pointing-hand", project.Canvas.Cursor.Asset);
        Assert.Equal("pulse", project.Canvas.Cursor.ClickStyle);
        Assert.Equal("rapid", project.Canvas.Cursor.Motion);
        Assert.Equal(3.5, project.Canvas.Cursor.MotionTilt);
        Assert.Equal("#FF0000", project.Canvas.Cursor.ClickColor);
    }

    [Fact]
    public void PointerJsonlImportsCursorAssetId()
    {
        var points = MacProjectImporter.ImportPointerEvents(
            ["{\"time\":1.0,\"kind\":\"move\",\"location\":{\"x\":0.5,\"y\":0.5},\"cursorAssetID\":\"system.ibeam\"}"], 0);
        Assert.Single(points);
        Assert.Equal("system.ibeam", points[0].Cursor);
    }

    [Fact]
    public void ClickLayerRendersAlphaOnlyInsideFootprint()
    {
        var size = 120;
        var frame = CursorCatalog.RenderClick(size, "ripple", .3, 0x292929, 1, 60);
        Assert.Contains(frame.Skip(3).Where((_, i) => i % 4 == 0), a => a > 0);
        Assert.Equal(0, frame[3]); // 角落透明
        var burst = CursorCatalog.RenderClick(size, "burst", .5, 0x292929, 1, 60);
        Assert.Contains(burst.Skip(3).Where((_, i) => i % 4 == 0), a => a > 0);
    }
}
