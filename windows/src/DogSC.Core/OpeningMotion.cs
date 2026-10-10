namespace DogSC.Core;

public readonly record struct OpeningPose(double Opacity, double Scale, double X, double Y, double RotationX, double RotationY, double Perspective)
{
    public static OpeningPose Identity => new(1, 1, 0, 0, 0, 0, 0);
}

public static class OpeningMotion
{
    public static bool IsNative(CanvasSettings canvas) => canvas.Opening is "converge" or "sideSlide" or "light3D";

    public static string[] ActiveElements(ProjectDocument project) => project.Canvas.OpeningOrder
        .Where(e => project.Canvas.OpeningIncluded.Contains(e)
            && (e == "screen" || e == "camera" && project.HasCamera && !project.Camera.Hidden
                || e == "stickers" && project.Canvas.Stickers.Any(s => s.Timing.Start <= .001)))
        .Distinct().ToArray();

    public static bool SuppressesStickerEntry(ProjectDocument project) => IsNative(project.Canvas)
        && ActiveElements(project).Contains("stickers");

    public static OpeningPose Sample(ProjectDocument project, string element, double time, double width, double height)
    {
        var canvas = project.Canvas;
        if (!IsNative(canvas) || time < 0 || time >= canvas.OpeningDuration) return OpeningPose.Identity;
        var active = ActiveElements(project);
        var index = Array.IndexOf(active, element);
        if (index < 0) return OpeningPose.Identity;
        var stagger = Math.Min(canvas.OpeningInterval, Math.Max(0, canvas.OpeningDuration - .18) / Math.Max(1, active.Length - 1));
        var duration = Math.Max(.18, canvas.OpeningDuration - stagger * Math.Max(0, active.Length - 1));
        var linear = Math.Clamp((time - index * stagger) / duration, 0, 1);
        var curve = canvas.OpeningTiming == "clear" ? "swift" : canvas.OpeningTiming;
        var eased = StickerMotion.CurveProgress(linear, curve);
        var opacity = StickerMotion.CurveProgress(Math.Min(linear * 1.65, 1), curve);
        var remaining = 1 - eased;
        var rotationRemaining = Math.Pow(1 - linear, 1.5);
        var overshoot = Math.Clamp(1 + 1.35 * Math.Pow(linear - 1, 3) + .35 * Math.Pow(linear - 1, 2), 0, 1.04);
        var (scale, x, y) = (canvas.Opening, element) switch
        {
            ("converge", "screen") => (.72, -1.05, -.62),
            ("converge", "camera") => (.58, 1.12, .72),
            ("converge", _) => (.62, -1.30, -.70),
            ("sideSlide", "screen") => (.94, -1.12, 0d),
            ("sideSlide", "camera") => (.88, 1.15, 0d),
            ("sideSlide", _) => (.90, -1.35, 0d),
            (_, "screen") => (.80, -1.05, .08),
            (_, "camera") => (.72, 1.10, .16),
            _ => (.76, -1.30, -.12),
        };
        var tilt = canvas.Opening == "light3D" && element == "screen";
        return new(opacity, scale + (overshoot - scale) * eased, x * width * remaining, y * height * remaining,
            tilt ? 9 * rotationRemaining : 0, tilt ? -22 * rotationRemaining : 0, tilt ? .72 * rotationRemaining : 0);
    }

    public static MotionExport.CameraFrame CameraFrame(MotionExport.CameraFrame frame, OpeningPose pose) => new(
        frame.X + frame.W * (1 - pose.Scale) / 2 + pose.X,
        frame.Y + frame.H * (1 - pose.Scale) / 2 + pose.Y,
        frame.W * pose.Scale, frame.H * pose.Scale, frame.Opacity * pose.Opacity);
}
