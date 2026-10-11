namespace DogSC.Core;

/// <summary>Camera aperture and source crop, before stickers are composited.</summary>
public readonly record struct CameraScene(MotionExport.CameraFrame Frame, double Radius, SourceCrop Content, bool Mirrored)
{
    public double BorderWidth { get; init; }
    public double ShadowStrength { get; init; }
    public double ShadowRadius { get; init; }
    public double ShadowOffsetY { get; init; }
    public (double X, double Y) SourcePoint(double x, double y)
    {
        var u = (x - Frame.X) / Frame.W;
        return (Content.X + (Mirrored ? 1 - u : u) * Content.Width,
            Content.Y + (y - Frame.Y) / Frame.H * Content.Height);
    }

    public double Coverage(double x, double y)
        => ShapeCoverage(x, y) * Frame.Opacity;

    public double ShapeCoverage(double x, double y, double outset = 0)
    {
        var dx = Math.Abs(x - Frame.X - Frame.W / 2) - (Frame.W / 2 - Radius);
        var dy = Math.Abs(y - Frame.Y - Frame.H / 2) - (Frame.H / 2 - Radius);
        var distance = Math.Sqrt(Math.Pow(Math.Max(dx, 0), 2) + Math.Pow(Math.Max(dy, 0), 2))
            + Math.Min(Math.Max(dx, dy), 0) - Radius - outset;
        return Math.Clamp(.5 - distance, 0, 1);
    }
}

public static class CameraContentFill
{
    // Same overscan as upstream CameraContentFill; avoids seams at fractional apertures.
    public const double EdgeOverscan = 1.01;

    public static SourceCrop Visible(double sourceAspect, double targetAspect, CameraBubble camera)
    {
        var zoom = camera.ContentScale * EdgeOverscan;
        var w = Math.Min(1, targetAspect / sourceAspect) / zoom;
        var h = Math.Min(1, sourceAspect / targetAspect) / zoom;
        return new() { X = (1 - w) * (camera.Mirrored ? 1 - camera.ContentX : camera.ContentX),
            Y = (1 - h) * camera.ContentY, Width = w, Height = h };
    }
}

/// <summary>Continuous camera layout, crop, corner radius and entrance pose.</summary>
public sealed class CameraSceneTrack(ProjectDocument project, int width, int height, double sourceAspect, ZoomMotion? zoomMotion = null)
{
    readonly CameraMotionTrack _motion = new(project.CameraMotions);
    readonly ZoomMotion _zoom = zoomMotion ?? new(project);
    CameraMotionState Base => new()
    {
        Layout = project.Camera.Shape is "circle" or "horizontal" or "vertical" or "original" ? project.Camera.Shape : "square",
        Docked = project.Camera.Docked,
        X = project.Camera.CenterX, Y = project.Camera.CenterY, Size = project.Camera.Size,
        Roundness = project.Camera.Shape == "none" ? 0 : project.Camera.Corner * 2, Opacity = project.Camera.Hidden ? 0 : 1,
    };

    public CameraScene LayoutAt(double time, CameraMotionState? selectedTarget = null, double? screenZoomScale = null)
    {
        MotionExport.CameraFrame frame;
        double radius; var fullscreen = 0d;
        if (project.CameraMotions.Count > 0 || project.Camera.Docked || project.Camera.ScaleDuringZoom != 1 || selectedTarget is not null)
        {
            var sample = selectedTarget is not null ? CameraMotionTrack.Resolve(selectedTarget, sourceAspect, width / (double)height, project.Camera.Docked)
                : _motion.Sample(time, Base, sourceAspect, width / (double)height,
                project.Motion.ScreenSpringMass, project.Motion.ScreenSpringStiffness, project.Motion.ScreenSpringDamping);
            frame = MotionExport.CameraPixels(sample, width, height, project.Camera, screenZoomScale ?? _zoom.At(time).Scale);
            radius = Math.Min(frame.W, frame.H) * sample.CornerFraction;
            fullscreen = Math.Clamp(sample.FullscreenProgress, 0, 1);
        }
        else
        {
            var bubble = CanvasLayout.Bubble(width, height, project.Camera, sourceAspect);
            frame = new(bubble.X, bubble.Y, bubble.Width, bubble.Height, project.Camera.Hidden ? 0 : 1);
            radius = Math.Min(frame.W, frame.H) * (project.Camera.Shape == "circle" ? .5
                : project.Camera.Shape == "none" ? 0 : project.Camera.Corner);
        }
        if (project.Camera.Docked && project.Camera.Hidden && selectedTarget is null) frame = frame with { Opacity = 0 };
        var styleScale = Math.Min(width, height) / 1080d;
        return new(frame, radius, CameraContentFill.Visible(sourceAspect, frame.W / frame.H, project.Camera), project.Camera.Mirrored)
        {
            BorderWidth = project.Camera.BorderWidth * styleScale * (1 - fullscreen),
            ShadowStrength = project.Camera.ShadowStrength, ShadowRadius = 14 * styleScale, ShadowOffsetY = 8 * styleScale,
        };
    }

    public CameraScene At(double time, double entryStart, double entryEnd)
    {
        var layout = LayoutAt(time); var frame = layout.Frame; var radius = layout.Radius;
        var into = Math.Clamp((time - entryStart) / .5, 0, 1);
        var entryOpacity = project.Camera.Entry is "fade" or "rise"
            ? Math.Min(into, Math.Clamp((entryEnd - time) / .5, 0, 1)) : 1;
        var dx = 0d;
        var dy = project.Camera.Entry == "rise" ? (1 - into) * frame.H * .4 : 0;
        if (project.Canvas.Opening is "slide" or "sideslide" or "lift")
        {
            var progress = CanvasLayout.OpeningProgress(project.Canvas, time, 1);
            entryOpacity *= progress;
            if (project.Canvas.Opening == "sideslide") dx += (1 - progress) * frame.W * .4;
            else dy += (1 - progress) * frame.H * .4;
        }
        var opening = OpeningMotion.Sample(project, "camera", time, width, height);
        frame = OpeningMotion.CameraFrame(frame, opening);
        frame = frame with { X = frame.X + dx * opening.Scale, Y = frame.Y + dy * opening.Scale,
            Opacity = frame.Opacity * entryOpacity };
        radius *= opening.Scale;
        return layout with { Frame = frame, Radius = radius, BorderWidth = layout.BorderWidth * opening.Scale,
            Content = CameraContentFill.Visible(sourceAspect, frame.W / frame.H, project.Camera) };
    }
}
