using System.Globalization;

namespace DogSC.Core;

public static class ParameterNumber
{
    public static string StripUnits(string text)
    {
        var value = text.Trim().ToLowerInvariant().Replace('，', '.').Replace(',', '.');
        foreach (var unit in new[] { "％", "%", "×", "x", "倍", "秒", "s", "°", "度" })
            value = value.Replace(unit, "");
        return value.Trim();
    }

    // Same bare-number and pasted-unit semantics as EditorSliderValueFormat.
    public static bool TryParse(string text, bool percent, double min, double max, out double value)
    {
        value = 0;
        if (!double.TryParse(StripUnits(text), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed)) return false;
        value = Math.Clamp(percent ? parsed / 100 : parsed, min, max);
        return true;
    }
}
