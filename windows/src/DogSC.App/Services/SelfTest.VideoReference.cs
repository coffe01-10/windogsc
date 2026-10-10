using DogSC.Core;

namespace DogSC;

public static partial class SelfTest
{
    static async Task CheckVideoReferenceAsync(string ffmpeg, string ffprobe, string root)
    {
        var source = Path.Combine(root, "checker.mp4"); var audio = Path.Combine(root, "tone.wav");
        async Task Run(params string[] args)
        {
            using var process = FfmpegProcess.Start(ffmpeg, args);
            if (await process.WaitAsync() != 0) throw new InvalidOperationException(process.ErrorText());
        }
        await Run("-y", "-f", "lavfi", "-i", "nullsrc=s=320x180:r=30:d=1.4,geq=lum='if(mod(floor(X/4)+floor(Y/4),2),235,16)':cb=128:cr=128",
            "-c:v", "libx264", "-crf", "12", "-pix_fmt", "yuv420p", source);
        await Run("-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1.4", audio);
        var asset = new MediaAsset { Video = source, SystemAudio = audio, Width = 320, Height = 180, Duration = 1.4 };
        var project = new ProjectDocument { Width = 320, Height = 180, Assets = [asset],
            Clips = [new() { AssetId = asset.Id, SourceEnd = .7, SystemGain = .82, SystemEnabled = false }, new() { AssetId = asset.Id, SourceStart = .7, SourceEnd = 1.4, SystemGain = .5, SystemEnabled = true }],
            SystemEnabled = false, Canvas = new() { Padding = 0, Corner = 0, Shadow = 0, ScreenBorderWidth = 16 } };
        var video = Path.Combine(root, "linear-band.mp4"); var m4a = Path.Combine(root, "clip-volume.m4a");
        var raw = Path.Combine(root, "linear-band.gray"); var pcm = Path.Combine(root, "clip-volume.pcm");
        try
        {
            var motion = new ScreenMotionClip { LinearBlur = true, LinearBlurSize = .3, LinearBlurSoftness = .4, LinearBlurStrength = .8,
                Timing = new() { StartTime = .1, Duration = 1, LeadInDuration = .1, ReturnDuration = .2 } };
            project.ScreenMotions = [motion];
            await new ExportService(ffmpeg).ExportAsync(project, root, new() { Resolution = "source", Fps = 30 }, video, CancellationToken.None);
            await Run("-y", "-ss", "0.5", "-i", video, "-frames:v", "1", "-pix_fmt", "gray", "-f", "rawvideo", raw);
            var pixels = await File.ReadAllBytesAsync(raw); File.Delete(raw);
            double Detail(int row) => Enumerable.Range(25, 270).Average(x => Math.Abs(pixels[row * 320 + x] - pixels[row * 320 + x - 1]));
            var clear = Detail(90); var soft = Detail(25);
            if (clear < 35 || soft > clear * .3)
                throw new InvalidOperationException($"线性柔化清晰带异常：中心细节={clear:0.0}，外围={soft:0.0}");
            // The white border must survive projection and output composition.
            if (pixels[90 * 320 + 1] < 200) throw new InvalidOperationException("屏幕描边未进入成片。");
            motion.Target.RotationY = 20;
            await new ExportService(ffmpeg).ExportAsync(project, root, new() { Resolution = "source", Fps = 30 }, video, CancellationToken.None);
            await Run("-y", "-ss", "0.5", "-i", video, "-frames:v", "1", "-pix_fmt", "gray", "-f", "rawvideo", raw);
            var tiltedPixels = await File.ReadAllBytesAsync(raw); File.Delete(raw);
            var tiltDifference = Enumerable.Range(0, pixels.Length).Average(i => Math.Abs(pixels[i] - tiltedPixels[i]));
            if (tiltDifference < 5) throw new InvalidOperationException($"屏幕 Y 轴倾斜未改变导出画面：平均像素差={tiltDifference:0.0}");
            motion.Target.Scale = .85;
            project.Zooms = [new() { Start = .2, Duration = .6, Scale = 1.6, Origin = "manual" }];
            await new ExportService(ffmpeg).ExportAsync(project, root, new() { Resolution = "source", Fps = 30 }, video, CancellationToken.None);
            var info = await FfmpegTools.Probe(ffprobe, video);
            if (info is null || Math.Abs(info.Duration - 1.4) > .1) throw new InvalidOperationException("3D 与缩放叠加后导出时长改变。");
            await new ExportService(ffmpeg).ExportAsync(project, root, new() { AudioOnly = true }, m4a, CancellationToken.None);
            await Run("-y", "-i", m4a, "-ac", "1", "-ar", "8000", "-f", "s16le", pcm);
            var samples = await File.ReadAllBytesAsync(pcm); File.Delete(pcm);
            double Rms(double from, double to)
            {
                var start = (int)(from * 8000); var end = Math.Min(samples.Length / 2, (int)(to * 8000));
                return Math.Sqrt(Enumerable.Range(start, end - start).Average(i => Math.Pow(BitConverter.ToInt16(samples, i * 2), 2)));
            }
            if (Rms(.1, .5) > 5 || Rms(.9, 1.2) < 700)
                throw new InvalidOperationException("片段静音或片段音量未进入 M4A。");
            Console.WriteLine($"SELFTEST_OK video-reference linear-clear-band({clear:0.0}/{soft:0.0})/border/3D-tilt({tiltDifference:0.0})/3D+zoom/clip-mute+gain/M4A");
        }
        finally
        {
            foreach (var path in new[] { source, audio, video, m4a, raw, pcm }) if (File.Exists(path)) File.Delete(path);
        }
    }
}
