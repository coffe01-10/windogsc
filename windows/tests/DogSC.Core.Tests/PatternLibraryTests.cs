using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class PatternLibraryTests
{
    [Theory]
    [InlineData("obsidianGrid")]
    [InlineData("engineeringWhiteGrid")]
    [InlineData("midnightDots")]
    [InlineData("architecturalDots")]
    [InlineData("isometricMesh")]
    public void PatternPresets_AllResolve_SingleStaticLayer(string id)
    {
        var bake = PatternLibrary.Resolve("pattern:" + id, 1080, 720, 1, 1);
        Assert.NotNull(bake);
        var layer = Assert.Single(bake!.Layers);
        Assert.Equal("0", layer.X);
        Assert.Equal("0", layer.Y);
    }

    [Fact]
    public void PatternPreset_ScaleMovesTileSize()
    {
        var small = PatternLibrary.Resolve("pattern:obsidianGrid", 1080, 720, 0.5, 1)!;
        var large = PatternLibrary.Resolve("pattern:obsidianGrid", 1080, 720, 2.0, 1)!;
        Assert.True(large.Layers[0].P1 > small.Layers[0].P1);
    }

    [Theory]
    [InlineData("cyberDriftGrid", 2)]
    [InlineData("starfieldDots", 1)]
    [InlineData("auroraFluid", 3)]
    public void FlowPresets_LayerCountsAndMovingExpressions(string id, int expectedLayers)
    {
        var bake = PatternLibrary.Resolve("flow:" + id, 1080, 720, 1, 1);
        Assert.NotNull(bake);
        Assert.True(bake!.Animated);
        Assert.Equal(expectedLayers, bake.Layers.Count);
        foreach (var layer in bake.Layers)
        {
            // ffmpeg 表达式安全：不能含 ':' ';' 会破坏滤镜图。
            Assert.DoesNotContain(':', layer.X);
            Assert.DoesNotContain(';', layer.X);
            Assert.DoesNotContain(':', layer.Y);
        }
    }

    [Fact]
    public void Resolve_RejectsUnknownAndNonProcedural()
    {
        Assert.Null(PatternLibrary.Resolve("pattern:nope", 1080, 720, 1, 1));
        Assert.Null(PatternLibrary.Resolve("flow:nope", 1080, 720, 1, 1));
        Assert.Null(PatternLibrary.Resolve("C:\\wall.png", 1080, 720, 1, 1));
        Assert.Null(PatternLibrary.Resolve(null, 1080, 720, 1, 1));
    }

    [Fact]
    public void Canvas_PatternSettingsSerialize()
    {
        var project = new ProjectDocument();
        project.Assets.Add(new MediaAsset { Id = "a", Video = "screen.mp4" });
        project.Clips.Add(new ClipSegment { Id = "c", AssetId = "a", SourceStart = 0, SourceEnd = 5 });
        project.Canvas.Wallpaper = "pattern:midnightDots";
        project.Canvas.PatternScale = 2.0;
        project.Canvas.PatternOpacity = 0.7;
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pat" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            ProjectFile.Save(dir, project);
            var loaded = ProjectFile.Load(dir);
            Assert.Equal("pattern:midnightDots", loaded.Canvas.Wallpaper);
            Assert.Equal(2.0, loaded.Canvas.PatternScale);
            Assert.Equal(0.7, loaded.Canvas.PatternOpacity);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MacImport_PatternBackgroundMapsToPseudoPath()
    {
        var json = """
        {
          "title": "t",
          "media": { "screen": { "relativePath": "media/screen.mp4" } },
          "timeline": { "sourceSequence": { "segments": [
            { "sourceStart": 0, "sourceDuration": 4, "playbackRate": 1 } ] } },
          "canvas": {
            "backgroundSource": { "kind": "pattern", "patternPreset": "暗夜星阵" },
            "patternScale": 1.6, "patternOpacity": 0.8
          }
        }
        """;
        var result = MacProjectImporter.Import(json);
        Assert.Equal("pattern:midnightDots", result.Project.Canvas.Wallpaper);
        Assert.Equal(1.6, result.Project.Canvas.PatternScale, 3);
        Assert.Equal(0.8, result.Project.Canvas.PatternOpacity, 3);
    }

    [Fact]
    public void MacImport_FlowBackgroundMapsToPseudoPath()
    {
        var json = """
        {
          "title": "t",
          "media": { "screen": { "relativePath": "media/screen.mp4" } },
          "timeline": { "sourceSequence": { "segments": [
            { "sourceStart": 0, "sourceDuration": 4, "playbackRate": 1 } ] } },
          "canvas": { "backgroundSource": { "kind": "dynamicFlow", "dynamicFlowPreset": "极光流体星云" } }
        }
        """;
        var result = MacProjectImporter.Import(json);
        Assert.Equal("flow:auroraFluid", result.Project.Canvas.Wallpaper);
    }
}
