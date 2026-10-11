using DogSC;
using DogSC.Core;
using System.IO;
using System.Windows.Controls;

internal static class CameraDecorationChecks
{
    public static async Task RunAsync(Grid host)
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
        var root = Path.Combine(Path.GetTempPath(), "DogSC-camera-decoration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        async Task Run(params string[] args)
        {
            using var process = FfmpegProcess.Start(ffmpeg, args);
            if (await process.WaitAsync() != 0) throw new InvalidOperationException(process.ErrorText());
        }
        try
        {
            const int width = 320, height = 180; const double duration = 1.8;
            var screen = Path.Combine(root, "screen.mp4"); var camera = Path.Combine(root, "camera.mp4");
            await Run("-y", "-f", "lavfi", "-i", "color=0x808080:s=320x180:r=60:d=1.8", "-c:v", "libx264", "-crf", "8", screen);
            var blackScreen = Path.Combine(root, "black.mp4");
            await Run("-y", "-f", "lavfi", "-i", "color=black:s=320x180:r=60:d=1.8", "-c:v", "libx264", "-crf", "8", blackScreen);
            await Run("-y", "-f", "lavfi", "-i", "color=0x2850c8:s=480x360:r=60:d=1.8", "-c:v", "libx264", "-crf", "8", "-pix_fmt", "yuv444p", camera);
            var asset = new MediaAsset { Video = screen, Camera = camera, Width = width, Height = height, Duration = duration };
            var p = new ProjectDocument { Width = width, Height = height, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = duration }],
                Canvas = new() { Padding = 0, Corner = 0, Shadow = 0, Background = "#808080", Cursor = new() { Mode = "hidden" } },
                Camera = new() { Docked = true, Shape = "square", Size = .5, CenterX = .5, CenterY = .35, Corner = .2,
                    Entry = "fade", BorderWidth = 18, ShadowStrength = .8 },
                CameraMotions = [new() { Timing = new() { StartTime = .05, Duration = .55, LeadInDuration = .15 },
                        Target = new() { Layout = "square", Size = .5, X = .5, Y = .35, Roundness = .4, Opacity = .6 } },
                    new() { Timing = new() { StartTime = .7, Duration = .5, LeadInDuration = .2, ReturnDuration = .15 },
                        Target = new() { Layout = "fullscreen", Roundness = 0, Opacity = .6 } }] };
            var worst = 0d; var worstPreview = 0d; var frames = 0; var strongestShadow = 0d; var brightestBorder = 0d;
            foreach (var (fps, opening, border, strength) in new[] { (30, "none", 18d, .8), (60, "none", 18d, .8),
                (60, "light3D", 18d, .8), (30, "none", 18d, 0d), (30, "none", 0d, .8) })
            {
                p.Canvas.Opening = opening; p.Camera.BorderWidth = border; p.Camera.ShadowStrength = strength;
                var background = opening == "none" ? 128 : 0;
                asset.Video = background == 0 ? blackScreen : screen;
                p.Canvas.Background = background == 0 ? "#000000" : "#808080";
                await CameraMorphChecks.CheckPreviewAsync(host, p, [.15, .5, .9, 1.4, 1.55], 128, (image, s) =>
                {
                    var pixels = new byte[width * height * 4]; image.CopyPixels(pixels, width * 4, 0);
                    var shadow = Shadow(s, width, height); var error = 0d; var count = 0;
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                    {
                        var inner = s.ShapeCoverage(x + .5, y + .5);
                        var outer = s.ShapeCoverage(x + .5, y + .5, s.BorderWidth);
                        // Only decoration pixels: the preview texture uses a gradient.
                        if (inner > 0) continue;
                        if (outer == 0 && shadow[y * width + x] < .03) continue;
                        var expected = (255 * outer + 128 * (1 - outer - shadow[y * width + x] * (1 - outer))) * s.Frame.Opacity
                            + 128 * (1 - s.Frame.Opacity);
                        error += Math.Abs(pixels[(y * width + x) * 4 + 2] - expected); count++;
                    }
                    if (count > 0)
                    {
                        worstPreview = Math.Max(worstPreview, error / count);
                        if (error / count > 5) throw new InvalidOperationException($"WPF camera border/shadow pixel mean differs by {error / count:0.00}");
                    }
                });
                var output = Path.Combine(root, "decoration-" + fps + ".mp4"); var raw = Path.Combine(root, "frames.rgb");
                await new ExportService(ffmpeg).ExportAsync(p, root, new() { Fps = fps }, output, CancellationToken.None);
                try
                {
                    await Run("-y", "-i", output, "-pix_fmt", "rgb24", "-f", "rawvideo", raw);
                    var pixels = await File.ReadAllBytesAsync(raw);
                    if (pixels.Length != (int)Math.Round(duration * fps) * width * height * 3)
                        throw new InvalidOperationException("Camera decoration changed the frame count");
                    var track = new CameraSceneTrack(p, width, height, 4d / 3);
                    for (var frame = 0; frame < duration * fps; frame++)
                    {
                        var s = track.At(frame / (double)fps, 0, duration);
                        var shadow = Shadow(s, width, height); var error = 0d; var count = 0;
                        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                        {
                            var inner = s.ShapeCoverage(x + .5, y + .5);
                            var outer = s.ShapeCoverage(x + .5, y + .5, s.BorderWidth);
                            var ring = Math.Max(0, outer - inner);
                            var opacity = s.Frame.Opacity;
                            var alpha = (inner + (ring + shadow[y * width + x] * (1 - ring)) * (1 - inner)) * opacity;
                            var i = (frame * width * height + y * width + x) * 3;
                            var color = new[] { 40, 80, 200 };
                            for (var c = 0; c < 3; c++)
                            {
                                var expected = (color[c] * inner + 255 * ring * (1 - inner)) * opacity + background * (1 - alpha);
                                error += Math.Abs(pixels[i + c] - expected); count++;
                            }
                            if (background == 128 && inner == 0 && ring == 0 && opacity > .5)
                                strongestShadow = Math.Max(strongestShadow, background - pixels[i]);
                            if (ring > .99 && opacity > .15)
                                brightestBorder = Math.Max(brightestBorder, (pixels[i] - background) / ((255d - background) * opacity));
                        }
                        var mean = error / count; worst = Math.Max(worst, mean);
                        if (mean > 3.5) throw new InvalidOperationException($"{fps}fps decoration frame {frame} differs from camera/ring/shadow composition: mean RGB {mean:0.00}");
                        frames++;
                    }
                }
                finally { File.Delete(output); File.Delete(raw); }
            }
            if (strongestShadow < 15 || brightestBorder < .9)
                throw new InvalidOperationException($"Missing visible camera decoration: shadow={strongestShadow}, border={brightestBorder}");
            Console.WriteLine($"CAMERA_DECORATION_OK {frames} real 30/60fps frames: border-only/shadow-only/both, outer white ring, fullscreen border fade, moving Gaussian shadow, shared opacity/entrance and native opening; mean RGB <= {worst:0.00}; WPF decoration mean <= {worstPreview:0.00}, geometry/crop");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Independent Gaussian convolution of the evaluated outer silhouette. Padding
    // includes off-canvas pixels, so a fullscreen shadow is not artificially faded
    // along the frame edge.
    static double[] Shadow(CameraScene scene, int width, int height)
    {
        if (scene.ShadowStrength == 0) return new double[width * height];
        var radius = (int)Math.Ceiling(scene.ShadowRadius * 3);
        var weights = Enumerable.Range(-radius, radius * 2 + 1).Select(i => Math.Exp(-i * i / (2 * scene.ShadowRadius * scene.ShadowRadius))).ToArray();
        var total = weights.Sum(); for (var i = 0; i < weights.Length; i++) weights[i] /= total;
        var horizontal = new double[width * (height + radius * 2)]; var result = new double[width * height];
        for (var y = -radius; y < height + radius; y++) for (var x = 0; x < width; x++)
            for (var dx = -radius; dx <= radius; dx++)
                horizontal[(y + radius) * width + x] += weights[dx + radius]
                    * scene.ShapeCoverage(x + dx + .5, y + .5 - scene.ShadowOffsetY, scene.BorderWidth);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            for (var dy = -radius; dy <= radius; dy++)
                result[y * width + x] += weights[dy + radius] * horizontal[(y + dy + radius) * width + x] * scene.ShadowStrength;
        return result;
    }
}
