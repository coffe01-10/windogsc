namespace DogSC.Core;

public static class ScreenFrameStyles
{
    // ScreenFrameDefaults.swift: selecting a new style and restoring a style
    // use the same authored geometry. Metadata and composition stay in place.
    public static void ApplyDefaults(CanvasSettings canvas, string style)
    {
        var (frameScale, toolbarScale, outer, content, shadow) = style switch
        {
            "windowLight" or "window" => (1d, .90, 18d, 0d, 22d),
            "windowDark" => (1d, .90, 18d, 0d, 30d),
            "browserLight" or "browser" => (1d, 1.08, 22d, 0d, 22d),
            "browserDark" => (1d, 1.08, 22d, 0d, 30d),
            "phone" or "devicePhone" or "devicePhonePortrait" or "devicePhoneLandscape" => (.90, 1d, 64d, 40d, 24d),
            "tablet" or "deviceTablet" or "deviceTabletPortrait" or "deviceTabletLandscape" => (1d, 1d, 48d, 24d, 24d),
            _ => (1d, 1d, 24d, 24d, 28d),
        };
        canvas.ScreenFrame = style;
        canvas.ScreenFrameScale = frameScale; canvas.ScreenFrameToolbarScale = toolbarScale;
        canvas.ScreenFrameOuterCornerRadius = outer; canvas.ScreenFrameContentCornerRadius = content;
        canvas.Corner = content; canvas.Shadow = shadow; canvas.ScreenBorderWidth = 0;
    }
}
