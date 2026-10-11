using DogSC;
using DogSC.Core;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;

internal static class RecordingRecoveryChecks
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // The parent kills this worker, without killing its process tree. Its actual
    // RecordingSession/FFmpeg child must be terminated by the production Job Object.
    public static async Task<int> WorkerAsync(string ffmpeg, string ffprobe, string directory)
    {
        await using var session = new RecordingSession(ffmpeg, ffprobe,
            new(CaptureKind.Region, "Recovery regression", 0, 0, 320, 180, null),
            new() { FrameRate = 60 }, directory);
        await session.StartAsync();
        await Task.Delay(1300);
        session.Mark("first-take");
        var journal = Path.Combine(directory, "journal.json");
        var previous = File.ReadAllText(journal);
        // A failed replacement must preserve the last complete journal.
        var temporary = journal + ".tmp";
        Directory.CreateDirectory(temporary);
        try
        {
            session.Mark("blocked-write");
            Require(File.ReadAllText(journal) == previous, "Failed journal write destroyed the previous markers.");
        }
        finally { Directory.Delete(temporary); }
        await Task.Delay(300);
        await session.PauseAsync();
        await session.ResumeAsync();
        await Task.Delay(300);
        session.Mark("second-take");
        var video = typeof(RecordingSession).GetField("_video", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
        var process = (Process)typeof(FfmpegProcess).GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(video)!;
        Console.WriteLine("CHILD " + process.Id);
        Console.Out.Flush();
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    public static async Task RunAsync()
    {
        var settingsPath = AppPaths.SettingsFile;
        var settingsBytes = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        SettingsStore.Load();
        var projectsBefore = SettingsStore.Current.ProjectsDirectory;
        var recentBefore = SettingsStore.Current.RecentProjects.ToList();
        var root = Path.Combine(Path.GetTempPath(), "DogSC-recovery-check-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "crashed.dogscproject");
        Directory.CreateDirectory(directory);
        Process? worker = null;
        Process? child = null;
        try
        {
            var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
            var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg) ?? throw new InvalidOperationException("FFprobe unavailable");
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { Assembly.GetExecutingAssembly().Location, "--recovery-worker", ffmpeg, ffprobe, directory })
                start.ArgumentList.Add(arg);
            worker = Process.Start(start)!;
            var errors = worker.StandardError.ReadToEndAsync();
            var ready = await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Require(ready is not null && ready.StartsWith("CHILD "), "Recording worker did not start: " + (ready ?? await errors));
            child = Process.GetProcessById(int.Parse(ready![6..]));
            var journal = RecordingSession.ReadJournal(directory);
            Require(journal.Takes.Count == 2, "Pause/resume lost the first take in the journal.");
            await Task.Delay(3200);
            worker.Kill(); // Deliberately leave FFmpeg cleanup to ChildJob.
            await worker.WaitForExitAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Console.WriteLine("RECOVERY_OK parent termination stops FFmpeg");

            var latest = Directory.GetFiles(Path.Combine(directory, "media"), "*.mp4").OrderBy(path => path).Last();
            var media = await FfmpegTools.Probe(ffprobe, latest);
            Require(media is { Width: 320, Height: 180, Duration: > 1 }, "Short interrupted recording has no playable fragments.");
            using (var decode = FfmpegProcess.Start(ffmpeg, ["-v", "error", "-i", latest, "-f", "null", "-"]))
                Require(await decode.WaitAsync() == 0, "Interrupted recording cannot be decoded: " + decode.ErrorText());

            var legacy = Path.Combine(root, "legacy.dogscproject");
            Directory.CreateDirectory(Path.Combine(legacy, "media"));
            File.Copy(latest, Path.Combine(legacy, "media", "0001.mp4"));
            File.WriteAllText(Path.Combine(legacy, "media", "0001-camera.mp4"), "broken camera file");
            File.WriteAllText(Path.Combine(legacy, "journal.json"), "{broken");
            SettingsStore.Current.ProjectsDirectory = root;
            Require(await ProjectFactory.RecoverPartialAsync(ffprobe, ffmpeg) == 2, "Recovery failed for a journal or legacy project.");
            var project = ProjectFile.Load(directory);
            Require(project.Assets.Count == 2 && project.Clips.Count == 2, "Recovered project lost a take.");
            Require(project.FrameRate == 60 && project.SourceLabel == "Recovery regression", "Recovery lost capture metadata.");
            foreach (var take in journal.Takes.Values)
                Require(project.Assets.Any(asset => asset.Id == take.AssetId), "Recovery changed asset IDs.");
            Require(project.Markers.Any(mark => mark.Note == "first-take") && project.Markers.Any(mark => mark.Note == "second-take"),
                "Recovery lost markers from a completed or interrupted take.");
            Require(!File.Exists(Path.Combine(directory, "journal.json.tmp")), "Journal writes leave temporary files behind.");
            Require(ProjectFile.Load(legacy).Assets.Count == 1, "Corrupt journal prevented filename-based recovery.");
            Require(ProjectFile.Load(legacy).Assets[0].Camera is null, "Corrupt camera prevented recovery of the valid screen recording.");
            Console.WriteLine("RECOVERY_OK short MP4 decode, pause/resume, markers, asset IDs, 60fps and corrupt-journal/camera fallback");

            var normal = Path.Combine(root, "normal.dogscproject");
            await using (var session = new RecordingSession(ffmpeg, ffprobe,
                new(CaptureKind.Region, "Normal regression", 0, 0, 320, 180, null), new(), normal))
            {
                await session.StartAsync();
                session.Mark("normal-finish");
                await Task.Delay(1000);
                var finished = await session.FinishAsync();
                Require(finished.Assets.Count == 1 && finished.Markers.Any(mark => mark.Note == "normal-finish"), "Normal stop lost the recording or marker.");
                Require(!File.Exists(Path.Combine(normal, "journal.json")), "Normal stop retained the recovery journal.");
            }
            Console.WriteLine("RECOVERY_OK normal finish preserves markers and removes the journal");

            foreach (var previewPath in new[] { latest, Directory.GetFiles(Path.Combine(normal, "media"), "*.mp4").Single() })
                await CheckPreviewAsync(previewPath, ffprobe);
        }
        finally
        {
            if (worker is not null) { if (!worker.HasExited) { worker.Kill(); await worker.WaitForExitAsync(); } worker.Dispose(); }
            if (child is not null) { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } child.Dispose(); }
            SettingsStore.Current.ProjectsDirectory = projectsBefore;
            SettingsStore.Current.RecentProjects = recentBefore;
            if (settingsBytes is null) File.Delete(settingsPath);
            else File.WriteAllBytes(settingsPath, settingsBytes);
            Directory.Delete(root, recursive: true);
        }
    }

    static async Task CheckPreviewAsync(string path, string ffprobe)
    {
        var media = await FfmpegTools.Probe(ffprobe, path);
        Require(media is { Duration: > .2 }, "Completed/recovered recording has no usable media.");
        var opened = new TaskCompletionSource();
        var player = new MediaPlayer();
        player.MediaOpened += (_, _) => opened.TrySetResult();
        player.MediaFailed += (_, e) => opened.TrySetException(e.ErrorException);
        try
        {
            player.Open(new Uri(path));
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // WPF can round short MP4 durations to whole seconds. What failed
            // before remuxing was a zero duration, which prevents useful seeking.
            Require(player.NaturalVideoWidth == 320 && player.NaturalDuration.HasTimeSpan &&
                player.NaturalDuration.TimeSpan > TimeSpan.Zero &&
                Math.Abs(player.NaturalDuration.TimeSpan.TotalSeconds - media!.Duration) < 1,
                $"WPF preview metadata differs from FFprobe: {player.NaturalVideoWidth}px, {player.NaturalDuration}, expected {media!.Duration:0.00}s.");
            var seek = Math.Min(.5, media!.Duration / 2);
            player.Position = TimeSpan.FromSeconds(seek);
            player.Play();
            await Task.Delay(200);
            player.Pause();
            Require(player.Position.TotalSeconds >= seek * .5, "WPF could not seek within the recording.");
            Console.WriteLine("RECOVERY_OK WPF completed/recovered MP4 preview and seek");
        }
        finally { player.Close(); }
    }
}
