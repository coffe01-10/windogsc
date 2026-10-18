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
        if (start >= oldStart && start + duration <= oldEnd && duration < source.Timing.Duration)
            return TimelineShortcutEdits.TrimMotionRange(project, id, start, start + duration);
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
        foreach (var id in project.ScreenMotions.Select(c => c.Id).Concat(project.CameraMotions.Select(c => c.Id)))
            TimelineShortcutEdits.PreserveMotionEntry(project, id);
        var rightGroups = new Dictionary<string, string>();
        Cut(project.ScreenMotions, c => c.Timing, (c, t) => c.Timing = t,
            (c, id) => c.Id = id, c => c.GroupId, (c, group) => c.GroupId = group);
        Cut(project.CameraMotions, c => c.Timing, (c, t) => c.Timing = t,
            (c, id) => c.Id = id, c => c.GroupId, (c, group) => c.GroupId = group);

        void Cut<T>(List<T> clips, Func<T, TransitionTiming> timingOf, Action<T, TransitionTiming> setTiming,
            Action<T, string> setId, Func<T, string?> groupOf, Action<T, string> setGroup) where T : class
        {
            var kept = new List<T>();
            foreach (var clip in clips)
            {
                var timing = timingOf(clip);
                var effectEnd = timing.EndTime + (timing.PreferredReturnDuration ?? timing.ReturnDuration);
                if (effectEnd <= start) { kept.Add(clip); continue; }
                if (timing.StartTime >= end) { timing.StartTime -= end - start; kept.Add(clip); continue; }
                var hasLeft = timing.StartTime < start;
                if (hasLeft)
                {
                    var left = ScenePreset.Clone(clip);
                    setTiming(left, Slice(timing, timing.StartTime, start)); kept.Add(left);
                }
                if (effectEnd > end)
                {
                    var right = ScenePreset.Clone(clip);
                    var slice = Slice(timing, end, effectEnd); slice.StartTime -= end - start;
                    if (hasLeft)
                    {
                        setId(right, Guid.NewGuid().ToString("N")); slice.Id = Guid.NewGuid().ToString("N");
                        if (groupOf(clip) is { } group)
                        {
                            if (!rightGroups.TryGetValue(group, out var newGroup)) rightGroups[group] = newGroup = Guid.NewGuid().ToString("N");
                            setGroup(right, newGroup);
                        }
                    }
                    setTiming(right, slice); kept.Add(right);
                }
            }
            clips.Clear(); clips.AddRange(kept);
        }
    }

    static TransitionTiming Slice(TransitionTiming original, double start, double end)
    {
        var timing = ScenePreset.Clone(original);
        var lead = Math.Min(original.LeadInDuration, original.Duration);
        double Entry(double t) => lead <= .0001 ? original.LeadInProgressEnd
            : original.LeadInProgressOffset + (original.LeadInProgressEnd - original.LeadInProgressOffset)
                * Math.Clamp((t - original.StartTime) / lead, 0, 1);
        var returning = original.PreferredReturnDuration ?? original.ReturnDuration;
        double Return(double t) => returning <= .0001 ? original.ReturnProgressEnd
            : original.ReturnProgressOffset + (original.ReturnProgressEnd - original.ReturnProgressOffset)
                * Math.Clamp((t - original.EndTime) / returning, 0, 1);
        timing.StartTime = start; timing.Duration = Math.Max(0, Math.Min(end, original.EndTime) - start);
        timing.LeadInDuration = Math.Max(0, Math.Min(end, original.StartTime + lead) - start);
        timing.LeadInProgressOffset = Entry(start); timing.LeadInProgressEnd = Entry(end);
        timing.ReturnDuration = Math.Max(0, end - Math.Max(start, original.EndTime));
        timing.ReturnProgressOffset = Return(Math.Max(start, original.EndTime)); timing.ReturnProgressEnd = Return(end);
        timing.PreferredLeadInDuration = timing.LeadInDuration; timing.PreferredReturnDuration = timing.ReturnDuration;
        return timing;
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
