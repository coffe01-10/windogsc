using System.Text.Json;

namespace DogSC.Core;

public sealed class ScenePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "我的场景";
    public CanvasSettings Canvas { get; set; } = new();
    public CameraBubble Camera { get; set; } = new();
    public bool IncludesCanvas { get; set; } = true;
    public bool IncludesCamera { get; set; } = true;
    public bool IncludesOpening { get; set; } = true;
    public bool IncludesCursorBackdrop { get; set; } = true;

    public static ScenePreset Capture(ProjectDocument project, string name) => new()
    {
        Name = name.Trim(), Canvas = Clone(project.Canvas), Camera = Clone(project.Camera)
    };

    public void Apply(ProjectDocument project)
    {
        var current = project.Canvas;
        var canvas = IncludesCanvas ? Clone(Canvas) : Clone(current);
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
        canvas.Background = backdrop.Background;
        canvas.Wallpaper = backdrop.Wallpaper;
        canvas.WallpaperBlur = backdrop.WallpaperBlur;
        canvas.PatternScale = backdrop.PatternScale;
        canvas.PatternOpacity = backdrop.PatternOpacity;
        canvas.FocusBlur = backdrop.FocusBlur;
        project.Canvas = canvas;
        if (IncludesCamera)
        {
            var advance = project.Camera.TimingAdvance;
            project.Camera = Clone(Camera);
            project.Camera.TimingAdvance = advance;
        }
    }

    public static T Clone<T>(T source) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, ProjectSchema.Json), ProjectSchema.Json)!;
}
