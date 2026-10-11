namespace DogSC.Core;

/// <summary>Ripple editing maps motion entry, hold and return boundaries together.</summary>
public static class MotionTimingEdits
{
    /// <summary>Move/resize a motion and its linked layout partners by the same boundary deltas.</summary>
    public static bool SetRange(ProjectDocument project, string id, double start, double duration)
    {
        var members = project.ScreenMotions.Select(c => (c.Id, c.GroupId, c.Timing, Screen: true))
            .Concat(project.CameraMotions.Select(c => (c.Id, c.GroupId, c.Timing, Screen: false))).ToArray();
        var source = members.FirstOrDefault(c => c.Id == id);
        if (source.Timing is null) return false;
        var oldStart = source.Timing.StartTime; var oldEnd = source.Timing.EndTime;
        source.Timing.StartTime = Math.Max(0, start);
        source.Timing.Duration = Math.Max(.05, duration);
        source.Timing.LeadInDuration = Math.Min(source.Timing.LeadInDuration, source.Timing.Duration);
        if (string.IsNullOrEmpty(source.GroupId)) return true;
        var deltaStart = source.Timing.StartTime - oldStart;
        var deltaEnd = source.Timing.EndTime - oldEnd;
        var timelineEnd = TimelineMath.Duration(project);
        // Snapshot all partners before changing any of them. Split layouts can
        // have several members on one track; evaluating mutated neighbours drifts.
        var origins = members.Where(c => c.Id != id && c.GroupId == source.GroupId)
            .Select(c => (Member: c, Start: c.Timing.StartTime, End: c.Timing.EndTime)).ToArray();
        foreach (var origin in origins)
        {
            var member = origin.Member; var timing = member.Timing;
            var sameTrack = members.Where(c => c.Screen == member.Screen && c.Id != member.Id).ToArray();
            (double Start, double End) Original((string Id, string? GroupId, TransitionTiming Timing, bool Screen) c)
            {
                if (c.Id == id) return (oldStart, oldEnd);
                var snapshot = origins.FirstOrDefault(o => o.Member.Id == c.Id);
                return snapshot.Member.Timing is null ? (c.Timing.StartTime, c.Timing.EndTime) : (snapshot.Start, snapshot.End);
            }
            var lower = sameTrack.Select(Original).Where(c => c.End <= origin.Start + .0001)
                .Select(c => c.End).DefaultIfEmpty(0).Max();
            var upper = sameTrack.Select(Original).Where(c => c.Start >= origin.End - .0001)
                .Select(c => c.Start).DefaultIfEmpty(timelineEnd).Min();
            // A partner stops at its own neighbouring clip, as in the Mac editor.
            var movedStart = Math.Clamp(origin.Start + deltaStart, lower, Math.Max(lower, upper - (origin.End - origin.Start)));
            var movedEnd = Math.Clamp(origin.End + deltaEnd, movedStart + .05, Math.Max(movedStart + .05, upper));
            timing.StartTime = movedStart; timing.Duration = movedEnd - movedStart;
            timing.LeadInDuration = Math.Min(timing.LeadInDuration, timing.Duration);
        }
        return true;
    }

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
