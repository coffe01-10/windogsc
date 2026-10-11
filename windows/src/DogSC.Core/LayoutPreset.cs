namespace DogSC.Core;

/// <summary>A named camera/screen arrangement. Applying it authors a linked animation, not a static scene.</summary>
public sealed class LayoutPreset
{
    public string Name { get; set; } = "";
    public CameraMotionState Camera { get; set; } = new();
    public bool CameraDocked { get; set; }
    public ScreenMotionState Screen { get; set; } = new() { CanvasPlacement = true };

    public static LayoutPreset Capture(ProjectDocument project, string name) => new()
    {
        Name = name.Trim(), CameraDocked = project.Camera.Docked,
        Camera = new() { Layout = project.Camera.Shape == "none" ? "square" : project.Camera.Shape,
            X = project.Camera.CenterX, Y = project.Camera.CenterY, Size = project.Camera.Size,
            Roundness = project.Camera.Shape == "none" ? 0 : project.Camera.Corner * 2 },
        Screen = new() { CanvasPlacement = true, X = project.Canvas.CenterX, Y = project.Canvas.CenterY, Scale = project.Canvas.Scale },
    };

    public CameraMotionClip? Apply(ProjectDocument project, double time)
    {
        var target = ScenePreset.Clone(Camera);
        target.Docked = CameraDocked;
        return LayoutMotionEdits.Apply(project, time, target, Screen);
    }
}
