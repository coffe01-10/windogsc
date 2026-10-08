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
        var source = Path.Combine(root, "media", "screen.mp4");
        var generated = FfmpegProcess.Start(ffmpeg, [
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

        generated.Dispose();
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
        TimelineEdits.AddZoom(project, 0.4);
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
        var monitor = Native.Monitors().First();
        var grabPath = Path.Combine(root, "grab.mp4");
        var region = Native.EvenRegion(monitor.Left, monitor.Top, Math.Min(640, monitor.Width), Math.Min(360, monitor.Height));
        var grab = FfmpegProcess.Start(ffmpeg, [
            "-y", "-f", "gdigrab", "-framerate", "15",
            "-offset_x", region.X.ToString(), "-offset_y", region.Y.ToString(),
            "-video_size", region.Width + "x" + region.Height,
            "-i", "desktop", "-t", "1",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", grabPath,
        ]);
        var grabCode = await grab.WaitAsync();
        var grabInfo = grabCode == 0 ? await FfmpegTools.Probe(ffprobe, grabPath) : null;
        grab.Dispose();
        if (grabInfo is null || grabInfo.Width < 64)
        {
            Console.Error.WriteLine("SELFTEST_FAIL 屏幕采集失败。");
            Console.Error.WriteLine(grab.ErrorText());
            return 1;
        }

        Console.WriteLine($"SELFTEST_OK capture {grabInfo.Width}x{grabInfo.Height}");
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            Console.WriteLine("SELFTEST_NOTE 临时目录未删除：" + root);
        }

        return 0;
    }
}
