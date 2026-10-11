using DogSC;
using DogSC.Core;
using System.IO;

internal static class ReportExportCheck
{
    // The report failed around 31% on a 34.6 s, 1600 × 1200 project.
    // This fixture overlaps zoom, perspective and blur at that time; it uses
    // an explicit source argument and never edits the user's project.
    public static async Task RunAsync(string source)
    {
        SettingsStore.Load();
        var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
        var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg) ?? throw new InvalidOperationException("FFprobe unavailable");
        var directory = Path.Combine(Path.GetTempPath(), "DogSC-report-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const double duration = 34.6;
            var asset = new MediaAsset { Video = Path.GetFullPath(source), Width = 1600, Height = 1200, Duration = duration };
            var project = new ProjectDocument
            {
                Width = 1600, Height = 1200, FrameRate = 30, Assets = [asset],
                Clips = [new() { AssetId = asset.Id, SourceEnd = duration }],
                Canvas = new() { Padding = 72, Corner = 24, Shadow = 24, Wallpaper = "pattern:obsidianGrid", Cursor = new() { Mode = "hidden" } },
                Zooms = [new() { Start = 8, Duration = 7, Scale = 1.6, FocusX = .7, FocusY = .6, Origin = "manual" },
                    new() { Start = 22, Duration = 5, Scale = 2, FocusX = .3, FocusY = .4, Origin = "manual" }],
                ScreenMotions = [new() { Timing = new() { StartTime = 9, Duration = 4, LeadInDuration = .7, ReturnDuration = .7, Easing = "quintic" },
                    Target = new() { RotationX = 12, RotationY = -15, Scale = .9 } }],
            };
            project.Canvas.SoftRegions = [new() { Enabled = true, Strength = .6, CoordinateSpace = "source", X = .2, Y = .2, Width = .4, Height = .3,
                Timing = new() { Start = 10, Duration = 12, FadeIn = .3, FadeOut = .3 } }];
            var exporter = new ExportService(ffmpeg);
            var lastPercent = -1;
            exporter.Progress += progress =>
            {
                var percent = (int)(progress * 10) * 10;
                if (percent == lastPercent) return;
                lastPercent = percent; Console.WriteLine("REPORT_EXPORT " + percent + "%");
            };
            var output = Path.Combine(directory, "report.mp4");
            await exporter.ExportAsync(project, directory, new() { Fps = 30, Resolution = "source" }, output, CancellationToken.None);
            var media = await FfmpegTools.Probe(ffprobe, output);
            if (media is null || media.Width != 1600 || media.Height != 1200 || Math.Abs(media.Duration - duration) > .1)
                throw new InvalidOperationException("Report export has incorrect dimensions or duration: " + media);
            Console.WriteLine("REPORT_EXPORT_OK 1600x1200 / 34.6s / zoom + perspective + source blur");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
