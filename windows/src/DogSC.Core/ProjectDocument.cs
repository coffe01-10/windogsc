using System.Text.Json;
using System.Text.Json.Serialization;

namespace DogSC.Core;

public static class ProjectSchema
{
    public const int Current = 1;
    public const string Platform = "windows";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// A DogSC Windows project directory. It is not interchangeable with a macOS
/// <c>.dogscproject</c> package: the media layout and schema differ.
/// </summary>
public sealed class ProjectDocument
{
    public int Schema { get; set; } = ProjectSchema.Current;
    public string Platform { get; set; } = ProjectSchema.Platform;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "未命名";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public int Width { get; set; }
    public int Height { get; set; }
    public int FrameRate { get; set; } = 30;
    public string? SourceLabel { get; set; }
    public List<MediaAsset> Assets { get; set; } = [];
    public List<ClipSegment> Clips { get; set; } = [];
    public List<ZoomSpan> Zooms { get; set; } = [];
    public List<SourceMarker> Markers { get; set; } = [];
    public CanvasSettings Canvas { get; set; } = new();
    public CameraBubble Camera { get; set; } = new();
    public double MicrophoneGain { get; set; } = 1;
    public double SystemGain { get; set; } = 1;

    public MediaAsset? Asset(string id) => Assets.FirstOrDefault(asset => asset.Id == id);

    public bool HasCamera => Assets.Any(asset => !string.IsNullOrWhiteSpace(asset.Camera));
}

public sealed class MediaAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Video { get; set; } = "";
    public string? Microphone { get; set; }
    public string? SystemAudio { get; set; }
    public string? Camera { get; set; }
    public double Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class ClipSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AssetId { get; set; } = "";
    public double SourceStart { get; set; }
    public double SourceEnd { get; set; }
    public double Speed { get; set; } = 1;
}

public sealed class ZoomSpan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double Start { get; set; }
    public double Duration { get; set; } = 2;
    public double Scale { get; set; } = 1.45;
    public double FocusX { get; set; } = 0.5;
    public double FocusY { get; set; } = 0.5;
}

public sealed class SourceMarker
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AssetId { get; set; } = "";
    public double SourceTime { get; set; }
    public string? Note { get; set; }
}

public sealed class CanvasSettings
{
    public string Aspect { get; set; } = "source";
    public string Background { get; set; } = "#F3F1EC";
    public double Padding { get; set; } = 72;
    public double CenterX { get; set; } = 0.5;
    public double CenterY { get; set; } = 0.5;
    public double Scale { get; set; } = 1;
    public double Corner { get; set; } = 18;
    public double Shadow { get; set; } = 28;
}

public sealed class CameraBubble
{
    public double CenterX { get; set; } = 0.84;
    public double CenterY { get; set; } = 0.78;
    public double Size { get; set; } = 0.2;
}

public static class ProjectFile
{
    public static string Media(string projectDirectory, string relative) =>
        Path.Combine(projectDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

    public static void Save(string projectDirectory, ProjectDocument project)
    {
        Directory.CreateDirectory(projectDirectory);
        Normalize(project);
        var json = JsonSerializer.Serialize(project, ProjectSchema.Json);
        var path = Path.Combine(projectDirectory, "project.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static ProjectDocument Load(string projectDirectory)
    {
        var path = Path.Combine(projectDirectory, "project.json");
        if (!File.Exists(path))
        {
            throw new InvalidDataException("这个文件夹里没有 DogSC 项目。");
        }

        var project = JsonSerializer.Deserialize<ProjectDocument>(File.ReadAllText(path), ProjectSchema.Json)
            ?? throw new InvalidDataException("项目文件是空的。");
        if (!string.Equals(project.Platform, ProjectSchema.Platform, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("这是 macOS 版 DogSC 项目，Windows 版还不能打开它。");
        }

        if (project.Schema > ProjectSchema.Current)
        {
            throw new InvalidDataException($"项目版本 {project.Schema} 来自更新的 DogSC，当前版本只支持到 {ProjectSchema.Current}。");
        }

        Normalize(project);
        if (project.Clips.Count == 0 || project.Assets.Count == 0)
        {
            throw new InvalidDataException("项目里没有可播放的画面。");
        }

        return project;
    }

    public static void Normalize(ProjectDocument project)
    {
        project.Assets ??= [];
        project.Clips ??= [];
        project.Zooms ??= [];
        project.Markers ??= [];
        project.Canvas ??= new CanvasSettings();
        project.Camera ??= new CameraBubble();
        project.Canvas.Background = NormalizeColor(project.Canvas.Background);
        project.Canvas.Scale = Math.Clamp(project.Canvas.Scale, 0.25, 2.5);
        project.Canvas.Padding = Math.Clamp(project.Canvas.Padding, 0, 240);
        project.Canvas.Corner = Math.Clamp(project.Canvas.Corner, 0, 240);
        project.Canvas.Shadow = Math.Clamp(project.Canvas.Shadow, 0, 100);
        project.Canvas.CenterX = Math.Clamp(project.Canvas.CenterX, 0, 1);
        project.Canvas.CenterY = Math.Clamp(project.Canvas.CenterY, 0, 1);
        project.Camera.Size = Math.Clamp(project.Camera.Size, 0.08, 0.46);
        project.Camera.CenterX = Math.Clamp(project.Camera.CenterX, 0, 1);
        project.Camera.CenterY = Math.Clamp(project.Camera.CenterY, 0, 1);
        project.MicrophoneGain = Math.Clamp(project.MicrophoneGain, 0, 2);
        project.SystemGain = Math.Clamp(project.SystemGain, 0, 2);
        foreach (var clip in project.Clips)
        {
            clip.Speed = Math.Clamp(clip.Speed, 0.5, 2);
            if (clip.SourceEnd < clip.SourceStart)
            {
                (clip.SourceStart, clip.SourceEnd) = (clip.SourceEnd, clip.SourceStart);
            }
        }

        foreach (var zoom in project.Zooms)
        {
            zoom.Scale = Math.Clamp(zoom.Scale, 1, 4);
            zoom.FocusX = Math.Clamp(zoom.FocusX, 0, 1);
            zoom.FocusY = Math.Clamp(zoom.FocusY, 0, 1);
            zoom.Duration = Math.Max(0.12, zoom.Duration);
        }
    }

    public static string NormalizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#F3F1EC";
        }

        var text = value.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length == 6 && int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out _))
        {
            return "#" + text.ToUpperInvariant();
        }

        return "#F3F1EC";
    }

    public static string SanitizeName(string? name)
    {
        var text = string.IsNullOrWhiteSpace(name) ? "未命名" : name.Trim();
        foreach (var character in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(character, ' ');
        }

        text = text.Replace('.', ' ').Trim();
        return string.IsNullOrWhiteSpace(text) ? "未命名" : text;
    }
}
