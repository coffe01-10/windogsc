// 屏幕外框（样机/窗口/浏览器 chrome）几何与配色，1:1 复刻
// laogou717/dogsc ScreenFrameGeometry.decorationInsetsAtScaleOne +
// FrameSceneEvaluator.screenDecoration。尺寸按 1080pt 短边参考系换算。
namespace DogSC.Core;

public sealed record ChromeSpec(
    string Kind,                       // window / browser / phonePortrait / phoneLandscape / tabletPortrait / tabletLandscape
    double OuterX, double OuterY, double OuterW, double OuterH,
    double ToolbarH,                   // 顶栏高（窗口/浏览器），设备壳为 bezel 宽
    double OuterRadius, double ContentRadius,
    uint Surface, uint Toolbar, uint Separator, uint Field, uint Glyph,
    string Title, string Address)
{
    public bool IsDevice => Kind.StartsWith("phone") || Kind.StartsWith("tablet");
    public bool IsBrowser => Kind == "browser";
}

public static class ChromeGeometry
{
    // 对齐 Mac ScreenFrameStyle 枚举；额外接受短名。
    public static ChromeSpec? Resolve(CanvasSettings canvas, FramePlacement placement, int canvasW, int canvasH)
    {
        var style = (canvas.ScreenFrame ?? "none").Trim();
        if (style.Length == 0 || style is "none" or "无")
        {
            return null;
        }

        var styleScale = Math.Max(Math.Min(canvasW, canvasH) / 1080.0, 0.0001);
        var frameScale = Math.Clamp(canvas.ScreenFrameScale, 0.6, 1.6);
        var toolbarScale = Math.Clamp(canvas.ScreenFrameToolbarScale, 0.65, 1.6);
        var visualScale = styleScale * frameScale;
        var fittedW = Math.Max(placement.Width, 1);
        var fittedH = Math.Max(placement.Height, 1);

        double top = 0, side = 0, bottom = 0;
        var kind = "window";
        var isDevice = false;
        var isPhone = false;
        switch (style)
        {
            case "windowLight":
            case "windowDark":
            case "browserLight":
            case "browserDark":
            case "window":
            case "browser":
                var ideal = 46 * visualScale * toolbarScale;
                top = Math.Min(Math.Max(ideal, 4 * styleScale), Math.Max(fittedH * 0.25, 4 * styleScale));
                kind = style.Contains("browser", StringComparison.OrdinalIgnoreCase) ? "browser" : "window";
                break;
            case "devicePhone":
            case "devicePhonePortrait":
            case "devicePhoneLandscape":
            case "phone":
                var shortEdge = Math.Max(Math.Min(fittedW, fittedH), 1);
                var bezel = Math.Min(Math.Max(shortEdge * 0.026 * frameScale, 2 * styleScale), shortEdge * 0.07);
                top = side = bottom = bezel;
                isDevice = true; isPhone = true;
                kind = "phone" + (fittedW >= fittedH ? "Landscape" : "Portrait");
                if (style is "devicePhonePortrait") kind = "phonePortrait";
                if (style is "devicePhoneLandscape") kind = "phoneLandscape";
                break;
            case "deviceTablet":
            case "deviceTabletPortrait":
            case "deviceTabletLandscape":
            case "tablet":
                var shortEdgeT = Math.Max(Math.Min(fittedW, fittedH), 1);
                var bezelT = Math.Min(Math.Max(shortEdgeT * 0.035 * frameScale, 2 * styleScale), shortEdgeT * 0.08);
                top = side = bottom = bezelT;
                isDevice = true;
                kind = "tablet" + (fittedW >= fittedH ? "Landscape" : "Portrait");
                if (style is "deviceTabletPortrait") kind = "tabletPortrait";
                if (style is "deviceTabletLandscape") kind = "tabletLandscape";
                break;
            default:
                return null;
        }

        var outerX = placement.X - side;
        var outerY = placement.Y - top;
        var outerW = placement.Width + side * 2;
        var outerH = placement.Height + top + bottom;
        var toolbarH = top + Math.Min(2 * visualScale, top * 0.08);

        // 外圆角：窗口 18pt / 浏览器 22pt（*visualScale）；设备按外框短边比例。
        var outerRadius = isDevice
            ? Math.Min(outerW, outerH) * (isPhone ? 0.11 : 0.065)
            : (kind == "browser" ? 22 : 18) * visualScale;
        if (canvas.ScreenFrameOuterCornerRadius is { } authoredOuter)
            outerRadius = Math.Clamp(authoredOuter, 0, 160) * visualScale;
        outerRadius = Math.Min(outerRadius, Math.Min(outerW, outerH) / 2);

        // 无显式半径时：设备壳 = 外圆角 - bezel；窗口/浏览器内容为直角。
        var contentRadius = isDevice
            ? Math.Max(outerRadius - Math.Min(top, side), 0)
            : 0;
        if (canvas.ScreenFrameContentCornerRadius is { } authoredContent)
            contentRadius = Math.Clamp(authoredContent, 0, 160) * visualScale;
        contentRadius = Math.Min(contentRadius, Math.Min(fittedW, fittedH) / 2);

        var dark = style.EndsWith("Dark", StringComparison.OrdinalIgnoreCase) || isDevice;
        return new ChromeSpec(
            kind, outerX, outerY, outerW, outerH, toolbarH,
            outerRadius, contentRadius,
            dark ? 0x222327u : 0xECEDF0u,
            dark ? 0x313238u : 0xF4F4F6u,
            dark ? 0x4B4D55u : 0xC9CACEu,
            dark ? 0x1B1C20u : 0xFFFFFFu,
            dark ? 0xA9ABB3u : 0x7A7C84u,
            canvas.ScreenFrameTitle ?? "",
            canvas.ScreenFrameAddress ?? "");
    }
}
