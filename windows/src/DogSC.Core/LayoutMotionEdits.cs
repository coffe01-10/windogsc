namespace DogSC.Core;

/// <summary>Camera layout commands; paired screen/camera clips share one authored window.</summary>
public static class LayoutMotionEdits
{
    public static CameraMotionClip? Apply(ProjectDocument project, double time, CameraMotionState cameraTarget,
        ScreenMotionState? screenTarget = null)
    {
        var camera = project.CameraMotions.FirstOrDefault(c => time >= c.Timing.StartTime && time < c.Timing.EndTime);
        var screen = screenTarget is null ? null
            : project.ScreenMotions.FirstOrDefault(c => time >= c.Timing.StartTime && time < c.Timing.EndTime);
        var group = screenTarget is null ? null : camera?.GroupId ?? screen?.GroupId ?? Guid.NewGuid().ToString("N");
        var timing = camera?.Timing ?? (screen is null ? null : ScenePreset.Clone(screen.Timing));
        if (camera is not null && screen is not null)
        {
            // Both clips contain the playhead. Their common window fits both
            // tracks without extending either into an unrelated neighbour.
            timing = ScenePreset.Clone(camera.Timing);
            var end = Math.Min(camera.Timing.EndTime, screen.Timing.EndTime);
            timing.StartTime = Math.Max(camera.Timing.StartTime, screen.Timing.StartTime);
            timing.Duration = end - timing.StartTime;
            timing.LeadInDuration = Math.Min(timing.LeadInDuration, timing.Duration);
        }
        if (timing is null)
        {
            var among = project.CameraMotions.Select(c => c.Timing)
                .Concat(screenTarget is null ? [] : project.ScreenMotions.Select(c => c.Timing));
            timing = Fit(project, time, among);
            if (timing is null) return null;
        }
        if (camera is null)
        {
            camera = new() { Timing = timing };
            project.CameraMotions.Add(camera);
        }
        else camera.Timing = timing;
        camera!.Target = ScenePreset.Clone(cameraTarget); camera.GroupId = group;
        if (screenTarget is not null)
        {
            if (screen is null)
            {
                screen = new() { Timing = ScenePreset.Clone(timing) };
                project.ScreenMotions.Add(screen);
            }
            else screen.Timing = ScenePreset.Clone(timing);
            screen.Target = ScenePreset.Clone(screenTarget); screen.GroupId = group;
        }
        project.ScreenMotions.Sort((a, b) => a.Timing.StartTime.CompareTo(b.Timing.StartTime));
        project.CameraMotions.Sort((a, b) => a.Timing.StartTime.CompareTo(b.Timing.StartTime));
        return camera;
    }

    static TransitionTiming? Fit(ProjectDocument project, double time, IEnumerable<TransitionTiming> among)
    {
        var duration = TimelineMath.Duration(project);
        time = Math.Clamp(time, 0, duration);
        var ordered = among.OrderBy(t => t.StartTime).ToArray();
        var previous = ordered.LastOrDefault(t => t.StartTime <= time + .0001);
        var next = ordered.FirstOrDefault(t => t.StartTime > time + .0001);
        var lower = previous?.EndTime ?? 0;
        var transition = project.Motion.DefaultZoomTransitionDuration;
        var clear = previous is null ? 0 : previous.EndTime + (previous.ReturnDuration > .0001 ? previous.ReturnDuration : transition);
        var upper = next?.StartTime ?? duration;
        var start = Math.Clamp(time, Math.Min(lower, upper), upper);
        if (start < clear) start = lower;
        var length = Math.Min(1.2, Math.Max(duration * .15, .5));
        var end = Math.Min(start + length, upper);
        if (start + length > upper) start = Math.Max(lower, end - length);
        // A real neighbour can leave less than the editor's minimum clip width.
        if (end - start < .16 - .0001) return null;
        return new() { StartTime = start, Duration = end - start, LeadInDuration = Math.Min(transition, end - start),
            ReturnDuration = Math.Min(transition, upper - end), PreferredLeadInDuration = transition,
            PreferredReturnDuration = transition, Easing = project.Motion.DefaultZoomEasing };
    }
}
