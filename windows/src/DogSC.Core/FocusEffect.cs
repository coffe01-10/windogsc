namespace DogSC.Core;

// Coordinates and geometry match RecorderCore/FocusEffect.swift.
public sealed class FocusEffect
{
    public string Shape { get; set; } = "radial";
    public string Target { get; set; } = "animation";
    public NormalizedPoint Center { get; set; } = new(.5, .5);
    public double Size { get; set; } = .45;
    public double Blur { get; set; } = .35;
    public double Softness { get; set; } = .5;
    public double Dimming { get; set; } = .18;
    public double AngleDegrees { get; set; }

    public static FocusEffect Linear() => new() { Shape = "linear", Target = "fixed", Size = .32, Dimming = 0 };
    public double BlurRadius(double canvasWidth, double canvasHeight) => Blur * 28 * Math.Min(canvasWidth, canvasHeight) / 1080;

    public double Outside(double x, double y, double centerX, double centerY, double shortEdge)
    {
        var dx = x - centerX; var dy = y - centerY;
        var radius = shortEdge * Size / 2;
        var angle = AngleDegrees * Math.PI / 180;
        var distance = Shape == "linear" ? Math.Abs(-dx * Math.Sin(angle) + dy * Math.Cos(angle)) : Math.Sqrt(dx * dx + dy * dy);
        var feather = Shape == "linear" ? Math.Max(shortEdge * Softness * .4, 1) : Math.Max(radius * Softness * 1.5, .5);
        return Math.Clamp((distance - radius) / feather, 0, 1);
    }
}

public sealed record FocusScene(string Id, FocusEffect Effect, NormalizedPoint Center, double Progress);

public sealed class FocusTrack
{
    readonly ProjectDocument _project;
    readonly CursorTimeline _cursor;
    public FocusTrack(ProjectDocument project) { _project = project; _cursor = new(project); }
    public static FocusEffect? EffectOf(ScreenMotionClip clip) => clip.FocusEffect ?? (clip.LinearBlur ? new FocusEffect
    {
        Shape = "linear", Target = "fixed", Size = clip.LinearBlurSize, Blur = clip.LinearBlurStrength,
        Softness = clip.LinearBlurSoftness, AngleDegrees = clip.LinearBlurAngle, Dimming = 0,
    } : null);

    public static IEnumerable<(string Id, FocusEffect Effect)> Effects(ProjectDocument project) =>
        project.Zooms.Where(z => z.FocusEffect is not null).Select(z => (z.Id, z.FocusEffect!))
            .Concat(project.ScreenMotions.Where(s => EffectOf(s) is not null).Select(s => (s.Id, EffectOf(s)!)));

    public FocusScene? At(double time, ZoomSpan? editingZoom = null, ScreenMotionClip? editingScreen = null, ZoomPose? poseOverride = null)
    {
        var screen = editingScreen ?? _project.ScreenMotions.OrderBy(s => s.Timing.StartTime).LastOrDefault(s =>
            time >= s.Timing.StartTime && (s.Timing.ReturnDuration == 0 || time < s.Timing.EffectEndTime));
        var zoom = editingZoom ?? _project.Zooms.LastOrDefault(z => time >= z.Start && time < z.Start + z.Duration);
        FocusEffect? effect;
        NormalizedPoint anchor;
        double progress;
        string id;
        var crop = _project.Canvas.Crop;
        if (screen is not null && EffectOf(screen) is { } screenEffect)
        {
            effect = screenEffect; id = screen.Id;
            anchor = new(crop.X + screen.Target.X * crop.Width, crop.Y + screen.Target.Y * crop.Height);
            progress = editingScreen is not null ? 1 : LinearFocus.Progress(screen, time);
        }
        else if (zoom?.FocusEffect is { } zoomEffect)
        {
            effect = zoomEffect; id = zoom.Id;
            var pose = poseOverride ?? _cursor.Motion.At(time);
            anchor = new(crop.X + crop.Width * (.5 / pose.Scale + pose.X * (1 - 1 / pose.Scale)),
                crop.Y + crop.Height * (.5 / pose.Scale + pose.Y * (1 - 1 / pose.Scale)));
            var (enter, exit) = ZoomMotion.Windows(zoom);
            var local = time - zoom.Start + zoom.AnimationOffset;
            var duration = zoom.AnimationDuration ?? zoom.Duration;
            progress = editingZoom is not null ? 1 : enter > 0 && local < enter ? ZoomMotion.Progress(zoom, local / enter)
                : exit > 0 && local > duration - exit ? 1 - ZoomMotion.Progress(zoom, (local - duration + exit) / exit) : 1;
        }
        else return null;
        if (progress <= .001 || (effect.Blur <= 0 && effect.Dimming <= 0)) return null;
        var center = effect.Target switch { "pointer" => _cursor.PositionAt(time) ?? anchor, "fixed" => effect.Center, _ => anchor };
        if (screen is { LinearBlur: true, FocusEffect: null }) center = new(crop.X + crop.Width / 2, crop.Y + crop.Height / 2);
        if (center.X < crop.X || center.X > crop.X + crop.Width || center.Y < crop.Y || center.Y > crop.Y + crop.Height) return null;
        return new(id, effect, center.Constrained(0), Math.Clamp(progress, 0, 1));
    }

    public ZoomPose PoseAt(double time) => _cursor.Motion.At(time);
}

// The same spatial mask is used by the live WPF preview and by every exported frame.
public sealed class FocusRaster
{
    readonly FocusScene _scene;
    readonly MotionExport.Homography _inverse;
    readonly FramePlacement _placement;
    readonly double _centerX, _centerY, _shortEdge;
    readonly bool _projected;

    public FocusRaster(ProjectDocument project, FocusScene scene, FramePlacement placement, int width, int height,
        double time, ZoomPose pose, ScreenMotionClip? editingScreen = null)
    {
        _scene = scene; _placement = placement;
        var crop = project.Canvas.Crop;
        _centerX = placement.X + ((scene.Center.X - crop.X) / crop.Width * pose.Scale - pose.X * (pose.Scale - 1)) * placement.Width;
        _centerY = placement.Y + ((scene.Center.Y - crop.Y) / crop.Height * pose.Scale - pose.Y * (pose.Scale - 1)) * placement.Height;
        _shortEdge = Math.Min(placement.Width, placement.Height) * pose.Scale;
        var viewport = new ScreenAnchorViewport(width, height, placement.Width, placement.Height,
            BaseCenterX: (placement.X + placement.Width / 2d) / width,
            BaseCenterY: (placement.Y + placement.Height / 2d) / height, BaseScale: project.Canvas.Scale);
        var track = new ScreenMotionTrack(project.ScreenMotions);
        var sample = editingScreen is null ? track.SampleLayout(time, null, viewport,
            project.Motion.ScreenSpringMass, project.Motion.ScreenSpringStiffness, project.Motion.ScreenSpringDamping)
            : ScreenMotionTrack.TargetLayout(editingScreen.Target, viewport);
        var corners = MotionExport.ScreenCorners(sample, track.SampleLayout(-1, null, viewport),
            width, height, placement.X, placement.Y, placement.Width, placement.Height,
            OpeningMotion.Sample(project, "screen", time, width, height));
        var canvas = new[] { (0d, 0d), ((double)width, 0d), (0d, (double)height), ((double)width, (double)height) };
        var inverse = MotionExport.Homography.FromQuad(corners, canvas);
        if (project.Canvas.TiltX != 0 || project.Canvas.TiltY != 0)
        {
            var rect = new[] { ((double)placement.X, (double)placement.Y), ((double)placement.X + placement.Width, (double)placement.Y),
                ((double)placement.X, (double)placement.Y + placement.Height), ((double)placement.X + placement.Width, (double)placement.Y + placement.Height) };
            var staticQuad = PerspectiveGeometry.Corners(placement.Width, placement.Height, project.Canvas.TiltX, project.Canvas.TiltY)
                .Select(p => (p.X + placement.X, p.Y + placement.Y)).ToArray();
            inverse = inverse.Then(MotionExport.Homography.FromQuad(staticQuad, rect));
        }
        _inverse = inverse;
        _projected = project.Canvas.TiltX != 0 || project.Canvas.TiltY != 0 || sample.State.RotationX != 0 || sample.State.RotationY != 0;
    }

    public double Coverage(double x, double y)
    {
        var p = _inverse.Apply(x, y);
        if (_projected && (p.X < _placement.X || p.Y < _placement.Y || p.X > _placement.X + _placement.Width || p.Y > _placement.Y + _placement.Height))
            return _scene.Progress;
        return _scene.Effect.Outside(p.X, p.Y, _centerX, _centerY, _shortEdge) * _scene.Progress;
    }

    public byte[] Gray(int rasterWidth, int rasterHeight, double width, double height)
    {
        var data = new byte[rasterWidth * rasterHeight];
        for (var y = 0; y < rasterHeight; y++)
            for (var x = 0; x < rasterWidth; x++)
                data[y * rasterWidth + x] = (byte)Math.Round(Coverage((x + .5) * width / rasterWidth, (y + .5) * height / rasterHeight) * 255);
        return data;
    }
}
