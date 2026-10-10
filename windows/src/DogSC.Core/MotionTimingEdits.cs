namespace DogSC.Core;

/// <summary>Ripple editing maps motion entry, hold and return boundaries together.</summary>
public static class MotionTimingEdits
{
    public static void RemoveRange(ProjectDocument project, double start, double end)
    {
        if (end <= start) return;
        foreach (var timing in project.ScreenMotions.Select(c => c.Timing).Concat(project.CameraMotions.Select(c => c.Timing)))
        {
            var lead = Math.Min(timing.LeadInDuration, timing.Duration);
            if (start <= timing.StartTime && end > timing.StartTime && lead > 0)
                timing.LeadInProgressOffset += (1 - timing.LeadInProgressOffset) * Math.Clamp((end - timing.StartTime) / lead, 0, 1);
            if (start <= timing.EndTime && end > timing.EndTime && timing.ReturnDuration > 0)
                timing.ReturnProgressOffset += (1 - timing.ReturnProgressOffset) * Math.Clamp((end - timing.EndTime) / timing.ReturnDuration, 0, 1);
        }
        Apply(project, t => t <= start ? t : t < end ? start : t - (end - start));
    }

    public static void InsertGap(ProjectDocument project, double time, double duration)
    {
        if (duration <= 0) return;
        Apply(project, t => t >= time ? t + duration : t);
    }

    public static void Retime(ProjectDocument project, double start, double oldDuration, double newDuration)
    {
        if (oldDuration <= 0) return;
        var end = start + oldDuration;
        Apply(project, t => t <= start ? t : t < end
            ? start + (t - start) * newDuration / oldDuration : t + newDuration - oldDuration);
    }

    static void Apply(ProjectDocument project, Func<double, double> map)
    {
        project.ScreenMotions.RemoveAll(clip => !Map(clip.Timing));
        project.CameraMotions.RemoveAll(clip => !Map(clip.Timing));

        bool Map(TransitionTiming timing)
        {
            var start = map(timing.StartTime);
            var leadEnd = map(timing.StartTime + Math.Min(timing.LeadInDuration, timing.Duration));
            var end = map(timing.EndTime);
            var returnEnd = map(timing.EffectEndTime);
            if (returnEnd - start < .0001) return false;
            timing.StartTime = start;
            timing.Duration = Math.Max(0, end - start);
            timing.LeadInDuration = Math.Max(0, leadEnd - start);
            timing.ReturnDuration = Math.Max(0, returnEnd - end);
            if (timing.PreferredLeadInDuration is not null) timing.PreferredLeadInDuration = timing.LeadInDuration;
            if (timing.PreferredReturnDuration is not null) timing.PreferredReturnDuration = timing.ReturnDuration;
            return true;
        }
    }
}
