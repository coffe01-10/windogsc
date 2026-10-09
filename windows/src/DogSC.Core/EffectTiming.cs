using System.Globalization;

namespace DogSC.Core;

public sealed class EffectWindow
{
    public double Start { get; set; }
    /// <summary>Zero keeps legacy decorations visible throughout the project.</summary>
    public double Duration { get; set; }
    public double Fade { get; set; } = 0.2;
    public double? FadeIn { get; set; }
    public double? FadeOut { get; set; }
    public string Transition { get; set; } = "linear";
}

public static class EffectTiming
{
    public static double OpacityAt(EffectWindow window, double time)
    {
        if (window.Duration <= 0) return 1;
        if (time < window.Start || time >= window.Start + window.Duration) return 0;
        if (window.Transition == "none") return 1;
        var enter = window.FadeIn ?? Math.Min(window.Fade, window.Duration / 2);
        var exit = window.FadeOut ?? Math.Min(window.Fade, window.Duration / 2);
        double Ramp(double value) => window.Transition == "smooth" ? value * value * value * (value * (value * 6 - 15) + 10) : value;
        return Math.Min(Ramp(enter <= 0 ? 1 : Math.Clamp((time - window.Start) / enter, 0, 1)),
            Ramp(exit <= 0 ? 1 : Math.Clamp((window.Start + window.Duration - time) / exit, 0, 1)));
    }

    public static string Expression(EffectWindow window, string time = "T")
    {
        if (window.Duration <= 0) return "1";
        string F(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);
        var bounds = $"gte({time}\\,{F(window.Start)})*lt({time}\\,{F(window.Start + window.Duration)})";
        if (window.Transition == "none") return bounds;
        string Ramp(string value, double seconds)
        {
            if (seconds <= 0) return "1";
            var p = $"clip(({value})/{F(seconds)}\\,0\\,1)";
            return window.Transition == "smooth" ? $"({p})*({p})*({p})*(({p})*(({p})*6-15)+10)" : p;
        }
        return bounds + "*min(" + Ramp($"{time}-{F(window.Start)}", window.FadeIn ?? Math.Min(window.Fade, window.Duration / 2))
            + "\\," + Ramp($"{F(window.Start + window.Duration)}-{time}", window.FadeOut ?? Math.Min(window.Fade, window.Duration / 2)) + ")";
    }

    public static void Normalize(EffectWindow window)
    {
        window.Start = Math.Max(0, window.Start);
        window.Duration = Math.Max(0, window.Duration);
        window.Fade = Math.Clamp(window.Fade, 0, 5);
        if (window.FadeIn is { } enter) window.FadeIn = Math.Clamp(enter, 0, 5);
        if (window.FadeOut is { } exit) window.FadeOut = Math.Clamp(exit, 0, 5);
        window.Transition = window.Transition is "smooth" or "none" ? window.Transition : "linear";
    }

    // Reuse the timeline's tested interval edits; full-length decorations keep
    // their meaning while timed decorations follow ripple trims and retiming.
    public static void Edit(ProjectDocument project, Action<IList<ZoomSpan>> edit)
    {
        project.Canvas.Stickers.RemoveAll(sticker => !EditWindow(sticker.Timing, edit));
        project.Canvas.SoftRegions.RemoveAll(soft => !EditWindow(soft.Timing, edit));
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
