using DogSC.Core;

namespace DogSC;

public static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        SettingsStore.Load();
        var ffmpeg = FfmpegTools.LocateFfmpeg();
        var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg);
        if (ffmpeg is null || ffprobe is null)
        {
            Console.Error.WriteLine("SELFTEST_FAIL 找不到 ffmpeg 或 ffprobe。");
            return 1;
        }

        var root = Path.Combine(Path.GetTempPath(), "DogSC-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "media"));
        try
        {
            return await RunInDirectoryAsync(ffmpeg, ffprobe, root);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SELFTEST_NOTE 临时目录未删除：" + root + "；" + ex.Message);
            }
        }
    }

    static async Task<int> RunInDirectoryAsync(string ffmpeg, string ffprobe, string root)
    {
        var source = Path.Combine(root, "media", "screen.mp4");
        using var generated = FfmpegProcess.Start(ffmpeg, [
            "-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=30:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", source,
        ]);
        if (await generated.WaitAsync() != 0)
        {
            Console.Error.WriteLine("SELFTEST_FAIL 无法生成测试画面。");
            Console.Error.WriteLine(generated.ErrorText());
            return 1;
        }

        var asset = new MediaAsset
        {
            Video = "media/screen.mp4",
            Duration = 2,
            Width = 640,
            Height = 360,
        };
        var project = new ProjectDocument
        {
            Name = "selftest",
            Width = 640,
            Height = 360,
            FrameRate = 30,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = 2 }],
            Canvas = new CanvasSettings { Padding = 36, Corner = 20, Shadow = 24, Background = "#F3F1EC" },
        };
        var zoom = TimelineEdits.AddZoom(project, 0.2)!;
        zoom.Duration = 0.6;
        if (TimelineEdits.PasteZoom(project, zoom, 1, 0, out var pasteError) is null)
        {
            Console.Error.WriteLine("SELFTEST_FAIL 缩放复制失败：" + pasteError);
            return 1;
        }
        ProjectFile.Save(root, project);
        var output = Path.Combine(root, "out.mp4");
        var exporter = new ExportService(ffmpeg);
        try
        {
            using var exportCancel = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await exporter.ExportAsync(project, root, new ExportOptions { Resolution = "source", Fps = 30 }, output, exportCancel.Token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SELFTEST_FAIL 导出失败。");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var info = await FfmpegTools.Probe(ffprobe, output);
        if (info is null || info.Width != 640 || info.Height != 360 || info.Duration < 1.5)
        {
            Console.Error.WriteLine($"SELFTEST_FAIL 导出结果异常：{info}");
            return 1;
        }

        Console.WriteLine($"SELFTEST_OK export {info.Width}x{info.Height} {info.Duration:0.00}s");
        asset.Cursor = [new CursorPoint { Time = 0, X = 0.2, Y = 0.3 },
            new CursorPoint { Time = 0.5, X = 0.5, Y = 0.5, Click = true },
            new CursorPoint { Time = 0.55, X = 0.55, Y = 0.55 }];
        asset.Camera = asset.Video;
        asset.SystemAudio = asset.Video;
        project.Camera.Mirrored = true;
        project.Camera.TimingAdvance = 0.01;
        project.Canvas.Crop = new SourceCrop { X = 0.1, Y = 0.1, Width = 0.8, Height = 0.8 };
        project.Canvas.Aspect = "9:16";
        project.Canvas.TiltY = 18;
        var parityOutput = Path.Combine(root, "parity.mp4");
        await exporter.ExportAsync(project, root, new ExportOptions(), parityOutput, CancellationToken.None);
        var parityInfo = await FfmpegTools.Probe(ffprobe, parityOutput);
        if (parityInfo is null || parityInfo.Width >= parityInfo.Height || !parityInfo.HasAudio || parityInfo.Duration < 1.5)
            throw new InvalidOperationException("裁切、光标、摄像头或声音导出异常。");
        Console.WriteLine("SELFTEST_OK parity crop/cursor/ripple/mirror/sync/audio");
        var sticker = Path.Combine(root, "media", "logo.bmp");
        MaskImage.WriteBmp(sticker, 64, 64, 8, false);
        project.Canvas.Sticker = new() { Path = "media/logo.bmp", Timing = new() { Start = .2, Duration = 1.3, Fade = .2 } };
        project.Canvas.SoftRegion = new() { Enabled = true, Timing = new() { Start = .3, Duration = 1.1, Fade = .2 } };
        project.Canvas.Intro = "zoom";
        var effectsOutput = Path.Combine(root, "effects.mp4");
        await new ExportService(ffmpeg).ExportAsync(project, root, new(), effectsOutput, CancellationToken.None);
        if ((await FfmpegTools.Probe(ffprobe, effectsOutput))?.Duration < 1.5) throw new InvalidOperationException("时段装饰导出被截断。");
        await CheckRenderedPixels(ffmpeg, effectsOutput, root, project);
        var protectedOutput = Path.Combine(root, "keep-existing.mp4");
        await File.WriteAllTextAsync(protectedOutput, "existing-export");
        try
        {
            await new ExportService(ffmpeg).ExportAsync(project, root, new(), protectedOutput, new CancellationToken(true));
            throw new InvalidOperationException("取消导出没有停止。");
        }
        catch (OperationCanceledException) { }
        if (await File.ReadAllTextAsync(protectedOutput) != "existing-export" || Directory.EnumerateFiles(root, ".DogSC-*").Any())
            throw new InvalidOperationException("取消导出覆盖了旧文件或留下半成品。");
        Console.WriteLine("SELFTEST_OK timed-sticker/soft/fades/perspective/intro");
        Console.WriteLine("SELFTEST_OK color/projection-edges/cancel-keeps-existing");
        await CheckMacImportAsync(ffmpeg, ffprobe, root, source);
        var monitor = Native.Monitors().First();
        var grabPath = Path.Combine(root, "grab.mp4");
        var region = Native.EvenRegion(monitor.Left, monitor.Top, Math.Min(640, monitor.Width), Math.Min(360, monitor.Height));
        using var grab = FfmpegProcess.Start(ffmpeg, [
            "-y", "-f", "gdigrab", "-framerate", "15",
            "-offset_x", region.X.ToString(), "-offset_y", region.Y.ToString(),
            "-video_size", region.Width + "x" + region.Height,
            "-i", "desktop", "-t", "1",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", grabPath,
        ]);
        var grabCode = await grab.WaitAsync();
        var grabInfo = grabCode == 0 ? await FfmpegTools.Probe(ffprobe, grabPath) : null;
        if (grabInfo is null || grabInfo.Width < 64)
        {
            Console.Error.WriteLine("SELFTEST_FAIL 屏幕采集失败。");
            Console.Error.WriteLine(grab.ErrorText());
            return 1;
        }

        Console.WriteLine($"SELFTEST_OK capture {grabInfo.Width}x{grabInfo.Height}");
        return 0;
    }

    static async Task CheckMacImportAsync(string ffmpeg, string ffprobe, string root, string source)
    {
        var package = Path.Combine(root, "mac-input");
        Directory.CreateDirectory(package);
        using (var mov = FfmpegProcess.Start(ffmpeg, ["-y", "-i", source, "-c", "copy", Path.Combine(package, "screen.mov")]))
            if (await mov.WaitAsync() != 0) throw new InvalidOperationException(mov.ErrorText());
        await File.WriteAllTextAsync(Path.Combine(package, "pointer.jsonl"), """
            {"time":0,"location":{"x":0.2,"y":0.2},"kind":"move"}
            {"time":0.5,"location":{"x":0.5,"y":0.5},"kind":"leftClick"}
            """);
        await File.WriteAllTextAsync(Path.Combine(package, "project.json"), """
            {"version":27,"title":"import-selftest","media":{"screen":{"relativePath":"screen.mov"},"pointerEvents":{"relativePath":"pointer.jsonl"}},
             "timeline":{"sourceSequence":{"kind":"fullRecording"}},"recordingMarkers":[{"sourceTime":0.5,"number":1}]}
            """);
        var previousDirectory = SettingsStore.Current.ProjectsDirectory;
        var previousRecent = SettingsStore.Current.RecentProjects.ToList();
        try
        {
            SettingsStore.Current.ProjectsDirectory = Path.Combine(root, "imports");
            var imported = await ProjectFactory.ImportMacAsync(ffmpeg, ffprobe, package);
            var importedProject = imported.Result.Project;
            if (importedProject.Assets[0].SystemAudio is null || importedProject.Assets[0].Cursor.Count != 2 || importedProject.Markers.Count != 1)
                throw new InvalidOperationException("Mac 音频、指针或标记迁移异常。");
            var output = Path.Combine(root, "mac-export.mp4");
            await new ExportService(ffmpeg).ExportAsync(importedProject, imported.Directory, new ExportOptions(), output, CancellationToken.None);
            if ((await FfmpegTools.Probe(ffprobe, output))?.HasAudio != true) throw new InvalidOperationException("Mac 导入后导出丢失声音。");
            Console.WriteLine("SELFTEST_OK mac-import audio/pointer/markers/export");
        }
        finally
        {
            SettingsStore.Current.ProjectsDirectory = previousDirectory;
            SettingsStore.Current.RecentProjects = previousRecent;
            SettingsStore.Save();
        }
    }

    static async Task CheckRenderedPixels(string ffmpeg, string video, string root, ProjectDocument project)
    {
        var path = Path.Combine(root, "rendered.png");
        try
        {
            using var extract = FfmpegProcess.Start(ffmpeg, ["-y", "-ss", "1", "-i", video, "-frames:v", "1", path]);
            if (await extract.WaitAsync() != 0) throw new InvalidOperationException(extract.ErrorText());
            var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var image = new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
            image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            var colored = 0;
            for (var i = 0; i < pixels.Length; i += 4)
                if (Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) - Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2])) > 40) colored++;
            if (colored < image.PixelWidth * image.PixelHeight * .02) throw new InvalidOperationException("柔化后成片丢失颜色。");
            var placement = CanvasLayout.Place(image.PixelWidth, image.PixelHeight, project.Width, project.Height, project.Canvas);
            var corner = ((placement.Y + 1) * image.PixelWidth + placement.X + 1) * 4;
            if (pixels[corner] < 180 || pixels[corner + 1] < 180 || pixels[corner + 2] < 180)
                throw new InvalidOperationException("透视四角外侧出现拉伸填充。");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
