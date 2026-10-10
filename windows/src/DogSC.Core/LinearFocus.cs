namespace DogSC.Core;

public static class LinearFocus
{
    public static double Progress(ScreenMotionClip clip, double time)
    {
        var t = clip.Timing;
        if (time < t.StartTime) return 0;
        if (t.LeadInDuration > .0001 && time < t.StartTime + Math.Min(t.LeadInDuration, t.Duration))
            return MotionEasing.EasedProgress(t.LeadInProgressOffset + (1 - t.LeadInProgressOffset) *
                (time - t.StartTime) / Math.Min(t.LeadInDuration, t.Duration), t.Easing, t.CustomCurve);
        if (time < t.EndTime) return 1;
        if (t.ReturnDuration <= .0001) return 1;
        return 1 - MotionEasing.EasedProgress(t.ReturnProgressOffset + (1 - t.ReturnProgressOffset) *
            Math.Clamp((time - t.EndTime) / t.ReturnDuration, 0, 1), t.Easing, t.CustomCurve);
    }

    public static double Mask(double x, double y, double width, double height, ScreenMotionClip clip)
    {
        var a = clip.LinearBlurAngle * Math.PI / 180;
        var distance = Math.Abs(-(x - width / 2) * Math.Sin(a) + (y - height / 2) * Math.Cos(a));
        var shortEdge = Math.Min(width, height);
        return Math.Clamp((distance - shortEdge * clip.LinearBlurSize / 2) /
            Math.Max(1, shortEdge * clip.LinearBlurSoftness * .4), 0, 1);
    }
}
