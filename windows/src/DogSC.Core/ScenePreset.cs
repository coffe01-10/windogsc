using System.Text.Json;

namespace DogSC.Core;

public sealed class ScenePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "我的场景";
    public CanvasSettings Canvas { get; set; } = new();
    public CameraBubble Camera { get; set; } = new();

    public static ScenePreset Capture(ProjectDocument project, string name) => new()
    {
        Name = name.Trim(), Canvas = Clone(project.Canvas), Camera = Clone(project.Camera)
    };

    public void Apply(ProjectDocument project)
    {
        project.Canvas = Clone(Canvas);
        project.Camera = Clone(Camera);
    }

    public static T Clone<T>(T source) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, ProjectSchema.Json), ProjectSchema.Json)!;
}
