using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class ParityCompletionTests
{
    static ProjectDocument Sample() => new()
    {
        Width = 640, Height = 360,
        Assets = [new() { Id = "a", Video = "screen.mp4", Duration = 10, Width = 640, Height = 360 }],
        Clips = [new() { Id = "c", AssetId = "a", SourceEnd = 10 }]
    };

    [Fact]
    public void CursorInterpolatesContinuousMovesWithoutMovingDuringLongIdleGaps()
    {
        var project = Sample();
        project.Canvas.Cursor.Motion = "none";
        project.Assets[0].Cursor = [new() { Time = 0, X = .1, Y = .2 }, new() { Time = .1, X = .5, Y = .4 }, new() { Time = 5, X = .9, Y = .8 }];
        var track = new CursorTimeline(project);
        Assert.Equal(.3, track.At(.05).Pointer!.Value.X, 6);
        Assert.Equal(.5, track.At(3).Pointer!.Value.X, 6);
        project.Canvas.Cursor.IdleHide = true;
        project.Canvas.Cursor.IdleDelay = 1;
        Assert.Null(track.At(3).Pointer);
    }

    [Fact]
    public void CursorTimingUsesSourceClockAndHiddenAlsoHidesClicks()
    {
        var project = Sample();
        project.Clips[0].SourceStart = 1;
        project.Clips[0].Speed = 4;
        project.Canvas.Cursor.TimingAdvance = .2;
        project.Assets[0].Cursor = [new() { Time = 2, X = .4, Y = .6, Click = true }];
        var track = new CursorTimeline(project);
        Assert.Null(track.At(.19).Click);
        Assert.NotNull(track.At(.21).Click);
        project.Canvas.Cursor.Mode = "hidden";
        Assert.Equal(default, track.At(.21));
    }

    [Fact]
    public void CursorCoordinatesFollowCropAndZoom()
    {
        var project = Sample();
        project.Canvas.Crop = new() { X = .25, Width = .5 };
        project.Zooms = [new() { Start = 0, Duration = 3, Scale = 2, FocusX = .5, FocusY = .5 }];
        var point = new CursorTimeline(project).InFrame((.375, .5), 1);
        Assert.Equal(0, point.X, 6);
        Assert.Equal(.5, point.Y, 6);
    }

    [Fact]
    public void CropRoundsInsideSourceAndCustomRatiosPreserveShape()
    {
        var crop = CanvasLayout.Crop(641, 361, new() { X = .333, Y = .2, Width = .667, Height = .8 });
        Assert.True(crop.X >= 0 && crop.X + crop.Width <= 641);
        Assert.True(crop.Y >= 0 && crop.Y + crop.Height <= 361);
        Assert.Equal(0, crop.Width % 2);
        var (w, h) = CanvasLayout.OutputSize(1920, 1080, "2.39:1", "1080");
        Assert.InRange(w / (double)h, 2.38, 2.4);
    }

    [Fact]
    public void TimedDecorationsFadeAndFollowRippleTrimAndSpeed()
    {
        var project = Sample();
        project.Canvas.Sticker = new() { Path = "logo.png", Timing = new() { Start = 4, Duration = 4, Fade = 1 } };
        project.Canvas.SoftRegion = new() { Enabled = true, Timing = new() { Start = 1, Duration = 1 } };
        Assert.Equal(.5, EffectTiming.OpacityAt(project.Canvas.Sticker.Timing, 4.5), 6);
        Assert.Equal(0, EffectTiming.OpacityAt(project.Canvas.Sticker.Timing, 8));
        Assert.True(TimelineEdits.TryTrim(project, "c", 2, 10));
        Assert.False(project.Canvas.SoftRegion.Enabled);
        Assert.Equal(2, project.Canvas.Sticker.Timing.Start);
        Assert.True(TimelineEdits.TrySetSpeed(project, "c", 4));
        Assert.Equal(.5, project.Canvas.Sticker.Timing.Start);
        Assert.Equal(1, project.Canvas.Sticker.Timing.Duration);
        Assert.Equal(1, EffectTiming.OpacityAt(new(), 100));
    }

    [Fact]
    public void PresetIsIndependentAndDoesNotReplaceMediaOrTimeline()
    {
        var project = Sample();
        project.Canvas.Cursor.ClickColor = "#FF0000";
        project.Camera.Mirrored = true;
        var preset = ScenePreset.Capture(project, "竖屏");
        project.Canvas.Cursor.ClickColor = "#000000";
        project.Camera.Mirrored = false;
        preset.Apply(project);
        Assert.Equal("#FF0000", project.Canvas.Cursor.ClickColor);
        Assert.True(project.Camera.Mirrored);
        Assert.Single(project.Clips);
        project.Canvas.Padding = 10;
        Assert.NotEqual(10, preset.Canvas.Padding);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(40, 40)]
    [InlineData(-40, 35)]
    public void PerspectiveQuadFitsFrameAndHasPositiveDepth(double x, double y)
    {
        var quad = PerspectiveGeometry.Corners(640, 360, x, y);
        for (var i = 0; i < quad.Length; i++)
        {
            var p = quad[i];
            Assert.InRange(p.X, -1e-8, 640 + 1e-8);
            Assert.InRange(p.Y, -1e-8, 360 + 1e-8);
            Assert.True(p.Weight > 0);
            var source = PerspectiveGeometry.SourcePoint(quad, p.X, p.Y);
            Assert.Equal(i % 2, source.X, 6);
            Assert.Equal(i / 2, source.Y, 6);
        }
        var mask = MaskImage.CreateProjectionMask(640, 360, quad);
        Assert.Equal(255, mask[(180 * 640 + 320) * 4]);
        if (x != 0 || y != 0) Assert.Equal(0, mask[0]);
    }

    [Fact]
    public void HundredTimesSpeedUsesChainedAudioTempo()
    {
        var project = Sample();
        project.Assets[0].SystemAudio = "system.wav";
        project.Clips[0].SourceEnd = 100;
        project.Clips[0].Speed = 100;
        ProjectFile.Normalize(project);
        Assert.Equal(1, TimelineMath.Duration(project));
        var media = new ResolvedMedia
        {
            Videos = new Dictionary<string, string> { ["a"] = "screen.mp4" },
            Systems = new Dictionary<string, string> { ["a"] = "system.wav" },
            Microphones = new Dictionary<string, string>(), Cameras = new Dictionary<string, string>()
        };
        var plan = ExportGraph.Build(project, new(), media);
        Assert.Contains("atempo=2,atempo=2", plan.FilterScript);
        Assert.DoesNotContain("atempo=100", plan.FilterScript);
    }
}
