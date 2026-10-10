using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class ChromeGeometryTests
{
    static FramePlacement Card(int w = 1108, int h = 624) => new(86, 48, w, h, 18, 8, 10, 0.14);

    [Fact]
    public void None_ReturnsNull()
    {
        Assert.Null(ChromeGeometry.Resolve(new CanvasSettings(), Card(), 1280, 720));
        Assert.Null(ChromeGeometry.Resolve(new CanvasSettings { ScreenFrame = "none" }, Card(), 1280, 720));
    }

    [Fact]
    public void Window_ToolbarInsetMatchesMacSpec()
    {
        // styleScale = min(1280,720)/1080 = 0.667；顶栏 = 46*0.667*0.9 ≈ 27.6 + 接缝微扩。
        var spec = ChromeGeometry.Resolve(new CanvasSettings { ScreenFrame = "windowLight" }, Card(), 1280, 720)!;
        Assert.Equal("window", spec.Kind);
        Assert.InRange(spec.ToolbarH, 28, 34);
        Assert.Equal(86, spec.OuterX, 1);
        Assert.True(spec.OuterY < 48);           // 外框只向上扩
        Assert.Equal(624 + spec.OuterY * -1 + 48, spec.OuterH, 1);
        Assert.Equal(0xECEDF0u, spec.Surface);   // 亮色套件
        Assert.Equal(0xF4F4F6u, spec.Toolbar);
    }

    [Fact]
    public void DarkBrowser_DarkPaletteAndAddress()
    {
        var spec = ChromeGeometry.Resolve(new CanvasSettings
        {
            ScreenFrame = "browserDark",
            ScreenFrameAddress = "demo.internal",
        }, Card(), 1280, 720)!;
        Assert.Equal("browser", spec.Kind);
        Assert.Equal(0x222327u, spec.Surface);
        Assert.Equal("demo.internal", spec.Address);
    }

    [Fact]
    public void Phone_BezelAllSidesAndOrientation()
    {
        // 竖屏卡片 → phonePortrait，四边 bezel = 短边*0.026。
        var spec = ChromeGeometry.Resolve(new CanvasSettings { ScreenFrame = "devicePhone" },
            Card(576, 1024), 720, 1280)!;
        Assert.Equal("phonePortrait", spec.Kind);
        var bezel = 576 * 0.026;
        Assert.Equal(86 - bezel, spec.OuterX, 1);
        Assert.Equal(48 - bezel, spec.OuterY, 1);
        Assert.Equal(576 + bezel * 2, spec.OuterW, 1);
        Assert.Equal(0x222327u, spec.Surface);
        Assert.True(spec.IsDevice);
    }

    [Fact]
    public void PlacementRadius_OverriddenByContentRadius()
    {
        var placement = Card();
        var chrome = ChromeGeometry.Resolve(new CanvasSettings { ScreenFrame = "devicePhone" }, placement, 720, 1280)!;
        // 设备壳内容圆角 = 外圆角 - bezel > 0，且小于半短边。
        Assert.InRange(chrome.ContentRadius, 1, Math.Min(placement.Width, placement.Height) / 2);
    }
}
