using System.Text.Json;

namespace DogSC;

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
    public double BarLeft { get; set; } = double.NaN;
    public double BarTop { get; set; } = double.NaN;
    public string MemoText { get; set; } = "";
    public double MemoSize { get; set; } = 18;
    public double MemoOpacity { get; set; } = 0.94;
    public double MemoLeft { get; set; } = double.NaN;
    public double MemoTop { get; set; } = double.NaN;
    public string ExportDirectory { get; set; } = "";
    public string ExportResolution { get; set; } = "source";
    public int ExportFps { get; set; } = 30;
    public string? FfmpegPath { get; set; }
}

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DogSC");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string LogFile => Path.Combine(Root, "logs", "dogsc.log");

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + Environment.NewLine);
        }
        catch
        {
            // Logging must not take down a recording.
        }
    }
}

public static class SettingsStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("设置读取失败：" + ex.Message);
            Current = new AppSettings();
        }

        if (string.IsNullOrWhiteSpace(Current.ProjectsDirectory))
        {
            Current.ProjectsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DogSC");
        }

        if (string.IsNullOrWhiteSpace(Current.ExportDirectory))
        {
            Current.ExportDirectory = Current.ProjectsDirectory;
        }

        Directory.CreateDirectory(Current.ProjectsDirectory);
    }

    public static void Save()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, Json));
    }

    public static void Remember(string projectDirectory)
    {
        Current.RecentProjects.RemoveAll(item => string.Equals(item, projectDirectory, StringComparison.OrdinalIgnoreCase));
        Current.RecentProjects.Insert(0, projectDirectory);
        if (Current.RecentProjects.Count > 8)
        {
            Current.RecentProjects.RemoveRange(8, Current.RecentProjects.Count - 8);
        }

        Save();
    }
}

public static class ThemeService
{
    public static void Apply(string theme)
    {
        var source = theme == "dark" ? "Themes/Dark.xaml" : "Themes/Light.xaml";
        var dictionary = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Add(dictionary);
        }
        else
        {
            merged[0] = dictionary;
        }

        SettingsStore.Current.Theme = theme == "dark" ? "dark" : "light";
    }
}

public static class AppHost
{
    public static RecorderBarWindow? Bar { get; set; }

    public static void ShowBar()
    {
        if (Bar is null)
        {
            Bar = new RecorderBarWindow();
            Bar.Show();
            return;
        }

        if (!Bar.IsVisible)
        {
            Bar.Show();
        }

        Bar.Activate();
    }
}
