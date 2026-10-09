using System.Globalization;

namespace DogSC.Core;

public sealed class EffectWindow
{
    public double Start { get; set; }
    /// <summary>Zero keeps legacy decorations visible throughout the project.</summary>
    public double Duration { get; set; }
    public double Fade { get; set; } = 0.2;
}

public static class EffectTiming
{
    public static double OpacityAt(EffectWindow window, double time)
    {
        if (window.Duration <= 0) return 1;
        if (time < window.Start || time >= window.Start + window.Duration) return 0;
        var fade = Math.Min(window.Fade, window.Duration / 2);
        return fade <= 0 ? 1 : Math.Clamp((time - window.Start) / fade, 0, 1)
            * Math.Clamp((window.Start + window.Duration - time) / fade, 0, 1);
    }

    public static string Expression(EffectWindow window, string time = "T")
    {
        if (window.Duration <= 0) return "1";
        var fade = Math.Min(window.Fade, window.Duration / 2);
        string F(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);
        var bounds = $"gte({time}\\,{F(window.Start)})*lt({time}\\,{F(window.Start + window.Duration)})";
        return fade <= 0 ? bounds : bounds + $"*clip(({time}-{F(window.Start)})/{F(fade)}\\,0\\,1)*clip(({F(window.Start + window.Duration)}-{time})/{F(fade)}\\,0\\,1)";
    }

    public static void Normalize(EffectWindow window)
    {
        window.Start = Math.Max(0, window.Start);
        window.Duration = Math.Max(0, window.Duration);
        window.Fade = Math.Clamp(window.Fade, 0, 2);
    }

    // Reuse the timeline's tested interval edits; full-length decorations keep
    // their meaning while timed decorations follow ripple trims and retiming.
    public static void Edit(ProjectDocument project, Action<IList<ZoomSpan>> edit)
    {
        if (project.Canvas.Sticker is { } sticker && !EditWindow(sticker.Timing, edit)) project.Canvas.Sticker = null;
        var soft = project.Canvas.SoftRegion;
        if (!EditWindow(soft.Timing, edit)) soft.Enabled = false;
    }

    static bool EditWindow(EffectWindow window, Action<IList<ZoomSpan>> edit)
    {
        if (window.Duration <= 0) return true;
        IList<ZoomSpan> proxy = new List<ZoomSpan> { new() { Start = window.Start, Duration = window.Duration } };
        edit(proxy);
        if (proxy.Count == 0) return false;
        window.Start = proxy[0].Start;
        window.Duration = proxy[0].Duration;
        return true;
    }
}
