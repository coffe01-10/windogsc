using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class ScreenFrameStylesTests
{
    [Theory]
    [InlineData("none", 1, 1, 24, 24, 28)]
    [InlineData("windowLight", 1, .90, 18, 0, 22)]
    [InlineData("windowDark", 1, .90, 18, 0, 30)]
    [InlineData("browserLight", 1, 1.08, 22, 0, 22)]
    [InlineData("browserDark", 1, 1.08, 22, 0, 30)]
    [InlineData("phone", .90, 1, 64, 40, 24)]
    [InlineData("tablet", 1, 1, 48, 24, 24)]
    public void RestoringStyleKeepsFrameAndMetadataWhileRestoringItsAuthoredGeometry(
        string style, double scale, double toolbar, double outer, double content, double shadow)
    {
        var canvas = new CanvasSettings { ScreenFrame = style, ScreenFrameTitle = "标题", ScreenFrameAddress = "example.test",
            ScreenFrameScale = 1.4, ScreenFrameToolbarScale = 1.6, ScreenBorderWidth = 12, Corner = 71, Shadow = 80,
            Padding = 108, CenterX = .3, Scale = 1.2, Wallpaper = "pattern:obsidian" };
        canvas.Crop.Width = .6;
        ScreenFrameStyles.ApplyDefaults(canvas, canvas.ScreenFrame);
        Assert.Equal(style, canvas.ScreenFrame); Assert.Equal("标题", canvas.ScreenFrameTitle); Assert.Equal("example.test", canvas.ScreenFrameAddress);
        Assert.Equal(scale, canvas.ScreenFrameScale); Assert.Equal(toolbar, canvas.ScreenFrameToolbarScale);
        Assert.Equal(outer, canvas.ScreenFrameOuterCornerRadius); Assert.Equal(content, canvas.ScreenFrameContentCornerRadius);
        Assert.Equal(content, canvas.Corner); Assert.Equal(shadow, canvas.Shadow); Assert.Equal(0, canvas.ScreenBorderWidth);
        Assert.Equal(108, canvas.Padding); Assert.Equal(.3, canvas.CenterX); Assert.Equal(1.2, canvas.Scale);
        Assert.Equal(.6, canvas.Crop.Width); Assert.Equal("pattern:obsidian", canvas.Wallpaper);
    }

    [Fact]
    public void WindowAndPhoneContentRadiiReachTheSharedPreviewAndExportPlacement()
    {
        var canvas = new CanvasSettings(); ScreenFrameStyles.ApplyDefaults(canvas, "windowLight");
        var placement = CanvasLayout.Place(1920, 1080, 1920, 1080, canvas);
        var chrome = ChromeGeometry.Resolve(canvas, placement, 1920, 1080)!;
        Assert.Equal(18, chrome.OuterRadius); Assert.Equal(0, chrome.ContentRadius); Assert.Equal(0, placement.Radius);
        ScreenFrameStyles.ApplyDefaults(canvas, "phone");
        placement = CanvasLayout.Place(1920, 1080, 1920, 1080, canvas);
        chrome = ChromeGeometry.Resolve(canvas, placement, 1920, 1080)!;
        Assert.Equal(57.6, chrome.OuterRadius, 5); Assert.Equal(36, chrome.ContentRadius, 5); Assert.Equal(36, placement.Radius);
        var recalled = ScenePreset.Clone(canvas);
        Assert.Equal(canvas.ScreenFrameOuterCornerRadius, recalled.ScreenFrameOuterCornerRadius);
        Assert.Equal(canvas.ScreenFrameContentCornerRadius, recalled.ScreenFrameContentCornerRadius);
    }
}
