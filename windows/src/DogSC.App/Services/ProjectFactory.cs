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
    /// Mac 录制恢复：录制中断留在磁盘上的项目目录只有 media/ 没有 project.json，
    /// 按 <token>.mp4 / <token>-camera.mp4 / <token>-mic.wav / <token>-system.wav
    /// 命名归组，逐 take 探测时长后重建项目文件。返回恢复的目录数。
    /// </summary>
    public static async Task<int> RecoverPartialAsync(string ffprobe, string? ffmpeg = null)
    {
        var root = SettingsStore.Current.ProjectsDirectory;
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var recovered = 0;
        foreach (var directory in Directory.GetDirectories(root))
        {
            try
            {
                if (File.Exists(Path.Combine(directory, "project.json")))
                {
                    continue;
                }

                var media = Path.Combine(directory, "media");
                if (!Directory.Exists(media))
                {
                    continue;
                }

                var takes = Directory.GetFiles(media, "*.mp4")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => name is not null
                        && !name.EndsWith("-camera", StringComparison.Ordinal)
                        && !name.EndsWith("-mic", StringComparison.Ordinal)
                        && !name.EndsWith("-system", StringComparison.Ordinal))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                if (takes.Length == 0)
                {
                    continue;
                }

                ffmpeg ??= FfmpegTools.LocateFfmpeg();

                var journal = RecordingSession.ReadJournal(directory);
                var project = new ProjectDocument
                {
                    Name = Path.GetFileName(directory).Replace(".dogscproject", ""),
                    SourceLabel = string.IsNullOrWhiteSpace(journal.TargetLabel) ? "恢复的录制" : journal.TargetLabel,
                    FrameRate = journal.FrameRate,
                };
                foreach (var token in takes)
                {
                    var screen = Path.Combine(media, token + ".mp4");
                    if (new FileInfo(screen).Length < 1024)
                    {
                        continue;
                    }

                    var info = await FfmpegTools.Probe(ffprobe, screen);
                    if (info is null || info.Width < 2 || info.Height < 2 || info.Duration < 0.2)
                    {
                        continue;
                    }

                    var camera = Path.Combine(media, token + "-camera.mp4");
                    string? recoveredCamera = File.Exists(camera) ? "media/" + Path.GetFileName(camera) : null;
                    if (ffmpeg is not null)
                    {
                        await FfmpegTools.FinalizeRecordingAsync(ffmpeg, screen);
                        if (recoveredCamera is not null && !await FfmpegTools.FinalizeCameraRecordingAsync(ffmpeg, camera))
                            recoveredCamera = null;
                    }

                    string? Companion(string suffix, params string[] extensions) => extensions
                        .Select(ext => Path.Combine(media, token + suffix + ext))
                        .FirstOrDefault(File.Exists) is { } found
                        ? "media/" + Path.GetFileName(found) : null;
                    journal.Takes.TryGetValue(token!, out var entry);
                    var asset = new MediaAsset
                    {
                        SourceLabel = entry?.TargetLabel ?? journal.TargetLabel,
                        Id = string.IsNullOrEmpty(entry?.AssetId) ? Guid.NewGuid().ToString("N") : entry!.AssetId,
                        Video = "media/" + token + ".mp4",
                        Camera = recoveredCamera,
                        Microphone = Companion("-mic", ".wav", ".mp3", ".m4a"),
                        SystemAudio = Companion("-system", ".wav", ".m4a", ".mp3"),
                        Duration = Math.Max(info.Duration, 0.1),
                        Width = info.Width,
                        Height = info.Height,
                    };
                    project.Assets.Add(asset);
                    project.Clips.Add(new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = asset.Duration });
                    if (entry is not null)
                    {
                        foreach (var mark in entry.Marks)
                        {
                            if (mark.Time <= asset.Duration)
                            {
                                project.Markers.Add(new SourceMarker
                                {
                                    AssetId = asset.Id,
                                    SourceTime = mark.Time,
                                    Note = mark.Note,
                                });
                            }
                        }
                    }
                }

                if (project.Clips.Count == 0)
                {
                    continue;
                }

                project.Width = Math.Max(project.Assets[0].Width, 2);
                project.Height = Math.Max(project.Assets[0].Height, 2);
                ProjectFile.Save(directory, project);
                SettingsStore.Remember(directory);
                recovered++;
            }
            catch (Exception ex)
            {
                AppPaths.Log("录制恢复失败 " + directory + "：" + ex.Message);
            }
        }

        return recovered;
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
            var copied = new Dictionary<string, string>();
            foreach (var reference in result.MediaReferences.Distinct(StringComparer.Ordinal))
            {
                var source = Path.Combine(packagePath, reference.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(source))
                {
                    continue;
                }

                var name = "import-" + copied.Count + "-" + Path.GetFileName(source);
                var target = Path.Combine(mediaDir, name);
                File.Copy(source, target, overwrite: true);
                copied[reference] = "media/" + name;
            }

            var asset = project.Assets[0];
            if (copied.TryGetValue(asset.Video, out var screen))
            {
                asset.Video = screen;
            }
            else
            {
                throw new InvalidDataException("macOS 项目的屏幕录制文件拷不过来：" + asset.Video);
            }

            asset.Camera = asset.Camera is { } camera && copied.TryGetValue(camera, out var cameraCopy) ? cameraCopy : null;
            asset.Microphone = asset.Microphone is { } mic && copied.TryGetValue(mic, out var micCopy) ? micCopy : null;
            if (project.Canvas.Wallpaper is { } wall && copied.TryGetValue(wall, out var wallCopy)) project.Canvas.Wallpaper = wallCopy;
            foreach (var sticker in project.Canvas.Stickers.ToArray())
                if (copied.TryGetValue(sticker.Path, out var stickerCopy)) sticker.Path = stickerCopy;
                else { result.DroppedFields.Add("贴图文件缺失：" + sticker.Path); project.Canvas.Stickers.Remove(sticker); }
            if (result.PointerPath is { } pointer)
            {
                var pointerFile = Path.Combine(packagePath, pointer.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(pointerFile)) asset.Cursor = MacProjectImporter.ImportPointerEvents(File.ReadLines(pointerFile), result.PointerAdvance);
                else result.DroppedFields.Add("指针轨迹文件缺失：" + pointer);
            }
            var originalScreen = ProjectFile.Media(directory, asset.Video);
            var originalInfo = await FfmpegTools.Probe(ffprobe, originalScreen);
            if (originalInfo is not null)
            {
                var fullRecording = result.FullRecording;
                asset.Duration = Math.Max(originalInfo.Duration, 0.1);
                asset.Width = project.Width = originalInfo.Width;
                asset.Height = project.Height = originalInfo.Height;
                if (fullRecording) project.Clips[0].SourceEnd = asset.Duration;
                if (originalInfo.HasAudio && ffmpeg is not null)
                {
                    var audio = Path.Combine(mediaDir, "system.wav");
                    using var extract = FfmpegProcess.Start(ffmpeg, ["-y", "-i", originalScreen, "-map", "0:a:0", "-vn", "-c:a", "pcm_s16le", audio]);
                    if (await extract.WaitAsync() != 0) throw new InvalidOperationException("导入系统声音失败：" + extract.ErrorText());
                    asset.SystemAudio = "media/system.wav";
                }
                else if (originalInfo.HasAudio) result.DroppedFields.Add("系统声音需要 ffmpeg 才能分离，请配置后重新导入。");
            }
            // mac 的 screen/camera 常是 .mov：HEVC 要装扩展、ProRes 无解，
            // Media Foundation 预览会黑屏。转成 h264 mp4，导出/预览都走它。
            if (ffmpeg is not null)
            {
                asset.Video = await ToMp4(ffmpeg, ffprobe, directory, asset.Video) ?? asset.Video;
                asset.Camera = await ToMp4(ffmpeg, ffprobe, directory, asset.Camera);
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

            var target = Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source) + "-preview.mp4");
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
