using DogSC;
using DogSC.Core;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

internal static class CaptureTargetChecks
{
    public static async Task RunAsync()
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
        var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg) ?? throw new InvalidOperationException("FFprobe unavailable");
        var root = Path.Combine(Path.GetTempPath(), "DogSC-capture-targets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var first = new Window { Title = "First fictional target", Background = Brushes.Cyan, Width = 320, Height = 200,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Topmost = true, Left = 80, Top = 80 };
        var second = new Window { Title = "Second fictional target", Background = Brushes.Yellow, Width = 400, Height = 240,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Topmost = true, Left = 600, Top = 80 };
        RecordingSession? session = null;
        CaptureTarget Target(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle; var rect = Native.WindowRect(hwnd)!;
            return new(CaptureKind.Window, window.Title, rect.Left, rect.Top, rect.Width, rect.Height, null) { WindowHandle = hwnd };
        }
        try
        {
            first.Show(); second.Show(); await Task.Delay(200);
            var a = Target(first); var b = Target(second);
            session = new RecordingSession(ffmpeg, ffprobe, a, new() { Quality = "high", RecordCursor = false }, root);
            await session.StartAsync(); await Task.Delay(500); session.Mark("first");
            await session.SwitchTargetAsync(b); await Task.Delay(500); session.Mark("second");
            var retained = session.Elapsed;
            var failed = false;
            try { await session.SwitchTargetAsync(new(CaptureKind.Window, "Unavailable", 0, 0, 320, 180, null)); }
            catch (Exception) { failed = true; }
            if (!failed || session.IsRunning || session.Elapsed < retained)
                throw new InvalidOperationException("Unavailable target silently captured the desktop or discarded existing takes");
            var journal = RecordingSession.ReadJournal(root);
            if (journal.Takes.Count != 2 || !journal.Takes.Values.Any(t => t.TargetLabel == b.Label))
                throw new InvalidOperationException("Target switch did not retain per-take recovery metadata");
            await session.SwitchTargetAsync(a);
            if (session.IsRunning) throw new InvalidOperationException("Switching a paused target unexpectedly started recording");
            await session.ResumeAsync(); await Task.Delay(500); session.Mark("third");
            var project = await session.FinishAsync();
            if (project.Assets.Count != 3 || project.Markers.Count != 3 || project.Assets.Any(asset => asset.Cursor.Count != 0)
                || project.Assets[0].SourceLabel != a.Label || project.Assets[1].SourceLabel != b.Label || project.Assets[2].SourceLabel != a.Label)
                throw new InvalidOperationException("Target switching lost assets, markers, source labels, or ignored disabled pointer recording");
            for (var i = 0; i < 3; i++)
            {
                var raw = Path.Combine(root, "frame.rgb");
                try
                {
                    using var decode = FfmpegProcess.Start(ffmpeg, ["-y", "-i", Path.Combine(root, project.Assets[i].Video), "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", raw]);
                    if (await decode.WaitAsync() != 0) throw new InvalidOperationException(decode.ErrorText());
                    var pixels = await File.ReadAllBytesAsync(raw); var index = (project.Assets[i].Height / 2 * project.Assets[i].Width + project.Assets[i].Width / 2) * 3;
                    var cyan = i != 1;
                    if (pixels[index + 1] < 230 || (cyan ? pixels[index] > 25 || pixels[index + 2] < 230 : pixels[index] < 230 || pixels[index + 2] > 25))
                        throw new InvalidOperationException("Switched recording retained the previous target's pixels");
                }
                finally { if (File.Exists(raw)) File.Delete(raw); }
            }
            var settings = new AppSettings(); var monitor = Native.Monitors()[0];
            var display = new CaptureTarget(CaptureKind.Display, monitor.Name, monitor.Left, monitor.Top, monitor.Width, monitor.Height, null);
            CaptureTargetHistory.Remember(settings, display); CaptureTargetHistory.Remember(settings, display);
            var path = Path.Combine(root, "settings.json");
            try
            {
                SettingsFile.Save(path, settings); var restored = SettingsFile.Load(path);
                if (restored.RecentCaptureTargets.Count != 1 || CaptureTargetHistory.Resolve(restored.RecentCaptureTargets[0], [])?.Label != display.Label)
                    throw new InvalidOperationException("Recent targets did not survive settings reload or refresh their catalog entry");
                if (CaptureTargetHistory.Resolve(new() { Kind = "Device", DeviceName = "Unavailable test device" }, []) is not null)
                    throw new InvalidOperationException("Missing recent device resolved to another capture source");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            Console.WriteLine("CAPTURE_TARGET_OK live/paused switching, failure retains usable takes, per-source pixels and labels, markers, pointer disabled, recent target persistence and unavailable target handling");
        }
        finally { if (session is not null) await session.DisposeAsync(); first.Close(); second.Close(); Directory.Delete(root, recursive: true); }
    }
}
