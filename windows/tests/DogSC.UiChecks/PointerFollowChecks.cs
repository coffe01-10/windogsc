using DogSC;
using DogSC.Core;
using System.IO;
using System.Globalization;

internal static class PointerFollowChecks
{
    public static async Task RunAsync()
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
        var root = Path.Combine(Path.GetTempPath(), "DogSC-pointer-follow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        async Task Run(params string[] args)
        {
            using var process = FfmpegProcess.Start(ffmpeg, args);
            if (await process.WaitAsync() != 0) throw new InvalidOperationException(process.ErrorText());
        }
        try
        {
            var source = Path.Combine(root, "gradient.mp4");
            await Run("-y", "-f", "lavfi", "-i", "nullsrc=s=320x180:r=30:d=4,geq=lum='16+219*X/W':cb=128:cr=128",
                "-c:v", "libx264", "-crf", "10", "-pix_fmt", "yuv420p", source);
            var referencePath = Path.Combine(root, "reference.rgb");
            byte[] reference;
            try
            {
                await Run("-y", "-i", source, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", referencePath);
                reference = await File.ReadAllBytesAsync(referencePath);
            }
            finally { if (File.Exists(referencePath)) File.Delete(referencePath); }
            var asset = new MediaAsset { Video = source, Width = 320, Height = 180, Duration = 4, Cursor =
                [new() { Time = 0, X = .5, Y = .5 }, new() { Time = .6, X = .5, Y = .5, Click = true },
                 new() { Time = 1, X = .55, Y = .55 }, new() { Time = 1.2, X = .55, Y = .55 },
                 new() { Time = 1.8, X = .9, Y = .55 }, new() { Time = 2.2, X = .9, Y = .55 },
                 new() { Time = 2.8, X = .2, Y = .55 }, new() { Time = 3.3, X = .2, Y = .55 }] };
            var project = new ProjectDocument { Width = 320, Height = 180, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 4 }],
                Zooms = [new() { Start = .2, Duration = 3.2, Scale = 2, Origin = "automatic", EnterDuration = .4, ExitDuration = .3 }],
                Canvas = new() { Padding = 0, Corner = 0, Shadow = 0, Cursor = new() { Mode = "hidden" } } };
            var motion = new ZoomMotion(project);
            foreach (var fps in new[] { 30, 60 })
            {
                var output = Path.Combine(root, "follow-" + fps + ".mp4");
                await new ExportService(ffmpeg).ExportAsync(project, root, new() { Fps = fps }, output, CancellationToken.None);
                foreach (var time in new[] { .8, 1.2, 1.7, 2.2, 2.8, 3.2 })
                {
                    var raw = Path.Combine(root, "frame.rgb");
                    try
                    {
                        await Run("-y", "-i", output, "-vf", "select=eq(n\\," + Math.Round(time * fps).ToString(CultureInfo.InvariantCulture) + ")", "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", raw);
                        var pixels = await File.ReadAllBytesAsync(raw); var index = (90 * 320 + 160) * 3;
                        var pose = motion.At(time);
                        var sx = (int)Math.Round(320 * (.5 + (pose.Scale - 1) * pose.X) / pose.Scale);
                        var sy = (int)Math.Round(180 * (.5 + (pose.Scale - 1) * pose.Y) / pose.Scale);
                        var sample = (sy * 320 + sx) * 3;
                        var expectedR = reference[sample]; var expectedB = reference[sample + 2];
                        if (pixels.Length != 320 * 180 * 3 || Math.Abs(pixels[index] - expectedR) > 4 || Math.Abs(pixels[index + 2] - expectedB) > 4)
                            throw new InvalidOperationException($"{fps}fps pointer export differs from shared preview pose at {time}: RGB {pixels[index]},{pixels[index+2]} expected {expectedR:0.0},{expectedB:0.0}; focus prev/current/next={motion.At(time-1d/fps).X:0.000}/{pose.X:0.000}/{motion.At(time+1d/fps).X:0.000}");
                    }
                    finally { if (File.Exists(raw)) File.Delete(raw); }
                }
                File.Delete(output);
            }
            Console.WriteLine("POINTER_FOLLOW_OK central rest area, boundary following and spring reversal agree with shared preview poses in real 30/60fps exports");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
