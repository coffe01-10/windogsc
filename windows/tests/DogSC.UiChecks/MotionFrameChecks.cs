using DogSC;
using DogSC.Core;
using System.IO;

internal static class MotionFrameChecks
{
    public static async Task RunAsync()
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
        var root = Path.Combine(Path.GetTempPath(), "DogSC-motion-frames-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        async Task Run(params string[] args)
        {
            using var process = FfmpegProcess.Start(ffmpeg, args);
            if (await process.WaitAsync() != 0) throw new InvalidOperationException(process.ErrorText());
        }
        try
        {
            const int width = 320, height = 180;
            const double duration = 1.8;
            var source = Path.Combine(root, "marker.mp4");
            await Run("-y", "-f", "lavfi", "-i", "color=blue:s=320x180:r=60:d=1.8,drawbox=x=190:y=66:w=10:h=10:color=white:t=fill",
                "-c:v", "libx264", "-crf", "8", "-pix_fmt", "yuv420p", source);
            var checkedFrames = 0; var maxError = 0d;
            foreach (var mode in new[] { "3d", "light3D", "zoom", "lift" })
            foreach (var fps in new[] { 30, 60 })
            {
                var asset = new MediaAsset { Video = source, Width = width, Height = height, Duration = duration };
                var project = new ProjectDocument { Width = width, Height = height, Assets = [asset],
                    Clips = [new() { AssetId = asset.Id, SourceEnd = duration }],
                    Canvas = new() { Padding = mode == "zoom" ? 0 : 100, Corner = 0, Shadow = 0, Background = "#101010",
                        Cursor = new() { Mode = "hidden" }, Intro = mode == "zoom" ? "zoom" : "none",
                        Opening = mode is "light3D" or "lift" ? mode : "none", OpeningDuration = .9 } };
                if (mode is "3d" or "light3D") project.ScreenMotions = [new()
                {
                    Timing = new() { StartTime = .13, Duration = 1, LeadInDuration = .65, ReturnDuration = .5, Easing = "cubic" },
                    Target = new() { Scale = .85, RotationX = 18, RotationY = -22, RotationZ = 12,
                        AnchorX = .2, AnchorY = .8, X = .55, Y = .45, Perspective = 1.2 },
                }];
                var file = Path.Combine(root, mode + fps + ".mp4");
                var raw = Path.Combine(root, "frames.rgb");
                await new ExportService(ffmpeg).ExportAsync(project, root, new() { Fps = fps }, file, CancellationToken.None);
                try
                {
                    await Run("-y", "-i", file, "-pix_fmt", "rgb24", "-f", "rawvideo", raw);
                    var pixels = await File.ReadAllBytesAsync(raw);
                    var count = (int)Math.Round(duration * fps);
                    if (pixels.Length != count * width * height * 3)
                        throw new InvalidOperationException($"{mode} {fps}fps lost/repeated frames: {pixels.Length / (width * height * 3)} expected {count}");
                    var placement = CanvasLayout.Place(width, height, width, height, project.Canvas);
                    var viewport = new ScreenAnchorViewport(width, height, placement.Width, placement.Height);
                    var track = new ScreenMotionTrack(project.ScreenMotions);
                    var rest = track.SampleLayout(-1, null, viewport);
                    var sourceQuad = new[] { (0d, 0d), ((double)width, 0d), (0d, (double)height), ((double)width, (double)height) };
                    for (var frame = 0; frame < count; frame++)
                    {
                        var time = frame / (double)fps;
                        var alpha = mode == "lift" ? CanvasLayout.OpeningProgress(project.Canvas, time, 0)
                            : mode == "zoom" ? CanvasLayout.IntroProgress(project.Canvas, time)
                            : OpeningMotion.Sample(project, "screen", time, width, height).Opacity;
                        if (alpha < .3) continue;
                        var x = placement.X + 194.5 / width * placement.Width;
                        var y = placement.Y + 70.5 / height * placement.Height;
                        if (mode is "zoom" or "lift")
                        {
                            var scale = mode == "lift" ? .94 + .06 * alpha : .9 + .1 * alpha;
                            x = placement.X + placement.Width / 2d + (x - placement.X - placement.Width / 2d) * scale;
                            y = placement.Y + placement.Height / 2d + (y - placement.Y - placement.Height / 2d) * scale;
                            if (mode == "lift") y += CanvasLayout.Even((int)Math.Round(placement.Height * .04)) * (1 - alpha);
                        }
                        else
                        {
                            var quad = MotionExport.ScreenCorners(track.SampleLayout(time, null, viewport,
                                project.Motion.ScreenSpringMass, project.Motion.ScreenSpringStiffness, project.Motion.ScreenSpringDamping),
                                rest, width, height, placement.X, placement.Y, placement.Width, placement.Height,
                                OpeningMotion.Sample(project, "screen", time, width, height));
                            (x, y) = MotionExport.Homography.FromQuad(sourceQuad, quad).Apply(x, y);
                        }
                        if (x < 10 || x > width - 10 || y < 10 || y > height - 10) continue;
                        var weight = 0d; var sumX = 0d; var sumY = 0d;
                        var offset = frame * width * height * 3;
                        for (var row = 0; row < height; row++)
                        for (var col = 0; col < width; col++)
                        {
                            var index = offset + (row * width + col) * 3;
                            // Only the white marker has red and green above the dark background.
                            var value = Math.Max(0, Math.Min(pixels[index], pixels[index + 1]) - 50);
                            weight += value; sumX += col * value; sumY += row * value;
                        }
                        if (weight < 100) throw new InvalidOperationException($"{mode} {fps}fps marker disappeared on frame {frame}; alpha={alpha:0.000}, peak red={Enumerable.Range(0, width * height).Max(p => pixels[offset + p * 3])}, frame peaks={string.Join(',', Enumerable.Range(0, Math.Min(count, 24)).Select(f => Enumerable.Range(0, width * height).Max(p => pixels[(f * width * height + p) * 3])))}");
                        var error = Math.Sqrt(Math.Pow(sumX / weight - x, 2) + Math.Pow(sumY / weight - y, 2));
                        maxError = Math.Max(maxError, error);
                        if (error > 1.8) throw new InvalidOperationException($"{mode} {fps}fps frame {frame} is out of phase: marker {sumX / weight:0.00},{sumY / weight:0.00} expected {x:0.00},{y:0.00}, error {error:0.00}");
                        var peak = Enumerable.Range(0, width * height).Max(p => pixels[offset + p * 3]);
                        var expectedPeak = 16 + (255 - 16) * alpha;
                        if (Math.Abs(peak - expectedPeak) > 15)
                            throw new InvalidOperationException($"{mode} {fps}fps frame {frame} opacity differs from preview: white={peak}, expected={expectedPeak:0.0}");
                        checkedFrames++;
                    }
                    // A zero-padding shrinking intro must expose background, not repeat its blue border.
                    if (mode == "zoom")
                    {
                        var edge = ((fps / 5) * width * height + 90 * width + 2) * 3;
                        if (pixels[edge + 2] > 35) throw new InvalidOperationException("Projection repeated the source edge over the transparent opening margin");
                    }
                }
                finally { File.Delete(raw); File.Delete(file); }
            }
            Console.WriteLine($"MOTION_FRAMES_OK {checkedFrames} actual 30/60fps frames match preview poses, max marker error={maxError:0.00}px; 3D entry/return, simultaneous native opening, zoom/lift opening, opacity, exact frame counts and transparent edges");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
