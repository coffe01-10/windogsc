using System.Text.Json;
using System.Text.Json.Serialization;

namespace DogSC.Core;

/// <summary>
/// Application-wide settings stored in %AppData%/DogSC/settings.json. Window
/// coordinates stay <see cref="double.NaN"/> until the window has been shown
/// once, so "remember my position" only kicks in after a real value exists.
/// </summary>
public sealed class AppSettings
{
    public string ProjectsDirectory { get; set; } = "";
    public int FrameRate { get; set; } = 30;
    public bool RecordMicrophone { get; set; }
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordCamera { get; set; }
    public string? MicrophoneId { get; set; }
    public string? PlaybackDeviceId { get; set; }
    public string? CameraName { get; set; }
    public string Theme { get; set; } = "light";
    public List<string> RecentProjects { get; set; } = [];
    [JsonConverter(typeof(NonFiniteToNullConverter))]
    public double BarLeft { get; set; } = double.NaN;
    [JsonConverter(typeof(NonFiniteToNullConverter))]
    public double BarTop { get; set; } = double.NaN;
    public double BarScale { get; set; } = 0.85;
    public bool BarCollapsed { get; set; }
    public BarDockEdge BarDockEdge { get; set; }
    public bool BarAnchorRight { get; set; }
    public bool BarAnchorBottom { get; set; }
    public string MemoText { get; set; } = "";
    public bool MemoReadMode { get; set; }
    public double MemoSize { get; set; } = 18;
    public double MemoOpacity { get; set; } = 0.94;
    [JsonConverter(typeof(NonFiniteToNullConverter))]
    public double MemoLeft { get; set; } = double.NaN;
    [JsonConverter(typeof(NonFiniteToNullConverter))]
    public double MemoTop { get; set; } = double.NaN;
    public string ExportDirectory { get; set; } = "";
    public string ExportResolution { get; set; } = "source";
    public int ExportFps { get; set; } = 30;
    public string? FfmpegPath { get; set; }
    /// <summary>预览画质：low = 四分之一分辨率代理（流畅），full = 完整分辨率。</summary>
    public string PreviewQuality { get; set; } = "full";
    /// <summary>时间线轨道显示位集（对齐 Mac EditorTimelineTrackVisibility）：1=缩放 2=屏幕运动 4=摄像头运动 8=叠加。默认只显示缩放轨，隐藏只是收起行。</summary>
    public int TimelineTracks { get; set; } = 1;
    public List<ScenePreset> ScenePresets { get; set; } = [];
    public bool TourSeen { get; set; }
    public bool RecorderTourSeen { get; set; }
    public string CameraDefaultShape { get; set; } = "circle";
    public int RecordCountdownSeconds { get; set; } = 3;
    public string? DefaultScenePresetId { get; set; }
    /// <summary>启动时自动检测 GitHub 上的新版本（只提示，不自动更新）。</summary>
    public bool CheckUpdates { get; set; } = true;
}

public static class SettingsFile
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Save(string path, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Normalize(settings);
        var json = JsonSerializer.Serialize(settings, Json);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static AppSettings Load(string path)
    {
        AppSettings settings;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                // 早期版本可能写过 System.Text.Json 拒绝的 NaN/Infinity 字面量。
                if (ContainsNonFiniteLiteral(json))
                {
                    throw new JsonException("设置文件含 NaN/Infinity 字面量。");
                }

                settings = JsonSerializer.Deserialize<AppSettings>(json, Json) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 损坏或不可读的设置文件回退到默认值。
            settings = new AppSettings();
        }

        Normalize(settings);
        return settings;
    }

    /// <summary>
    /// Drop non-finite numbers back to their defaults so serialization never
    /// sees NaN/Infinity, and repair fields a corrupted file may have nulled.
    /// </summary>
    public static void Normalize(AppSettings settings)
    {
        var defaults = new AppSettings();
        settings.ProjectsDirectory ??= defaults.ProjectsDirectory;
        settings.Theme = settings.Theme == "dark" ? "dark" : "light";
        settings.FrameRate = settings.FrameRate >= 60 ? 60 : 30;
        settings.RecentProjects ??= [];
        settings.ScenePresets ??= [];
        // 坐标字段 NaN 是合法哨兵（未存过位置），Infinity 没有语义、回到 NaN。
        settings.BarLeft = double.IsInfinity(settings.BarLeft) ? defaults.BarLeft : settings.BarLeft;
        settings.BarTop = double.IsInfinity(settings.BarTop) ? defaults.BarTop : settings.BarTop;
        settings.BarScale = Math.Clamp(FiniteOr(settings.BarScale, defaults.BarScale), 0.7, 1.1);
        settings.MemoText ??= "";
        settings.MemoSize = FiniteOr(settings.MemoSize, defaults.MemoSize);
        settings.MemoSize = Math.Clamp(settings.MemoSize, 8, 96);
        settings.MemoOpacity = FiniteOr(settings.MemoOpacity, defaults.MemoOpacity);
        settings.MemoOpacity = Math.Clamp(settings.MemoOpacity, 0.1, 1);
        settings.MemoLeft = double.IsInfinity(settings.MemoLeft) ? defaults.MemoLeft : settings.MemoLeft;
        settings.MemoTop = double.IsInfinity(settings.MemoTop) ? defaults.MemoTop : settings.MemoTop;
        settings.ExportDirectory ??= defaults.ExportDirectory;
        settings.ExportResolution ??= defaults.ExportResolution;
        settings.ExportFps = settings.ExportFps >= 60 ? 60 : 30;
        settings.PreviewQuality = settings.PreviewQuality == "low" ? "low" : "full";
        settings.CameraDefaultShape = settings.CameraDefaultShape is "square" or "rounded" ? settings.CameraDefaultShape : "circle";
        settings.RecordCountdownSeconds = settings.RecordCountdownSeconds <= 0 ? 0 : settings.RecordCountdownSeconds >= 5 ? 5 : 3;
    }

    static double FiniteOr(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    static bool ContainsNonFiniteLiteral(string json)
    {
        var index = 0;
        while (index < json.Length)
        {
            var character = json[index];
            if (character is 'N' or 'I')
            {
                if (json.AsSpan(index).StartsWith("NaN", StringComparison.Ordinal)
                    || json.AsSpan(index).StartsWith("Infinity", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (character is '-' or '+')
            {
                if (json.AsSpan(index + 1).StartsWith("Infinity", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            index++;
        }

        return false;
    }
}

/// <summary>
/// 坐标用 NaN 表示「还没存过位置」；JSON 不支持非有限数，写成 null，
/// 读回 null（或缺失）时恢复 NaN 哨兵。
/// </summary>
public sealed class NonFiniteToNullConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number ? reader.GetDouble() : double.NaN;

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsFinite(value))
        {
            writer.WriteNumberValue(value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
