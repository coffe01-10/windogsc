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
        catch (Exception ex)
        {
            Console.Error.WriteLine("SELFTEST_FAIL " + ex);
            return 1;
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
        await CheckMultiOverlayAsync(ffmpeg, ffprobe, root);
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

    static async Task CheckMultiOverlayAsync(string ffmpeg, string ffprobe, string root)
    {
        var white = Path.Combine(root, "media", "white.mp4");
        using (var generate = FfmpegProcess.Start(ffmpeg, ["-y", "-f", "lavfi", "-i", "color=white:s=640x360:r=30:d=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", white]))
            if (await generate.WaitAsync() != 0) throw new InvalidOperationException(generate.ErrorText());
        var asset = new MediaAsset { Video = "media/white.mp4", Duration = 2, Width = 640, Height = 360 };
        var project = new ProjectDocument
        {
            Width = 640, Height = 360, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 2 }],
            Canvas = new()
            {
                Padding = 0, Corner = 0, Shadow = 0, Aspect = "8:9", Cursor = new() { Mode = "hidden" },
                Crop = new() { X = .25, Width = .5 },
                SoftRegions = [new() { Enabled = true, CoordinateSpace = "source", Style = "spotlight", Dimming = .5,
                    X = .375, Y = .25, Width = .25, Height = .5, Timing = new() { Start = .2, Duration = 1.4, Transition = "none" } }],
            },
            Zooms = [new() { Start = .8, Duration = .8, Scale = 2 }],
        };
        var output = Path.Combine(root, "source-spotlight.mp4");
        await new ExportService(ffmpeg).ExportAsync(project, root, new(), output, CancellationToken.None);
        async Task<byte> Pixel(double time, double x, double y, string? video = null, int channel = 0)
        {
            var path = Path.Combine(root, "pixel.png");
            try
            {
                using var extract = FfmpegProcess.Start(ffmpeg, ["-y", "-ss", time.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-i", video ?? output, "-frames:v", "1", path]);
                if (await extract.WaitAsync() != 0) throw new InvalidOperationException(extract.ErrorText());
                using var stream = File.OpenRead(path);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var image = new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
                var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
                image.CopyPixels(pixels, image.PixelWidth * 4, 0);
                return pixels[((int)(y * image.PixelHeight) * image.PixelWidth + (int)(x * image.PixelWidth)) * 4 + channel];
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        var before = await Pixel(.1, .1, .5);
        var center = await Pixel(.5, .5, .5);
        var outside = await Pixel(.5, .1, .5);
        var zoomed = await Pixel(1.2, .1, .5);
        var after = await Pixel(1.8, .1, .5);
        if (before < 235 || center < 235 || outside is not (>= 110 and <= 145) || zoomed < 235 || after < 235)
            throw new InvalidOperationException($"源坐标聚光没有正确跟随裁切、缩放或时间窗：before={before}, center={center}, outside={outside}, zoomed={zoomed}, after={after}。");

        foreach (var (name, red, blue) in new[] { ("red.bmp", (byte)255, (byte)0), ("blue.bmp", (byte)0, (byte)255) })
        {
            var pixels = MaskImage.CreateBgra(64, 64, 8, false);
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = blue; pixels[i + 1] = 0; pixels[i + 2] = red; }
            MaskImage.WriteBmp(Path.Combine(root, "media", name), pixels, 64, 64);
        }
        project.Canvas.FocusBlur = .3;
        project.Canvas.Stickers = [new() { Path = "media/red.bmp", X = .2, Y = .2, Width = .15,
            Timing = new() { Start = .1, Duration = 1.5, Transition = "smooth", FadeIn = .4, FadeOut = .1 } },
            new() { Path = "media/blue.bmp", X = .8, Y = .8, Width = .2, Rotation = 25,
                Timing = new() { Start = .4, Duration = 1.4, Transition = "linear", FadeIn = 0, FadeOut = .3 } }];
        project.Canvas.SoftRegions.Add(new() { Enabled = true, CoordinateSpace = "source", X = .3, Y = .3, Width = .1, Height = .1 });
        project.Canvas.SoftRegions.Add(new() { Enabled = true, CoordinateSpace = "canvas", X = .2, Y = .2, Width = .2, Height = .2 });
        var overlays = Path.Combine(root, "multiple-overlays.mp4");
        await new ExportService(ffmpeg).ExportAsync(project, root, new(), overlays, CancellationToken.None);
        if ((await FfmpegTools.Probe(ffprobe, overlays))?.Duration < 1.9)
            throw new InvalidOperationException("多贴图、多柔化导出时长被截断。");
        if (await Pixel(.7, .2, .2, overlays) > 40 || await Pixel(.7, .2, .2, overlays, 2) < 220
            || await Pixel(.7, .8, .8, overlays) < 220 || await Pixel(.7, .8, .8, overlays, 1) > 40)
            throw new InvalidOperationException("多贴图导出漏层、颜色或旋转中心异常。");
        project.Canvas.Stickers[1].X = project.Canvas.Stickers[1].Y = .2;
        var layered = Path.Combine(root, "overlay-order.mp4");
        await new ExportService(ffmpeg).ExportAsync(project, root, new(), layered, CancellationToken.None);
        if (await Pixel(.7, .2, .2, layered) < 220 || await Pixel(.7, .2, .2, layered, 2) > 40)
            throw new InvalidOperationException("贴图图层顺序没有反映到成片。");
        Console.WriteLine("SELFTEST_OK multiple-stickers/rotation/blur/spotlight/source-crop-zoom/independent-transitions");
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
        File.Copy(Path.Combine(root, "media", "logo.bmp"), Path.Combine(package, "logo.bmp"));
        await File.WriteAllTextAsync(Path.Combine(package, "project.json"), """
            {"version":27,"title":"import-selftest","media":{"screen":{"relativePath":"screen.mov"},"pointerEvents":{"relativePath":"pointer.jsonl"}},
             "timeline":{"sourceSequence":{"kind":"fullRecording"},
               "stickerClips":[{"relativePath":"logo.bmp","animation":"fade","timing":{"startTime":0.1,"duration":1.5}},
                 {"relativePath":"logo.bmp","rotationDegrees":20,"animation":"none","position":{"x":0.8,"y":0.8},"timing":{"startTime":0.5,"duration":1}}],
               "mosaicClips":[{"style":"spotlight","sourceRect":{"x":0.2,"y":0.2,"width":0.5,"height":0.5},"transitionStyle":"smooth","timing":{"startTime":0.1,"duration":1.5}}]},
             "recordingMarkers":[{"sourceTime":0.5,"number":1}]}
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
            if (importedProject.Canvas.Stickers.Count != 2 || importedProject.Canvas.SoftRegions.Count != 1
                || importedProject.Canvas.Stickers.Any(x => !File.Exists(ProjectFile.Media(imported.Directory, x.Path))))
                throw new InvalidOperationException("Mac 多效果或贴图文件迁移异常。");
            var output = Path.Combine(root, "mac-export.mp4");
            await new ExportService(ffmpeg).ExportAsync(importedProject, imported.Directory, new ExportOptions(), output, CancellationToken.None);
            if ((await FfmpegTools.Probe(ffprobe, output))?.HasAudio != true) throw new InvalidOperationException("Mac 导入后导出丢失声音。");
            Console.WriteLine("SELFTEST_OK mac-import audio/pointer/markers/multiple-overlays/export");
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
