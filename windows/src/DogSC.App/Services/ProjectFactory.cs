using DogSC.Core;

namespace DogSC;

public static class ProjectFactory
{
    public static string CreateDirectory(string name)
    {
        var safe = ProjectFile.SanitizeName(name);
        var root = SettingsStore.Current.ProjectsDirectory;
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, safe + ".dogscproject");
        var suffix = 2;
        while (Directory.Exists(directory))
        {
            directory = Path.Combine(root, safe + " " + suffix + ".dogscproject");
            suffix++;
        }

        Directory.CreateDirectory(Path.Combine(directory, "media"));
        return directory;
    }

    public static async Task<string> ImportAsync(string ffmpeg, string ffprobe, string source)
    {
        var directory = CreateDirectory(Path.GetFileNameWithoutExtension(source));
        var video = Path.Combine(directory, "media", "screen.mp4");
        var audio = Path.Combine(directory, "media", "system.wav");
        var copied = false;
        if (source.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            var copy = FfmpegProcess.Start(ffmpeg, ["-y", "-i", source, "-map", "0:v:0", "-c:v", "copy", "-an", video]);
            var code = await copy.WaitAsync();
            copy.Dispose();
            copied = code == 0 && File.Exists(video);
        }

        if (!copied)
        {
            var transcode = FfmpegProcess.Start(ffmpeg, [
                "-y", "-i", source, "-map", "0:v:0", "-an",
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", video,
            ]);
            var code = await transcode.WaitAsync();
            var error = transcode.ErrorText();
            transcode.Dispose();
            if (code != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "无法读取这个视频。" : error.Trim());
            }
        }

        var extract = FfmpegProcess.Start(ffmpeg, ["-y", "-i", source, "-vn", "-c:a", "pcm_s16le", audio]);
        var audioCode = await extract.WaitAsync();
        extract.Dispose();
        if (audioCode != 0 || !File.Exists(audio) || new FileInfo(audio).Length < 64)
        {
            TryDelete(audio);
            audio = "";
        }

        var info = await FfmpegTools.Probe(ffprobe, video) ?? throw new InvalidOperationException("导入的视频没有画面。");
        var asset = new MediaAsset
        {
            Video = "media/screen.mp4",
            SystemAudio = audio.Length == 0 ? null : "media/system.wav",
            Duration = Math.Max(info.Duration, 0.1),
            Width = info.Width,
            Height = info.Height,
        };
        var project = new ProjectDocument
        {
            Name = Path.GetFileNameWithoutExtension(source),
            Width = Math.Max(info.Width, 2),
            Height = Math.Max(info.Height, 2),
            SourceLabel = "导入的视频",
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = asset.Duration }],
        };
        ProjectFile.Save(directory, project);
        SettingsStore.Remember(directory);
        return directory;
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A leftover silent file is harmless.
        }
    }
}
