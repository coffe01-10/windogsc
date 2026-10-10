using DogSC.Core;

namespace DogSC;

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
    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            Current = SettingsFile.Load(AppPaths.SettingsFile);
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
        try
        {
            SettingsFile.Save(AppPaths.SettingsFile, Current);
        }
        catch (Exception ex)
        {
            // 设置保存失败不该打断录制或关窗流程。
            AppPaths.Log("设置保存失败：" + ex.Message);
        }
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
    static ThemeService()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (SettingsStore.Current.AppearancePreference == "system")
                Application.Current.Dispatcher.BeginInvoke(() => Apply("system"));
        };
    }
    public static void Apply(string theme)
    {
        SettingsStore.Current.AppearancePreference = theme;
        if (theme == "system") theme = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0 ? "dark" : "light";
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
