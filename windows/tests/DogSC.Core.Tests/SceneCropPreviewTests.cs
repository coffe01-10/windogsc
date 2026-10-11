using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class SceneCropPreviewTests
{
    [Fact]
    public void DifferentDimensionsRequirePreviewOnlyWhenPresetIncludesAnActualCrop()
    {
        var source = new ProjectDocument { Width = 960, Height = 720 };
        source.Canvas.Crop = new SourceCrop { X = .25, Y = .1, Width = .5, Height = .8 };
        var preset = ScenePreset.Capture(source, "局部画面");
        Assert.False(preset.NeedsCropPreview(source));
        var target = new ProjectDocument { Width = 1920, Height = 1080 };
        Assert.True(preset.NeedsCropPreview(target));
        preset.IncludesCanvas = false; Assert.False(preset.NeedsCropPreview(target));
        preset.IncludesCanvas = true; preset.SourceWidth = preset.SourceHeight = 0;
        Assert.False(preset.NeedsCropPreview(target)); // Old snapshots have no source dimensions.
        preset.SourceWidth = 960; preset.SourceHeight = 720; preset.Canvas.Crop = new SourceCrop();
        Assert.False(preset.NeedsCropPreview(target));
    }

    [Fact]
    public void ApplyingAcrossDimensionsRetainsProportionsAndTargetMediaAndTimeline()
    {
        var source = new ProjectDocument { Width = 960, Height = 720 };
        source.Canvas.Crop = new SourceCrop { X = .25, Y = .1, Width = .5, Height = .8 };
        var preset = ScenePreset.Capture(source, "局部画面");
        var asset = new MediaAsset { Width = 1920, Height = 1080, Duration = 10 };
        var clip = new ClipSegment { AssetId = asset.Id, SourceStart = 2, SourceEnd = 8, Speed = 2 };
        var zoom = new ZoomSpan { Start = .5, Duration = 1, Scale = 2.8 };
        var target = new ProjectDocument { Width = 1920, Height = 1080, Assets = [asset], Clips = [clip], Zooms = [zoom] };
        preset.Apply(target); ProjectFile.Normalize(target);
        Assert.Equal((480, 108, 960, 864), CanvasLayout.Crop(target.Width, target.Height, target.Canvas.Crop));
        Assert.Equal(1920, target.Width); Assert.Equal(1080, target.Height);
        Assert.Same(asset, Assert.Single(target.Assets)); Assert.Same(clip, Assert.Single(target.Clips));
        Assert.Same(zoom, Assert.Single(target.Zooms)); Assert.Equal(3, TimelineMath.Duration(target));
        Assert.Equal(.25, preset.Canvas.Crop.X);
    }
}
