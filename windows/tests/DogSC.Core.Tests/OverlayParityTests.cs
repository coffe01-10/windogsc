using System.Text.Json;
using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class OverlayParityTests
{
    static ProjectDocument Sample() => new()
    {
        Width = 640, Height = 360,
        Assets = [new() { Id = "a", Video = "screen.mp4", Duration = 10 }],
        Clips = [new() { Id = "c", AssetId = "a", SourceEnd = 10 }],
    };

    [Fact]
    public void LegacyDecorationsMigrateWithoutWritingDuplicateFields()
    {
        var project = JsonSerializer.Deserialize<ProjectDocument>("""
            {"canvas":{"sticker":{"path":"logo.png","timing":{"start":2,"duration":3}},
            "softRegion":{"enabled":true,"x":0.1,"width":0.2}}}
            """, ProjectSchema.Json)!;
        ProjectFile.Normalize(project);
        Assert.Equal(2, Assert.Single(project.Canvas.Stickers).Timing.Start);
        Assert.Equal("canvas", Assert.Single(project.Canvas.SoftRegions).CoordinateSpace);
        var json = JsonSerializer.Serialize(project, ProjectSchema.Json);
        Assert.DoesNotContain("\"sticker\":", json);
        Assert.DoesNotContain("\"softRegion\":", json);
        var restored = JsonSerializer.Deserialize<ProjectDocument>(json, ProjectSchema.Json)!;
        Assert.Single(restored.Canvas.Stickers);
        Assert.Single(restored.Canvas.SoftRegions);
    }

    [Fact]
    public void AllDecorationsFollowRippleTrimsAndRetiming()
    {
        var project = Sample();
        project.Canvas.Stickers = [new() { Path = "a.png", Timing = new() { Start = 2, Duration = 2 } },
            new() { Path = "b.png", Timing = new() { Start = 6, Duration = 2 } }];
        project.Canvas.SoftRegions = [new() { Enabled = true, Timing = new() { Start = 0, Duration = 1 } },
            new() { Enabled = true, CoordinateSpace = "source", Style = "spotlight", Timing = new() { Start = 6, Duration = 2 } }];
        Assert.True(TimelineEdits.TryTrim(project, "c", 2, 10));
        Assert.Single(project.Canvas.SoftRegions);
        Assert.Equal(0, project.Canvas.Stickers[0].Timing.Start);
        Assert.Equal(4, project.Canvas.Stickers[1].Timing.Start);
        Assert.True(TimelineEdits.TrySetSpeed(project, "c", 2));
        Assert.Equal(2, project.Canvas.Stickers[1].Timing.Start);
        Assert.Equal(1, project.Canvas.Stickers[1].Timing.Duration);
        Assert.Equal(2, project.Canvas.SoftRegions[0].Timing.Start);
    }

    [Fact]
    public void SceneCopiesPreserveAllLayersWithoutSharingReferences()
    {
        var source = Sample();
        source.Canvas.Stickers = [new() { Path = "a.png" }, new() { Path = "b.png", Rotation = 30 }];
        source.Canvas.SoftRegions = [new() { Enabled = true }, new() { Enabled = true, Style = "spotlight" }];
        var bundle = TimelineEdits.CopyEffect(source, "c")!;
        var target = Sample();
        Assert.True(TimelineEdits.PasteEffect(target, "c", bundle, false));
        Assert.Equal(2, target.Canvas.Stickers.Count);
        Assert.Equal(2, target.Canvas.SoftRegions.Count);
        target.Canvas.Stickers[1].Rotation = 70;
        target.Canvas.SoftRegions[1].Dimming = .7;
        Assert.Equal(30, source.Canvas.Stickers[1].Rotation);
        Assert.Equal(.22, source.Canvas.SoftRegions[1].Dimming);
    }

    [Fact]
    public void MacImportsMultipleLayersSourceRectsAndIndependentTransitions()
    {
        var result = MacProjectImporter.Import("""
            {"media":{"screen":{"relativePath":"screen.mov"}},"timeline":{
            "sourceSequence":{"kind":"edited","segments":[{"sourceStart":0,"sourceDuration":10}]},
            "stickerClips":[{"id":"top","relativePath":"top.png","layerIndex":4,"rotationDegrees":25,"animation":"fade","timing":{"startTime":2,"duration":3}},
                {"id":"bottom","relativePath":"bottom.png","layerIndex":0,"animation":"none","timing":{"startTime":0,"duration":2}}],
            "mosaicClips":[{"id":"spot","style":"spotlight","sourceRect":{"x":0.3,"y":0.4,"width":0.2,"height":0.1},
                "cornerRadius":0.25,"spotlightDimming":0.7,"transitionStyle":"smooth","transitionInDuration":0.6,"transitionOutDuration":0,
                "timing":{"startTime":1,"duration":4}}]}}
            """ );
        Assert.Equal("bottom", result.Project.Canvas.Stickers[0].Id);
        Assert.Equal(25, result.Project.Canvas.Stickers[1].Rotation);
        Assert.Contains("top.png", result.MediaReferences);
        var region = Assert.Single(result.Project.Canvas.SoftRegions);
        Assert.Equal("source", region.CoordinateSpace);
        Assert.Equal("spotlight", region.Style);
        Assert.Equal(.3, region.X);
        Assert.Equal(.25, region.Corner);
        Assert.Equal(.6, region.Timing.FadeIn);
        Assert.Equal(0, region.Timing.FadeOut);
        Assert.DoesNotContain(result.DroppedFields, x => x == "timeline.mosaicClips" || x == "timeline.stickerClips");
    }

    [Fact]
    public void SmoothTransitionsMatchMacQuinticAndOverlappingRamps()
    {
        var window = new EffectWindow { Start = 1, Duration = 1, Transition = "smooth", FadeIn = 2, FadeOut = 1 };
        // enter=.25 -> quintic(.25)=.103515625; min with exit=.5, not a product.
        Assert.Equal(.103515625, EffectTiming.OpacityAt(window, 1.5), 9);
        window.FadeIn = 0;
        Assert.Equal(1, EffectTiming.OpacityAt(window, 1), 9);
        Assert.Equal(0, EffectTiming.OpacityAt(window, 2));
        window.Transition = "none";
        Assert.Equal(1, EffectTiming.OpacityAt(window, 1.99));
    }

    [Fact]
    public void RegionBoundsAndRoundedMaskStayInsideSource()
    {
        var project = Sample();
        var region = new SoftRegion { Enabled = true, X = .9, Y = .9, Width = .5, Height = .5, Corner = .5 };
        project.Canvas.SoftRegions.Add(region);
        ProjectFile.Normalize(project);
        Assert.Equal(.5, region.X);
        Assert.Equal(.5, region.Y);
        var mask = MaskImage.CreateRegionMask(100, 100, region);
        Assert.Equal(0, mask[(51 * 100 + 51) * 4]);
        Assert.Equal(255, mask[(75 * 100 + 75) * 4]);
        Assert.Equal(0, mask[(25 * 100 + 25) * 4]);
    }

    [Fact]
    public void SourceEffectsAreAppliedBeforeCropAndZoomWithGlobalTimePerSlice()
    {
        var project = Sample();
        project.Zooms.Add(new() { Start = 3, Duration = 2, Scale = 2 });
        TimelineEdits.TrySplit(project, 3);
        project.Canvas.SoftRegions = [new() { Enabled = true, CoordinateSpace = "source", Style = "spotlight", Timing = new() { Start = 1, Duration = 6 } },
            new() { Enabled = true, CoordinateSpace = "canvas" }];
        var media = new ResolvedMedia { Videos = new Dictionary<string, string> { ["a"] = "screen.mp4" },
            Microphones = new Dictionary<string, string>(), Systems = new Dictionary<string, string>(), Cameras = new Dictionary<string, string>() };
        var plan = ExportGraph.Build(project, new(), media);
        Assert.Equal(2, plan.RegionMasks.Count);
        Assert.True(plan.FilterScript.IndexOf("maskedmerge", StringComparison.Ordinal) < plan.FilterScript.IndexOf("crop=", StringComparison.Ordinal));
        Assert.Contains("(T+3)", plan.FilterScript);
        Assert.Contains("(255-p(X,Y))", plan.FilterScript);
        Assert.Empty(ExportGraph.Build(project, new() { AudioOnly = true }, media).RegionMasks);
    }
}
