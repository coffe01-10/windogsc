using System.Text.Json;
using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class FocusEffectTests
{
    static ProjectDocument Project() => new()
    {
        Width = 320, Height = 180, Canvas = new() { Padding = 0, Corner = 0, Shadow = 0, Cursor = new() { Mode = "hidden", Motion = "none" } },
        Assets = [new() { Id = "a", Video = "screen.mp4", Duration = 5, Width = 320, Height = 180,
            Cursor = [new() { Time = 0, X = .2, Y = .3 }, new() { Time = 4, X = .8, Y = .7 }] }],
        Clips = [new() { AssetId = "a", SourceEnd = 5 }],
        Zooms = [new() { Start = 1, Duration = 3, Scale = 1, EnterDuration = .5, ExitDuration = .5,
            FocusEffect = new() { Target = "fixed", Center = new(.5, .5) } }],
    };

    [Fact]
    public void RadialRampMatchesMacClearRadiusAndFeather()
    {
        var effect = new FocusEffect { Size = .4, Softness = .5 };
        Assert.Equal(0, effect.Outside(20, 0, 0, 0, 100));
        Assert.Equal(.5, effect.Outside(27.5, 0, 0, 0, 100), 9);
        Assert.Equal(1, effect.Outside(35, 0, 0, 0, 100));
        effect.Shape = "linear"; effect.AngleDegrees = 90;
        Assert.Equal(0, effect.Outside(0, 80, 0, 0, 100), 9);
        Assert.Equal(.5, effect.Outside(30, 80, 0, 0, 100), 9);
        Assert.Equal(28, new FocusEffect { Blur = 1 }.BlurRadius(1920, 1080));
    }

    [Fact]
    public void EffectEntersAndExitsWithoutLeakingPastZoom()
    {
        var track = new FocusTrack(Project());
        Assert.Null(track.At(.9)); Assert.Null(track.At(1));
        Assert.Equal(.5, track.At(1.25)!.Progress, 9);
        Assert.Equal(1, track.At(2)!.Progress);
        Assert.Equal(.5, track.At(3.75)!.Progress, 9);
        Assert.Null(track.At(4));
    }

    [Fact]
    public void PointerFocusWorksWithHiddenCursorAndUsesEditedSourceClock()
    {
        var p = Project(); p.Clips[0].SourceStart = 1; p.Clips[0].Speed = 2;
        p.Assets[0].Cursor = [new() { Time = 1.96, X = .2, Y = .3 }, new() { Time = 2.04, X = .8, Y = .7 }];
        p.Zooms[0].Start = 0; p.Zooms[0].FocusEffect!.Target = "pointer";
        var center = new FocusTrack(p).At(.5)!.Center; // source=2 seconds, halfway between pointer samples
        Assert.Equal(.5, center.X, 9); Assert.Equal(.5, center.Y, 9);
    }

    [Fact]
    public void Enabled3DFocusOwnsSingleEffectAndLegacyLinearStillWorks()
    {
        var p = Project(); p.ScreenMotions = [new() { Id = "3d", LinearBlur = true, LinearBlurAngle = 25,
            Timing = new() { StartTime = 1, Duration = 1, ReturnDuration = .5, Easing = "linear" } }];
        var track = new FocusTrack(p);
        Assert.Equal("3d", track.At(1.5)!.Id); Assert.Equal("linear", track.At(1.5)!.Effect.Shape);
        Assert.Equal(25, track.At(1.5)!.Effect.AngleDegrees); Assert.Equal(0, track.At(1.5)!.Effect.Dimming);
        Assert.Equal(p.Zooms[0].Id, track.At(2.5)!.Id);
    }

    [Fact]
    public void CropAndZoomKeepFixedSourceFocusAtItsRenderedPoint()
    {
        var p = Project(); p.Canvas.Crop = new() { X = .25, Y = .25, Width = .5, Height = .5 };
        p.Zooms[0].Scale = 2; p.Zooms[0].FocusEffect!.Center = new(.6, .5);
        var track = new FocusTrack(p); var scene = track.At(2)!;
        var placement = new FramePlacement(20, 10, 280, 160, 0, 0, 0, 0);
        var mask = new FocusRaster(p, scene, placement, 320, 180, 2, new(2, .5, .5));
        Assert.Equal(0, mask.Coverage(272, 90), 9);
        p.Zooms[0].FocusEffect!.Center = new(.1, .1);
        Assert.Null(new FocusTrack(p).At(2));
    }

    [Fact]
    public void DimmingWithoutBlurAndEditingOutsideTheClipRemainUsable()
    {
        var p = Project(); p.Zooms[0].FocusEffect!.Blur = 0;
        Assert.NotNull(new FocusTrack(p).At(2));
        Assert.Equal(1, new FocusTrack(p).At(.2, editingZoom: p.Zooms[0])!.Progress);
        p.Zooms[0].FocusEffect!.Dimming = 0; Assert.Null(new FocusTrack(p).At(2));
    }

    [Fact]
    public void FocusSurvivesSaveAndCopyWithoutAliasingTheOriginal()
    {
        var p = Project(); var snapshot = JsonSerializer.Deserialize<ProjectDocument>(JsonSerializer.Serialize(p, ProjectSchema.Json), ProjectSchema.Json)!;
        Assert.Equal(.18, snapshot.Zooms[0].FocusEffect!.Dimming);
        var copy = TimelineEdits.CopyZoom(p.Zooms[0]); copy.FocusEffect!.Center = new(.2, .2);
        Assert.Equal(new NormalizedPoint(.5, .5), p.Zooms[0].FocusEffect!.Center);
        p.ScreenMotions = [new() { FocusEffect = FocusEffect.Linear() }];
        var style = ScreenMotionCreationStyle.Capture(p.ScreenMotions[0]);
        var created = style.Create(new() { Duration = 1 }); created.FocusEffect!.Size = .9;
        Assert.Equal(.32, style.FocusEffect!.Size); Assert.Equal(.32, p.ScreenMotions[0].FocusEffect!.Size);
    }

    [Fact]
    public void MacImportsRadialLinearTargetsAndDefaultFocus()
    {
        const string json = """
        {"media":{"screen":{"relativePath":"media/screen.mp4"}},"timeline":{"sourceSequence":{"kind":"edited",
          "segments":[{"sourceStart":0,"sourceDuration":5}]},
          "zoomClips":[{"startTime":1,"endTime":4,"scale":2,"focusEffect":{"target":"pointer","size":0.6,"dimming":0.4}}],
          "screenMotionClips":[{"startTime":1,"duration":2,"focusEffect":{"angleDegrees":45,"size":0.3}}]},
          "motion":{"defaultScreenMotion":{"focusEffect":{"shape":"radial","target":"fixed","center":{"x":0.2,"y":0.8}}}}}
        """;
        var result = MacProjectImporter.Import(json); var p = result.Project;
        Assert.Equal("radial", p.Zooms[0].FocusEffect!.Shape); Assert.Equal("pointer", p.Zooms[0].FocusEffect!.Target);
        Assert.Equal(.4, p.Zooms[0].FocusEffect!.Dimming);
        Assert.Equal("linear", p.ScreenMotions[0].FocusEffect!.Shape); Assert.Equal(45, p.ScreenMotions[0].FocusEffect!.AngleDegrees);
        Assert.Equal(new NormalizedPoint(.2, .8), p.Motion.DefaultScreenMotion!.FocusEffect!.Center);
        Assert.DoesNotContain(result.DroppedFields, f => f.Contains("focusEffect"));
    }
}
