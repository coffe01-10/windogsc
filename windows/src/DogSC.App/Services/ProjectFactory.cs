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

    /// <summary>
    /// 把一个 macOS .dogscproject 包转成 Windows 项目：读它的 project.json，
    /// 把 media 清单里引用的文件拷到新目录，字段映射走 MacProjectImporter。
    /// 对不上的字段记录在返回值的 DroppedFields 里。
    /// </summary>
    /// <summary>返回新项目的目录 + 映射结果（含丢弃字段清单）。</summary>
    /// <param name="ffmpeg">用于把 Windows 预览放不了的 .mov（HEVC/ProRes）转成 mp4；为 null 时跳过转码。</param>
    public static async Task<(string Directory, MacProjectImporter.Result Result)> ImportMacAsync(string? ffmpeg, string ffprobe, string packagePath)
    {
        var jsonPath = Path.Combine(packagePath, "project.json");
        if (!File.Exists(jsonPath))
        {
            throw new InvalidDataException("这个文件夹里没有 project.json，不是 DogSC 项目包。");
        }

        var result = MacProjectImporter.Import(await File.ReadAllTextAsync(jsonPath));
        var project = result.Project;
        var directory = CreateDirectory(project.Name);
        try
        {
            // 拷贝 media 引用文件，并把 asset 的相对路径改成 Windows 约定。
            var mediaDir = Path.Combine(directory, "media");
            foreach (var reference in result.MediaReferences)
            {
                var source = Path.Combine(packagePath, reference.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(source))
                {
                    continue;
                }

                var name = Path.GetFileName(source);
                var target = Path.Combine(mediaDir, name);
                File.Copy(source, target, overwrite: true);
            }

            var asset = project.Assets[0];
            var screenName = Path.GetFileName(asset.Video.Replace('/', Path.DirectorySeparatorChar));
            var copiedScreen = Path.Combine(mediaDir, screenName);
            if (File.Exists(copiedScreen))
            {
                asset.Video = "media/" + screenName;
            }
            else
            {
                throw new InvalidDataException("macOS 项目的屏幕录制文件拷不过来：" + asset.Video);
            }

            asset.Camera = Relocate(asset.Camera, mediaDir);
            asset.Microphone = Relocate(asset.Microphone, mediaDir);
            // mac 的 screen/camera 常是 .mov：HEVC 要装扩展、ProRes 无解，
            // Media Foundation 预览会黑屏。转成 h264 mp4，导出/预览都走它。
            if (ffmpeg is not null)
            {
                asset.Video = await ToMp4(ffmpeg, ffprobe, directory, asset.Video) ?? asset.Video;
                asset.Camera = await ToMp4(ffmpeg, ffprobe, directory, asset.Camera);
            }

            var screenFile = Path.Combine(directory, asset.Video.Replace('/', Path.DirectorySeparatorChar));
            // fullRecording 时长未知：用 ffprobe 探测补齐。
            if (asset.Duration <= 1.01 && File.Exists(screenFile))
            {
                var info = await FfmpegTools.Probe(ffprobe, screenFile);
                if (info is not null)
                {
                    asset.Duration = Math.Max(info.Duration, 0.1);
                    asset.Width = info.Width;
                    asset.Height = info.Height;
                    project.Width = Math.Max(info.Width, 2);
                    project.Height = Math.Max(info.Height, 2);
                    project.Clips[0].SourceEnd = asset.Duration;
                }
            }

            ProjectFile.Save(directory, project);
            SettingsStore.Remember(directory);
            return (directory, result);
        }
        catch
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
            throw;
        }

        static string? Relocate(string? relative, string mediaDir)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return null;
            }

            var name = Path.GetFileName(relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(Path.Combine(mediaDir, name)) ? "media/" + name : null;
        }

        /// <summary>
        /// .mov 容器或 codec 不是 h264 时转成 h264/yuv420p 的 mp4，
        /// 返回新的项目相对路径；不需要转或转失败时保留原路径。
        /// </summary>
        static async Task<string?> ToMp4(string ffmpeg, string ffprobe, string directory, string? relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return relative;
            }

            var source = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
            {
                return relative;
            }

            var needsContainer = relative.EndsWith(".mov", StringComparison.OrdinalIgnoreCase);
            var info = await FfmpegTools.Probe(ffprobe, source);
            var needsCodec = info?.VideoCodec is not null and not "h264";
            if (!needsContainer && !needsCodec)
            {
                return relative;
            }

            var target = Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source) + ".mp4");
            var run = FfmpegProcess.Start(ffmpeg, [
                "-y", "-i", source, "-map", "0:v:0", "-an",
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", target,
            ]);
            var code = await run.WaitAsync();
            run.Dispose();
            if (code != 0 || !File.Exists(target))
            {
                return relative;
            }

            TryDelete(source);
            return "media/" + Path.GetFileName(target);
        }
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
