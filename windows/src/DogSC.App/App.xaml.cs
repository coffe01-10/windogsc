namespace DogSC;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        DispatcherUnhandledException += (_, args) =>
        {
            AppPaths.Log(args.Exception.ToString());
            MessageBox.Show(args.Exception.Message, "DogSC");
            args.Handled = true;
        };
        SettingsStore.Load();
        ThemeService.Apply(SettingsStore.Current.Theme);
        if (e.Args.Any(arg => arg == "--self-test"))
        {
            var code = await SelfTest.RunAsync();
            Environment.ExitCode = code;
            Shutdown(code);
            return;
        }

        var edit = Argument(e.Args, "--edit");
        if (edit is not null)
        {
            EditorWindow.Open(edit);
        }

        var import = Argument(e.Args, "--import");
        if (import is not null)
        {
            var ffmpeg = FfmpegTools.LocateFfmpeg();
            var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg);
            if (ffmpeg is not null && ffprobe is not null)
            {
                try
                {
                    EditorWindow.Open(await ProjectFactory.ImportAsync(ffmpeg, ffprobe, import));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "DogSC");
                }
            }
        }

        AppHost.ShowBar();
        if (SettingsStore.Current.CheckUpdates)
        {
            // 有新版本就在录制条角落提示，点文字跳浏览器到 release 页。
            UpdateService.CheckInBackground(info => AppHost.Bar?.ShowUpdateNotice(info));
        }
    }

    static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
