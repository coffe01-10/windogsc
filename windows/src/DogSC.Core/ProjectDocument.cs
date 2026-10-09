using System.Text.Json;
using System.Text.Json.Serialization;

namespace DogSC.Core;

public static class ProjectSchema
{
    public const int Current = 2;
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
    public string ExportResolution { get; set; } = "source";
    public int ExportFps { get; set; } = 30;

    /// <summary>增益上限：滑杆、预览和导出共用这一个值（200%）。</summary>
    public const double MaxGain = 2;

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
    public double CameraOffset { get; set; }
    public double MicrophoneOffset { get; set; }
    public double Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>录制时采样的光标轨迹（source 秒 + 相对画面 0..1 坐标 + 是否点击）。</summary>
    public List<CursorPoint> Cursor { get; set; } = [];
}

public sealed class CursorPoint
{
    public double Time { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool Click { get; set; }
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
    public SourceCrop Crop { get; set; } = new();
    public string Background { get; set; } = "#F3F1EC";
    public double Padding { get; set; } = 72;
    public double CenterX { get; set; } = 0.5;
    public double CenterY { get; set; } = 0.5;
    public double Scale { get; set; } = 1;
    public double Corner { get; set; } = 18;
    public double Shadow { get; set; } = 28;
    /// <summary>俯仰角（度），画面绕水平轴倾斜，-40..40。</summary>
    public double TiltX { get; set; }
    /// <summary>偏航角（度），画面绕垂直轴倾斜，-40..40。</summary>
    public double TiltY { get; set; }
    /// <summary>边缘虚化强度 0..1（移轴效果，中心清晰四周糊）。</summary>
    public double FocusBlur { get; set; }
    /// <summary>开场动画预设：none / fade / rise / zoom。</summary>
    public string Intro { get; set; } = "none";
    /// <summary>光标呈现设置（录制不录光标，编辑期按轨迹重绘）。</summary>
    public CursorFx Cursor { get; set; } = new();
    /// <summary>壁纸图片：项目相对路径（media/ 下）或绝对路径；空 = 纯色背景。</summary>
    public string? Wallpaper { get; set; }
    /// <summary>壁纸模糊强度 0..1（垫在画面下做氛围虚化）。</summary>
    public double WallpaperBlur { get; set; } = 0.35;
    public List<StickerImage> Stickers { get; set; } = [];
    public List<SoftRegion> SoftRegions { get; set; } = [];

    // Old projects and callers use the first decoration; new projects persist lists.
    [JsonIgnore]
    public StickerImage? Sticker
    {
        get => Stickers.FirstOrDefault();
        set { Stickers.Clear(); if (value is not null) Stickers.Add(value); }
    }
    [JsonIgnore]
    public SoftRegion SoftRegion
    {
        get { if (SoftRegions.Count == 0) SoftRegions.Add(new()); return SoftRegions[0]; }
        set { SoftRegions.Clear(); if (value is not null) SoftRegions.Add(value); }
    }
    [JsonPropertyName("sticker")]
    public StickerImage? LegacySticker { get => null; set { if (value is not null && Stickers.Count == 0) Stickers.Add(value); } }
    [JsonPropertyName("softRegion")]
    public SoftRegion? LegacySoftRegion { get => null; set { if (value is not null && SoftRegions.Count == 0) SoftRegions.Add(value); } }

}

/// <summary>画面上的一张贴图：位置/宽是相对 canvas 的 0..1 归一化，高按图比例。</summary>
public sealed class StickerImage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public EffectWindow Timing { get; set; } = new();
    public string Path { get; set; } = "";
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.5;
    public double Width { get; set; } = 0.2;
    /// <summary>整体不透明度 0..1。</summary>
    public double Opacity { get; set; } = 1;
    public double Rotation { get; set; }
}

/// <summary>局部柔化：矩形区域按强度混入高斯模糊。</summary>
public sealed class SoftRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Style { get; set; } = "blur";
    // Legacy masks were placed on the output canvas. New masks attach to source pixels.
    public string CoordinateSpace { get; set; } = "canvas";
    public double Corner { get; set; } = 0.08;
    public double Dimming { get; set; } = 0.22;
    public EffectWindow Timing { get; set; } = new();
    public bool Enabled { get; set; }
    public double X { get; set; } = 0.25;
    public double Y { get; set; } = 0.25;
    public double Width { get; set; } = 0.5;
    public double Height { get; set; } = 0.5;
    /// <summary>0..1，映射到 gblur sigma。</summary>
    public double Strength { get; set; } = 0.5;
}

public sealed class CursorFx
{
    /// <summary>pointer=箭头，dot=圆点，hidden=始终隐藏。</summary>
    public string Mode { get; set; } = "pointer";
    /// <summary>点击处画一个反馈圈。</summary>
    public bool ClickRipple { get; set; } = true;
    /// <summary>光标静止不动一段时间后隐藏。</summary>
    public bool IdleHide { get; set; }
    public double Size { get; set; } = 1;
    public double IdleDelay { get; set; } = 1.6;
    /// <summary>正值提前光标与点击，使用原始素材时钟。</summary>
    public double TimingAdvance { get; set; }
    public string ClickColor { get; set; } = "#292929";
    public double ClickOpacity { get; set; } = 1;
    public double ClickScale { get; set; } = 1;
}

public sealed class SourceCrop
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
}

public sealed class CameraBubble
{
    public double CenterX { get; set; } = 0.84;
    public double CenterY { get; set; } = 0.78;
    public double Size { get; set; } = 0.2;
    /// <summary>形状：circle / square / horizontal / vertical / original / none（直角方形）。</summary>
    public string Shape { get; set; } = "circle";
    /// <summary>进出动画：none / fade / rise。fade=淡入淡出，rise=淡入+底部滑入。</summary>
    public string Entry { get; set; } = "none";
    /// <summary>方形圆角半径比例 0..1（相对边长）。</summary>
    public double Corner { get; set; } = 0.18;
    public double ContentScale { get; set; } = 1;
    public double ContentX { get; set; } = .5;
    public double ContentY { get; set; } = .5;
    public bool Hidden { get; set; }
    public bool Mirrored { get; set; }
    /// <summary>摄像头画面相对原始素材提前的秒数。</summary>
    public double TimingAdvance { get; set; }
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
        foreach (var asset in project.Assets)
        {
            asset.Cursor ??= [];
        }
        project.Canvas.Background = NormalizeColor(project.Canvas.Background);
        project.Canvas.Crop ??= new();
        var crop = project.Canvas.Crop;
        crop.Width = Math.Clamp(crop.Width, 0.02, 1);
        crop.Height = Math.Clamp(crop.Height, 0.02, 1);
        crop.X = Math.Clamp(crop.X, 0, 1 - crop.Width);
        crop.Y = Math.Clamp(crop.Y, 0, 1 - crop.Height);
        project.Canvas.Scale = Math.Clamp(project.Canvas.Scale, 0.25, 2.5);
        project.Canvas.Padding = Math.Clamp(project.Canvas.Padding, 0, 240);
        project.Canvas.Corner = Math.Clamp(project.Canvas.Corner, 0, 240);
        project.Canvas.Shadow = Math.Clamp(project.Canvas.Shadow, 0, 100);
        project.Canvas.CenterX = Math.Clamp(project.Canvas.CenterX, 0, 1);
        project.Canvas.CenterY = Math.Clamp(project.Canvas.CenterY, 0, 1);
        project.Canvas.TiltX = Math.Clamp(project.Canvas.TiltX, -40, 40);
        project.Canvas.TiltY = Math.Clamp(project.Canvas.TiltY, -40, 40);
        project.Canvas.FocusBlur = Math.Clamp(project.Canvas.FocusBlur, 0, 1);
        project.Canvas.Intro = project.Canvas.Intro is "fade" or "rise" or "zoom" ? project.Canvas.Intro : "none";
        project.Canvas.Cursor ??= new CursorFx();
        project.Canvas.Cursor.Mode = project.Canvas.Cursor.Mode is "pointer" or "dot" or "hidden" ? project.Canvas.Cursor.Mode : "pointer";
        project.Canvas.Cursor.Size = Math.Clamp(project.Canvas.Cursor.Size, 0.5, 4);
        project.Canvas.Cursor.IdleDelay = Math.Clamp(project.Canvas.Cursor.IdleDelay, 0.1, 10);
        project.Canvas.Cursor.TimingAdvance = Math.Clamp(project.Canvas.Cursor.TimingAdvance, -1, 1);
        project.Canvas.Cursor.ClickColor = NormalizeColor(project.Canvas.Cursor.ClickColor);
        project.Canvas.Cursor.ClickOpacity = Math.Clamp(project.Canvas.Cursor.ClickOpacity, 0, 1);
        project.Canvas.Cursor.ClickScale = Math.Clamp(project.Canvas.Cursor.ClickScale, 0.5, 4);
        project.Canvas.Wallpaper = string.IsNullOrWhiteSpace(project.Canvas.Wallpaper) ? null : project.Canvas.Wallpaper.Trim();
        project.Canvas.WallpaperBlur = Math.Clamp(project.Canvas.WallpaperBlur, 0, 1);
        project.Canvas.Stickers ??= [];
        project.Canvas.SoftRegions ??= [];
        foreach (var sticker in project.Canvas.Stickers.ToArray())
        {
            sticker.Timing ??= new();
            EffectTiming.Normalize(sticker.Timing);
            sticker.Path = sticker.Path.Trim();
            if (string.IsNullOrWhiteSpace(sticker.Name)) sticker.Name = Path.GetFileName(sticker.Path);
            sticker.X = Math.Clamp(sticker.X, 0, 1);
            sticker.Y = Math.Clamp(sticker.Y, 0, 1);
            sticker.Width = Math.Clamp(sticker.Width, 0.02, 1);
            sticker.Opacity = Math.Clamp(sticker.Opacity, 0, 1);
            sticker.Rotation = Math.Clamp(sticker.Rotation, -180, 180);
            if (sticker.Path.Length == 0)
            {
                project.Canvas.Stickers.Remove(sticker);
            }
        }

        foreach (var soft in project.Canvas.SoftRegions)
        {
            soft.Timing ??= new();
            EffectTiming.Normalize(soft.Timing);
            soft.Style = soft.Style == "spotlight" ? "spotlight" : "blur";
            soft.CoordinateSpace = soft.CoordinateSpace == "source" ? "source" : "canvas";
            soft.Width = Math.Clamp(soft.Width, project.Width > 0 ? 1d / project.Width : .0001, 1);
            soft.Height = Math.Clamp(soft.Height, project.Height > 0 ? 1d / project.Height : .0001, 1);
            soft.X = Math.Clamp(soft.X, 0, 1 - soft.Width);
            soft.Y = Math.Clamp(soft.Y, 0, 1 - soft.Height);
            soft.Strength = Math.Clamp(soft.Strength, 0, 1);
            soft.Corner = Math.Clamp(soft.Corner, 0, 0.5);
            soft.Dimming = Math.Clamp(soft.Dimming, 0, 1);
        }
        project.Camera.Size = Math.Clamp(project.Camera.Size, 0.08, 0.46);
        project.Camera.CenterX = Math.Clamp(project.Camera.CenterX, 0, 1);
        project.Camera.CenterY = Math.Clamp(project.Camera.CenterY, 0, 1);
        project.Camera.Shape = project.Camera.Shape is "square" or "none" or "horizontal" or "vertical" or "original" ? project.Camera.Shape : "circle";
        project.Camera.ContentScale = Math.Clamp(project.Camera.ContentScale, 1, 4);
        project.Camera.ContentX = Math.Clamp(project.Camera.ContentX, 0, 1);
        project.Camera.ContentY = Math.Clamp(project.Camera.ContentY, 0, 1);
        project.Camera.Entry = project.Camera.Entry is "fade" or "rise" ? project.Camera.Entry : "none";
        project.Camera.Corner = Math.Clamp(project.Camera.Corner, 0, 0.5);
        project.MicrophoneGain = Math.Clamp(project.MicrophoneGain, 0, ProjectDocument.MaxGain);
        project.SystemGain = Math.Clamp(project.SystemGain, 0, ProjectDocument.MaxGain);
        foreach (var clip in project.Clips)
        {
            clip.Speed = Math.Clamp(clip.Speed, 0.5, 100);
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
