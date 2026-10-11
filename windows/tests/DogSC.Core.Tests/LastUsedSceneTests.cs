using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class LastUsedSceneTests
{
    [Fact]
    public void LastUsedAppearancePreservesTargetCropVisibilityAndTimedContent()
    {
        var source = new ProjectDocument { Width = 960, Height = 720, MicrophoneGain = .4, SystemEnabled = false };
        source.Canvas.Padding = 108; source.Canvas.Corner = 32; source.Canvas.Wallpaper = "pattern:engineering";
        source.Canvas.Crop = new SourceCrop { X = .2, Width = .6 };
        source.Camera.Hidden = true; source.Camera.Shape = "square"; source.Camera.Size = .31;
        source.Canvas.Cursor.Size = 1.7; source.Motion.DefaultZoomScale = 3.6;
        source.Motion.DefaultScreenMotion = new ScreenMotionCreationStyle { Target = new ScreenMotionState { RotationY = 20 } };
        source.Canvas.Stickers = [new StickerImage()]; source.Canvas.SoftRegions = [new SoftRegion()];
        var remembered = ScenePreset.CaptureLastUsed(source);
        var target = new ProjectDocument { Width = 1920, Height = 1080 };
        var crop = new SourceCrop { Y = .1, Height = .8 }; target.Canvas.Crop = crop;
        var zoom = new ZoomSpan { Scale = 2.2 }; target.Zooms = [zoom];
        var sticker = new StickerImage(); target.Canvas.Stickers = [sticker];
        target.Camera.TimingAdvance = .3; target.Canvas.Cursor.TimingAdvance = -.1;
        remembered.Apply(target);
        Assert.False(remembered.IncludesCrop); Assert.False(remembered.NeedsCropPreview(target));
        Assert.Equal(.1, target.Canvas.Crop.Y); Assert.Equal(.8, target.Canvas.Crop.Height);
        Assert.NotSame(crop, target.Canvas.Crop); Assert.False(target.Camera.Hidden);
        Assert.Equal("square", target.Camera.Shape); Assert.Equal(.31, target.Camera.Size);
        Assert.Equal(108, target.Canvas.Padding); Assert.Equal(32, target.Canvas.Corner);
        Assert.Equal("pattern:engineering", target.Canvas.Wallpaper); Assert.Equal(1.7, target.Canvas.Cursor.Size);
        Assert.Equal(3.6, target.Motion.DefaultZoomScale); Assert.Equal(20, target.Motion.DefaultScreenMotion!.Target.RotationY);
        Assert.Equal(.4, target.MicrophoneGain); Assert.False(target.SystemEnabled);
        Assert.Equal(.3, target.Camera.TimingAdvance); Assert.Equal(-.1, target.Canvas.Cursor.TimingAdvance);
        Assert.Same(zoom, Assert.Single(target.Zooms)); Assert.Same(sticker, Assert.Single(target.Canvas.Stickers));
        Assert.Empty(remembered.Canvas.Stickers); Assert.Empty(remembered.Canvas.SoftRegions);
        source.Canvas.Padding = 3; Assert.Equal(108, remembered.Canvas.Padding);
    }

    [Fact]
    public void ProjectOwnedBackgroundDoesNotTravelImplicitlyButExplicitScenesKeepCropAndVisibility()
    {
        var source = new ProjectDocument(); source.Canvas.Wallpaper = "media/decor/owned.png";
        source.Canvas.Crop.Width = .6; source.Camera.Hidden = true;
        var target = new ProjectDocument(); target.Canvas.Background = "#336699"; target.Canvas.Wallpaper = "pattern:obsidian";
        var remembered = ScenePreset.CaptureLastUsed(source);
        Assert.False(remembered.IncludesBackground); remembered.Apply(target);
        Assert.Equal("#336699", target.Canvas.Background); Assert.Equal("pattern:obsidian", target.Canvas.Wallpaper);
        Assert.Equal(1, target.Canvas.Crop.Width); Assert.False(target.Camera.Hidden);
        var explicitScene = ScenePreset.Capture(source, "新录制默认"); explicitScene.Apply(target);
        Assert.Equal(.6, target.Canvas.Crop.Width); Assert.True(target.Camera.Hidden);
        Assert.True(explicitScene.IncludesCrop);
    }

    [Fact]
    public void LastUsedConfigurationSurvivesSettingsSaveAndColdLoad()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dogsc-last-used-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "settings.json");
        try
        {
            var project = new ProjectDocument(); project.Canvas.Padding = 112; project.Motion.DefaultZoomScale = 4.5;
            var settings = new AppSettings { LastUsedScene = ScenePreset.CaptureLastUsed(project) };
            SettingsFile.Save(path, settings); var loaded = SettingsFile.Load(path);
            Assert.NotNull(loaded.LastUsedScene); Assert.False(loaded.LastUsedScene.IncludesCrop);
            var target = new ProjectDocument(); loaded.LastUsedScene.Apply(target);
            Assert.Equal(112, target.Canvas.Padding); Assert.Equal(4.5, target.Motion.DefaultZoomScale);
            Assert.Equal(1, target.Canvas.Crop.Width);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }
}
