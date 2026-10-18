namespace DogSC.Core;

/// <summary>Selection-aware S/Q/W editing shared by the editor's buttons and keyboard.</summary>
public static class TimelineShortcutEdits
{
    public static string? SplitEffect(ProjectDocument project, string id, double time)
    {
        if (project.Zooms.FirstOrDefault(x => x.Id == id) is { } zoom)
        {
            var end = zoom.Start + zoom.Duration;
            if (time - zoom.Start < .16 || end - time < .16) return null;
            PreserveZoom(project, zoom);
            var right = ScenePreset.Clone(zoom); right.Id = Guid.NewGuid().ToString("N");
            right.AnimationOffset += time - zoom.Start;
            right.Start = time; right.Duration = end - time;
            zoom.Duration = time - zoom.Start;
            project.Zooms.Add(right); return right.Id;
        }
        if (MotionMembers(project).Any(x => x.Id == id)) return SplitMotion(project, id, time);
        if (project.Canvas.Stickers.FirstOrDefault(x => x.Id == id) is { } sticker)
        {
            var pair = SplitWindow(sticker.Timing, time, TimelineMath.Duration(project)); if (pair is null) return null;
            var right = ScenePreset.Clone(sticker); right.Id = Guid.NewGuid().ToString("N");
            sticker.Timing = pair.Value.Left; right.Timing = pair.Value.Right;
            project.Canvas.Stickers.Add(right); return right.Id;
        }
        if (project.Canvas.SoftRegions.FirstOrDefault(x => x.Id == id) is { } soft)
        {
            var pair = SplitWindow(soft.Timing, time, TimelineMath.Duration(project)); if (pair is null) return null;
            var right = ScenePreset.Clone(soft); right.Id = Guid.NewGuid().ToString("N");
            soft.Timing = pair.Value.Left; right.Timing = pair.Value.Right;
            project.Canvas.SoftRegions.Add(right); return right.Id;
        }
        return null;
    }

    static void PreserveZoom(ProjectDocument project, ZoomSpan zoom)
    {
        if (zoom.AnimationDuration is not null) return;
        var joined = project.Zooms.Any(x => x.Id != zoom.Id && Math.Abs(x.Start - zoom.Start - zoom.Duration) < .001);
        var (enter, exit) = ZoomMotion.Windows(zoom, joined);
        zoom.AnimationInitialPose = new ZoomMotion(project).InitialPose(zoom);
        zoom.AnimationDuration = zoom.Duration;
        zoom.EnterDuration = enter; zoom.ExitDuration = exit;
    }

    internal static void TrimZoomRange(ProjectDocument project, ZoomSpan zoom, double start, double end)
    {
        PreserveZoom(project, zoom);
        zoom.AnimationOffset += start - zoom.Start;
        zoom.Start = start; zoom.Duration = end - start;
    }

    static (TransitionTiming Left, TransitionTiming Right) SplitTiming(TransitionTiming timing, double time)
    {
        var left = ScenePreset.Clone(timing); var right = ScenePreset.Clone(timing);
        TrimTiming(left, timing.StartTime, time);
        left.ReturnDuration = 0; left.PreferredReturnDuration = 0; left.ReturnProgressOffset = 0;
        right.Id = Guid.NewGuid().ToString("N");
        TrimTiming(right, time, timing.EndTime);
        return (left, right);
    }

    static void TrimTiming(TransitionTiming timing, double start, double end)
    {
        var oldStart = timing.StartTime;
        var lead = Math.Min(timing.LeadInDuration, timing.Duration);
        var leadEnd = oldStart + lead;
        var p0 = timing.LeadInProgressOffset; var p1 = timing.LeadInProgressEnd;
        double Progress(double t) => lead <= .0001 ? p1 : p0 + (p1 - p0) * Math.Clamp((t - oldStart) / lead, 0, 1);
        timing.LeadInProgressOffset = Progress(start);
        timing.LeadInProgressEnd = Progress(end);
        timing.LeadInDuration = Math.Max(0, Math.Min(end, leadEnd) - start);
        timing.StartTime = start; timing.Duration = end - start;
        timing.PreferredLeadInDuration = timing.LeadInDuration;
    }

    static IEnumerable<(string Id, string? Group, TransitionTiming Timing)> MotionMembers(ProjectDocument project)
        => project.ScreenMotions.Select(x => (x.Id, x.GroupId, x.Timing))
            .Concat(project.CameraMotions.Select(x => (x.Id, x.GroupId, x.Timing)));

    internal static void PreserveMotionEntry(ProjectDocument project, string id)
    {
        if (project.ScreenMotions.FirstOrDefault(x => x.Id == id) is { } screen && !screen.Timing.PreserveEntry)
        {
            var previous = project.ScreenMotions.Where(x => x.Id != id && x.Timing.StartTime <= screen.Timing.StartTime)
                .OrderBy(x => x.Timing.StartTime).LastOrDefault();
            if (previous is not null && Inherits(previous.Timing, screen.Timing)) screen.EntryFrom = ScenePreset.Clone(previous.Target);
            screen.Timing.PreserveEntry = true;
        }
        if (project.CameraMotions.FirstOrDefault(x => x.Id == id) is { } camera && !camera.Timing.PreserveEntry)
        {
            var previous = project.CameraMotions.Where(x => x.Id != id && x.Timing.StartTime <= camera.Timing.StartTime)
                .OrderBy(x => x.Timing.StartTime).LastOrDefault();
            if (previous is not null && Inherits(previous.Timing, camera.Timing)) camera.EntryFrom = ScenePreset.Clone(previous.Target);
            camera.Timing.PreserveEntry = true;
        }
    }

    static bool Inherits(TransitionTiming previous, TransitionTiming current)
        => Math.Abs(previous.EndTime - current.StartTime) <= MotionEasing.AdjacencyTolerance
            || (previous.PreferredReturnDuration ?? previous.ReturnDuration) <= .0001;

    internal static bool TrimMotionRange(ProjectDocument project, string id, double start, double end)
    {
        var members = MotionMembers(project).ToArray(); var source = members.First(x => x.Id == id);
        var linked = members.Where(x => x.Id == id || source.Group is not null && x.Group == source.Group).ToArray();
        var deltaStart = start - source.Timing.StartTime; var deltaEnd = end - source.Timing.EndTime;
        if (linked.Any(x => x.Timing.Duration + deltaEnd - deltaStart < .05)) return false;
        foreach (var member in linked) PreserveMotionEntry(project, member.Id);
        foreach (var member in linked)
            TrimTiming(member.Timing, member.Timing.StartTime + deltaStart, member.Timing.EndTime + deltaEnd);
        return true;
    }

    internal static void RemoveZoomRange(ProjectDocument project, double start, double end)
    {
        var motion = new ZoomMotion(project);
        foreach (var zoom in project.Zooms)
        {
            if (zoom.AnimationDuration is not null) continue;
            var joined = project.Zooms.Any(x => x.Id != zoom.Id && Math.Abs(x.Start - zoom.Start - zoom.Duration) < .001);
            var windows = ZoomMotion.Windows(zoom, joined);
            zoom.AnimationInitialPose = motion.InitialPose(zoom);
            zoom.AnimationDuration = zoom.Duration;
            zoom.EnterDuration = windows.Enter; zoom.ExitDuration = windows.Exit;
        }
        var kept = new List<ZoomSpan>();
        foreach (var zoom in project.Zooms)
        {
            var oldStart = zoom.Start; var oldEnd = oldStart + zoom.Duration;
            if (oldEnd <= start) { kept.Add(zoom); continue; }
            if (oldStart >= end) { zoom.Start -= end - start; kept.Add(zoom); continue; }
            if (oldStart < start && start - oldStart >= .12)
            {
                var left = TimelineEdits.CopyZoom(zoom); left.Id = zoom.Id;
                left.Duration = start - oldStart; kept.Add(left);
            }
            if (oldEnd > end && oldEnd - end >= .12)
            {
                var right = TimelineEdits.CopyZoom(zoom);
                if (oldStart >= start) right.Id = zoom.Id;
                right.AnimationOffset += end - oldStart;
                right.Start = start; right.Duration = oldEnd - end; kept.Add(right);
            }
        }
        project.Zooms = kept;
    }

    static string? SplitMotion(ProjectDocument project, string id, double time)
    {
        var members = MotionMembers(project).ToArray(); var selected = members.First(x => x.Id == id);
        var linked = members.Where(x => x.Id == id || selected.Group is not null && x.Group == selected.Group
            && time > x.Timing.StartTime && time < x.Timing.EndTime).ToArray();
        if (linked.Any(x => time - x.Timing.StartTime < .05 || x.Timing.EndTime - time < .05)) return null;
        foreach (var member in linked) PreserveMotionEntry(project, member.Id);
        var leftGroup = selected.Group is null ? null : Guid.NewGuid().ToString("N");
        var rightGroup = selected.Group is null ? null : Guid.NewGuid().ToString("N");
        string? result = null;
        foreach (var member in linked)
        {
            var pair = SplitTiming(member.Timing, time);
            string rightId;
            if (project.ScreenMotions.FirstOrDefault(x => x.Id == member.Id) is { } screen)
            {
                var right = ScenePreset.Clone(screen); rightId = right.Id = Guid.NewGuid().ToString("N");
                screen.Timing = pair.Left; right.Timing = pair.Right;
                screen.GroupId = leftGroup; right.GroupId = rightGroup; project.ScreenMotions.Add(right);
            }
            else
            {
                var camera = project.CameraMotions.First(x => x.Id == member.Id);
                var right = ScenePreset.Clone(camera); rightId = right.Id = Guid.NewGuid().ToString("N");
                camera.Timing = pair.Left; right.Timing = pair.Right;
                camera.GroupId = leftGroup; right.GroupId = rightGroup; project.CameraMotions.Add(right);
            }
            if (member.Id == id) result = rightId;
        }
        return result;
    }

    static (EffectWindow Left, EffectWindow Right)? SplitWindow(EffectWindow timing, double time, double total)
    {
        var end = timing.Duration > 0 ? timing.Start + timing.Duration : total;
        if (time - timing.Start < .01 || end - time < .01) return null;
        var left = ScenePreset.Clone(timing); var right = ScenePreset.Clone(timing);
        left.Duration = time - timing.Start; left.FadeOut = 0;
        right.Start = time; right.Duration = end - time; right.FadeIn = 0;
        return (left, right);
    }

    public static bool TrimEffect(ProjectDocument project, string id, double time, bool before)
    {
        if (project.Zooms.FirstOrDefault(x => x.Id == id) is { } zoom)
        {
            var end = zoom.Start + zoom.Duration;
            if (time <= zoom.Start + .12 || time >= end - .12) return false;
            TrimZoomRange(project, zoom, before ? time : zoom.Start, before ? end : time);
            return true;
        }
        var motion = project.ScreenMotions.FirstOrDefault(x => x.Id == id)?.Timing
            ?? project.CameraMotions.FirstOrDefault(x => x.Id == id)?.Timing;
        if (motion is not null)
        {
            var end = motion.EndTime;
            if (time <= motion.StartTime + .05 || time >= end - .05) return false;
            return TrimMotionRange(project, id, before ? time : motion.StartTime, before ? end : time);
        }
        var window = project.Canvas.Stickers.FirstOrDefault(x => x.Id == id)?.Timing
            ?? project.Canvas.SoftRegions.FirstOrDefault(x => x.Id == id)?.Timing;
        if (window is null) return false;
        var windowEnd = window.Duration > 0 ? window.Start + window.Duration : TimelineMath.Duration(project);
        if (time <= window.Start + .01 || time >= windowEnd - .01) return false;
        window.Duration = before ? windowEnd - time : time - window.Start;
        if (before) window.Start = time;
        return true;
    }
}
