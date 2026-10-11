using System.Text.Json;

namespace DogSC.Core;

public sealed class ScenePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "我的场景";
    public CanvasSettings Canvas { get; set; } = new();
    public CameraBubble Camera { get; set; } = new();
    public SceneAudio? Audio { get; set; }
    public MotionDefaults? Motion { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public bool IncludesCanvas { get; set; } = true;
    public bool IncludesCamera { get; set; } = true;
    public bool IncludesOpening { get; set; } = true;
    public bool IncludesCursorBackdrop { get; set; } = true;
    public bool IncludesCrop { get; set; } = true;
    public bool IncludesBackground { get; set; } = true;

    public bool NeedsCropPreview(ProjectDocument target) => IncludesCanvas && IncludesCrop
        && SourceWidth > 0 && SourceHeight > 0
        && (SourceWidth != target.Width || SourceHeight != target.Height)
        && (Canvas.Crop.X != 0 || Canvas.Crop.Y != 0 || Canvas.Crop.Width != 1 || Canvas.Crop.Height != 1);

    public static ScenePreset Capture(ProjectDocument project, string name, string? id = null, double? zoomCreationScale = null)
    {
        var preset = new ScenePreset
        {
            Name = name.Trim(), Canvas = Clone(project.Canvas), Camera = Clone(project.Camera),
            SourceWidth = project.Width, SourceHeight = project.Height, Motion = Clone(project.Motion),
            Audio = new SceneAudio { MicrophoneGain = project.MicrophoneGain, SystemGain = project.SystemGain, MicrophoneEnabled = project.MicrophoneEnabled, SystemEnabled = project.SystemEnabled }
        };
        if (id is not null) preset.Id = id;
        preset.Motion!.DefaultZoomScale ??= zoomCreationScale;
        preset.Canvas.Stickers.Clear(); preset.Canvas.SoftRegions.Clear();
        return preset;
    }

    public static ScenePreset CaptureLastUsed(ProjectDocument project)
    {
        var snapshot = Capture(project, "上次使用");
        snapshot.IncludesCrop = false;
        snapshot.SourceWidth = snapshot.SourceHeight = 0;
        var wallpaper = snapshot.Canvas.Wallpaper;
        // Project media stays in its original project. Patterns and standalone
        // system images can travel; availability is resolved by the application.
        snapshot.IncludesBackground = string.IsNullOrWhiteSpace(wallpaper)
            || wallpaper.StartsWith("pattern:") || wallpaper.StartsWith("flow:") || Path.IsPathRooted(wallpaper);
        return snapshot;
    }

    public bool MatchesConfiguration(ProjectDocument project, double? zoomCreationScale = null)
    {
        var current = Capture(project, Name, zoomCreationScale: zoomCreationScale);
        var canvas = Clone(Canvas); var camera = Clone(Camera);
        canvas.Cursor.TimingAdvance = current.Canvas.Cursor.TimingAdvance = 0;
        camera.TimingAdvance = current.Camera.TimingAdvance = 0;
        return JsonSerializer.Serialize(new { Canvas = canvas, Camera = camera, Audio, Motion }, ProjectSchema.Json)
            == JsonSerializer.Serialize(new { current.Canvas, current.Camera, current.Audio, current.Motion }, ProjectSchema.Json);
    }

    public void Apply(ProjectDocument project)
    {
        var current = project.Canvas;
        var canvas = IncludesCanvas ? Clone(Canvas) : Clone(current);
        if (!IncludesCrop) canvas.Crop = Clone(current.Crop);
        // Presets describe the scene, while timed overlays belong to this edit.
        canvas.Stickers = current.Stickers;
        canvas.SoftRegions = current.SoftRegions;
        var opening = IncludesOpening ? Canvas : current;
        canvas.Intro = opening.Intro;
        canvas.TiltX = opening.TiltX;
        canvas.TiltY = opening.TiltY;
        canvas.Opening = opening.Opening;
        canvas.OpeningTiming = opening.OpeningTiming;
        canvas.OpeningDuration = opening.OpeningDuration;
        canvas.OpeningInterval = opening.OpeningInterval;
        canvas.OpeningOrder = [.. opening.OpeningOrder];
        canvas.OpeningIncluded = [.. opening.OpeningIncluded];
        var backdrop = IncludesCursorBackdrop ? Canvas : current;
        canvas.Cursor = Clone(backdrop.Cursor);
        canvas.Cursor.TimingAdvance = current.Cursor.TimingAdvance;
        canvas.Background = IncludesBackground ? backdrop.Background : current.Background;
        canvas.Wallpaper = IncludesBackground ? backdrop.Wallpaper : current.Wallpaper;
        canvas.WallpaperBlur = backdrop.WallpaperBlur;
        canvas.PatternScale = backdrop.PatternScale;
        canvas.PatternOpacity = backdrop.PatternOpacity;
        canvas.FocusBlur = backdrop.FocusBlur;
        project.Canvas = canvas;
        if (IncludesOpening && Motion is not null)
        {
            var scale = Motion.DefaultZoomScale ?? project.Motion.DefaultZoomScale;
            project.Motion = Clone(Motion); project.Motion.DefaultZoomScale = scale;
        }
        if (IncludesCamera)
        {
            var advance = project.Camera.TimingAdvance;
            var cameraHidden = project.Camera.Hidden;
            project.Camera = Clone(Camera);
            project.Camera.TimingAdvance = advance;
            if (!IncludesCrop) project.Camera.Hidden = cameraHidden;
        }
        if (Audio is { } audio)
        {
            project.MicrophoneGain = audio.MicrophoneGain; project.SystemGain = audio.SystemGain;
            project.MicrophoneEnabled = audio.MicrophoneEnabled; project.SystemEnabled = audio.SystemEnabled;
        }
    }

    public static T Clone<T>(T source) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, ProjectSchema.Json), ProjectSchema.Json)!;
}

public sealed class SceneAudio
{
    public double MicrophoneGain { get; set; } = 1;
    public double SystemGain { get; set; } = 1;
    public bool MicrophoneEnabled { get; set; } = true;
    public bool SystemEnabled { get; set; } = true;
}
