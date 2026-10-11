using DogSC;
using DogSC.Core;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class CameraMorphChecks
{
    public static async Task RunAsync(Grid host)
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
        var root = Path.Combine(Path.GetTempPath(), "DogSC-camera-morph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        async Task Run(params string[] args)
        {
            using var process = FfmpegProcess.Start(ffmpeg, args);
            if (await process.WaitAsync() != 0) throw new InvalidOperationException(process.ErrorText());
        }
        try
        {
            const int width = 320, height = 180;
            const double duration = 2.7;
            var screen = Path.Combine(root, "screen.mp4"); var camera = Path.Combine(root, "camera.mp4");
            await Run("-y", "-f", "lavfi", "-i", "color=0x101010:s=320x180:r=60:d=2.7", "-c:v", "libx264", screen);
            await Run("-y", "-f", "lavfi", "-i", "nullsrc=s=480x360:r=60:d=2.7,format=gbrp,geq=r='40+180*X/W':g='50+180*Y/H':b=200",
                "-c:v", "libx264", "-crf", "8", "-pix_fmt", "yuv444p", camera);
            var checkedFrames = 0; var worstCenter = 0d; var worstColor = 0d;
            foreach (var fps in new[] { 30, 60 })
            foreach (var mirrored in new[] { false, true })
            {
                var asset = new MediaAsset { Video = screen, Camera = camera, Width = width, Height = height, Duration = duration };
                CameraMotionClip Clip(double start, double length, double entry, string layout, double size, double x, double y, double opacity = 1) => new()
                {
                    Timing = new() { StartTime = start, Duration = length, LeadInDuration = entry, Easing = "cubic" },
                    Target = new() { Layout = layout, Size = size, X = x, Y = y, Roundness = .3, Opacity = opacity },
                };
                var project = new ProjectDocument { Width = width, Height = height, Assets = [asset],
                    Clips = [new() { AssetId = asset.Id, SourceEnd = duration }],
                    Canvas = new() { Padding = 0, Corner = 0, Shadow = 0, Background = "#101010", Cursor = new() { Mode = "hidden" } },
                    Camera = new() { Size = .3, CenterX = .5, CenterY = .5, Shape = "circle", Entry = "none",
                        ContentScale = 1.35, ContentX = .65, ContentY = .25, Mirrored = mirrored },
                    CameraMotions = [Clip(.1, .55, .4, "horizontal", .38, .5, .5),
                        Clip(.65, .55, .4, "fullscreen", .8, .5, .5), Clip(1.2, .5, .3, "vertical", .25, .35, .45),
                        Clip(1.7, .4, .25, "square", .4, .1, .1, 0), Clip(2.1, .3, .2, "circle", .3, .7, .6)] };
                if (fps == 30) await CheckPreviewAsync(host, project);
                var output = Path.Combine(root, "camera-" + fps + "-" + mirrored + ".mp4");
                var raw = Path.Combine(root, "frames.rgb");
                await new ExportService(ffmpeg).ExportAsync(project, root, new() { Fps = fps }, output, CancellationToken.None);
                try
                {
                    await Run("-y", "-i", output, "-pix_fmt", "rgb24", "-f", "rawvideo", raw);
                    var pixels = await File.ReadAllBytesAsync(raw);
                    var count = (int)Math.Round(duration * fps);
                    if (pixels.Length != count * width * height * 3)
                        throw new InvalidOperationException($"Camera export changed frame count: {pixels.Length / (width * height * 3)} expected {count}");
                    var track = new CameraSceneTrack(project, width, height, 4d / 3);
                    for (var frame = 0; frame < count; frame++)
                    {
                        var scene = track.At(frame / (double)fps, 0, duration);
                        var offset = frame * width * height * 3;
                        double actualArea = 0, actualX = 0, actualY = 0, area = 0, sumX = 0, sumY = 0, colorError = 0;
                        var colors = 0;
                        for (var y = 0; y < height; y++)
                        for (var x = 0; x < width; x++)
                        {
                            var i = offset + (y * width + x) * 3;
                            var coverage = scene.Coverage(x + .5, y + .5);
                            var observed = Math.Clamp((pixels[i + 2] - 16) / 184d, 0, 1);
                            area += coverage; sumX += (x + .5) * coverage; sumY += (y + .5) * coverage;
                            actualArea += observed; actualX += (x + .5) * observed; actualY += (y + .5) * observed;
                            if (scene.Frame.Opacity < .2 || x % 5 != 0 || y % 5 != 0 || coverage < scene.Frame.Opacity * .99
                                || scene.Coverage(x - 2.5, y + .5) < coverage * .99 || scene.Coverage(x + 3.5, y + .5) < coverage * .99
                                || scene.Coverage(x + .5, y - 2.5) < coverage * .99 || scene.Coverage(x + .5, y + 3.5) < coverage * .99) continue;
                            var source = scene.SourcePoint(x + .5, y + .5);
                            var expectedR = 16 + (40 + 180 * source.X - 16) * coverage;
                            var expectedG = 16 + (50 + 180 * source.Y - 16) * coverage;
                            colorError += Math.Abs(pixels[i] - expectedR) + Math.Abs(pixels[i + 1] - expectedG); colors += 2;
                        }
                        if (scene.Frame.Opacity < .000001)
                        {
                            var peakBlue = Enumerable.Range(0, width * height).Max(p => pixels[offset + p * 3 + 2]);
                            if (peakBlue > 22) throw new InvalidOperationException($"Hidden camera left visible pixels on {fps}fps frame {frame}: blue={peakBlue}");
                            continue;
                        }
                        var centerError = scene.Frame.Opacity > .15
                            ? Math.Sqrt(Math.Pow(actualX / actualArea - sumX / area, 2) + Math.Pow(actualY / actualArea - sumY / area, 2)) : 0;
                        worstCenter = Math.Max(worstCenter, centerError);
                        // Integrated opacity near a fade endpoint is sensitive to a
                        // two-code-value H.264 blue-channel error over the aperture.
                        var areaTolerance = Math.Max(40, area * .035 + scene.Frame.W * scene.Frame.H * 2 / 184);
                        if (scene.Frame.Opacity > .15 && centerError > 1 || Math.Abs(actualArea - area) > areaTolerance)
                            throw new InvalidOperationException($"{fps}fps camera frame {frame} shape out of phase: center error={centerError:0.00}px, area={actualArea:0.0} expected {area:0.0}");
                        if (colors > 0)
                        {
                            var mean = colorError / colors; worstColor = Math.Max(worstColor, mean);
                            if (mean > 5) throw new InvalidOperationException($"{fps}fps mirrored={mirrored} frame {frame} source crop/zoom differs: mean RGB error={mean:0.00}");
                        }
                        checkedFrames++;
                    }
                }
                finally { File.Delete(raw); File.Delete(output); }
            }
            await CheckSourcesAsync(ffmpeg, root, screen, camera, Run);
            await CheckBackdropAsync(ffmpeg, root, screen, Run);
            Console.WriteLine($"CAMERA_MORPH_OK {checkedFrames} real 30/60fps frames: circle/horizontal/fullscreen/vertical, continuous corners, hidden-in-place and return, source aspect/crop/zoom/mirror; worst center={worstCenter:0.00}px, mean RGB={worstColor:0.00}, exact frame counts; WPF shape/crop preview; native opening, delayed camera, missing camera, portrait source and speed");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static async Task CheckBackdropAsync(string ffmpeg, string root, string screen, Func<string[], Task> run)
    {
        var camera = Path.Combine(root, "checker-camera.mp4"); var sticker = Path.Combine(root, "sticker.bmp");
        await run(["-y", "-f", "lavfi", "-i", "nullsrc=s=480x360:r=30:d=1.2,format=gbrp,geq=r='if(mod(floor(X/8)+floor(Y/8),2),220,20)':g=70:b=200",
            "-c:v", "libx264", "-crf", "8", "-pix_fmt", "yuv444p", camera]);
        var image = Enumerable.Repeat((byte)255, 16 * 16 * 4).ToArray(); MaskImage.WriteBmp(sticker, image, 16, 16);
        var asset = new MediaAsset { Video = screen, Camera = camera, Duration = 1.2 };
        var project = new ProjectDocument { Width = 320, Height = 180, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 1.2 }],
            Canvas = new() { Padding = 0, Corner = 0, Shadow = 0, Cursor = new() { Mode = "hidden" } },
            Camera = new() { Size = .3, CenterX = .5, CenterY = .5, Shape = "circle", Entry = "none" },
            CameraMotions = [new() { Timing = new() { StartTime = 0, Duration = 1.2 }, Target = new() { Layout = "circle", Size = .3, X = .5, Y = .5, Opacity = .6 } }] };
        foreach (var mode in new[] { "hide", "blur" })
        {
            project.Canvas.Stickers = [new() { Path = sticker, X = .05, Y = .9, Width = .05, Animation = "none",
                Timing = new() { Start = .4, Duration = .4 }, HidesCamera = mode == "hide", BackdropBlur = mode == "blur" ? 12 : 0,
                BackdropBlurIncludesCamera = mode == "blur", ShadowRadius = 0, ShadowOpacity = 0 }];
            var output = Path.Combine(root, "camera-" + mode + ".mp4"); var raw = Path.Combine(root, "backdrop.rgb");
            await new ExportService(ffmpeg).ExportAsync(project, root, new(), output, CancellationToken.None);
            try
            {
                await run(["-y", "-i", output, "-pix_fmt", "rgb24", "-f", "rawvideo", raw]);
                var pixels = await File.ReadAllBytesAsync(raw);
                int Pixel(int frame, int x, int channel) => pixels[(frame * 320 * 180 + 90 * 320 + x) * 3 + channel];
                var before = Pixel(6, 160, 2); var during = Pixel(18, 160, 2); var after = Pixel(30, 160, 2);
                if (Math.Abs(before - 126) > 7 || Math.Abs(after - 126) > 7)
                    throw new InvalidOperationException("Sticker changed camera visibility outside its interval");
                if (mode == "hide" && during > 22) throw new InvalidOperationException("Sticker did not suppress the moving camera");
                if (mode == "blur")
                {
                    double Detail(int frame) => Enumerable.Range(148, 24).Average(x => Math.Abs(Pixel(frame, x, 0) - Pixel(frame, x - 1, 0)));
                    if (Math.Abs(during - 126) > 7) throw new InvalidOperationException("Camera blur changed its authored opacity");
                    if (Detail(6) < 15 || Detail(18) > Detail(6) * .25)
                        throw new InvalidOperationException($"Sticker camera blur did not reduce actual detail: {Detail(6):0.0}->{Detail(18):0.0}");
                }
            }
            finally { File.Delete(raw); File.Delete(output); }
        }
        Console.WriteLine("CAMERA_BACKDROP_OK moving camera suppression, blur detail reduction, authored opacity and interval restore");
    }

    static async Task CheckSourcesAsync(string ffmpeg, string root, string screen, string camera, Func<string[], Task> run)
    {
        var portrait = Path.Combine(root, "portrait.mp4");
        await run(["-y", "-f", "lavfi", "-i", "nullsrc=s=360x480:r=60:d=2,format=gbrp,geq=r='40+180*X/W':g='50+180*Y/H':b=200",
            "-c:v", "libx264", "-crf", "8", "-pix_fmt", "yuv444p", portrait]);
        var a = new MediaAsset { Video = screen, Camera = camera, CameraOffset = -.2, Duration = 2.7 };
        var gap = new MediaAsset { Video = screen, Duration = 2.7 };
        var b = new MediaAsset { Video = screen, Camera = portrait, Duration = 2.7 };
        var project = new ProjectDocument { Width = 320, Height = 180, Assets = [a, gap, b],
            Clips = [new() { AssetId = a.Id, SourceEnd = .8 }, new() { AssetId = gap.Id, SourceEnd = .4 },
                new() { AssetId = b.Id, SourceStart = .2, SourceEnd = 1.2, Speed = 2 }],
            Canvas = new() { Padding = 0, Corner = 0, Shadow = 0, Background = "#101010", Cursor = new() { Mode = "hidden" },
                Opening = "converge", OpeningDuration = .45, OpeningOrder = ["camera", "screen", "stickers"] },
            Camera = new() { Shape = "original", Corner = .18, Size = .3, CenterX = .5, CenterY = .5, Entry = "none", ContentX = .65, ContentY = .25 } };
        var output = Path.Combine(root, "camera-sources.mp4"); var raw = Path.Combine(root, "source-frames.rgb");
        await new ExportService(ffmpeg).ExportAsync(project, root, new() { Fps = 60 }, output, CancellationToken.None);
        try
        {
            await run(["-y", "-i", output, "-pix_fmt", "rgb24", "-f", "rawvideo", raw]);
            var pixels = await File.ReadAllBytesAsync(raw);
            if (pixels.Length != 102 * 320 * 180 * 3) throw new InvalidOperationException("Camera source switches changed timeline duration");
            foreach (var time in new[] { .1, .3, .55, .78, .85, 1.1, 1.22, 1.4, 1.65 })
            {
                var frame = (int)Math.Round(time * 60); var offset = frame * 320 * 180 * 3;
                var cameraVisible = time >= .2 && time < .8 || time >= 1.2;
                if (!cameraVisible)
                {
                    if (Enumerable.Range(0, 320 * 180).Max(i => pixels[offset + i * 3 + 2]) > 22)
                        throw new InvalidOperationException("Camera from another source leaked into its delayed/missing interval at " + time);
                    continue;
                }
                var aspect = time < .8 ? 4d / 3 : 3d / 4;
                var start = time < .8 ? 0 : 1.2; var end = time < .8 ? .8 : 1.7;
                var scene = new CameraSceneTrack(project, 320, 180, aspect).At(frame / 60d, start, end);
                var x = (int)(scene.Frame.X + scene.Frame.W * .65); var y = (int)(scene.Frame.Y + scene.Frame.H * .55);
                var source = scene.SourcePoint(x + .5, y + .5); var i = offset + (y * 320 + x) * 3;
                var r = 16 + (40 + 180 * source.X - 16) * scene.Frame.Opacity;
                var g = 16 + (50 + 180 * source.Y - 16) * scene.Frame.Opacity;
                if (Math.Abs(pixels[i] - r) > 7 || Math.Abs(pixels[i + 1] - g) > 7)
                    throw new InvalidOperationException($"Camera source aspect/opening phase mismatch at {time}: RGB={pixels[i]},{pixels[i+1]} expected {r:0.0},{g:0.0}");
            }
        }
        finally { File.Delete(raw); File.Delete(output); }
    }

    static async Task CheckPreviewAsync(Grid host, ProjectDocument project)
    {
        var editor = new EditorWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(EditorWindow).GetField(name, flags)!.SetValue(editor, value);
        T Element<T>(string name) where T : FrameworkElement => (T)editor.FindName(name);
        var player = (PreviewPlayer)typeof(EditorWindow).GetField("_player", flags)!.GetValue(editor)!;
        try
        {
            Set("_project", project); Set("_pvFrameScale", 1d); Set("_pvOutW", 320); Set("_pvOutH", 180); Set("_cameraMaskAspect", 4d / 3);
            player.CameraDrawing.Rect = new Rect(0, 0, 480, 360);
            var scene = Element<Canvas>("SceneCanvas"); ((Panel)scene.Parent).Children.Remove(scene);
            host.Children.Add(scene); scene.Width = 320; scene.Height = 180;
            scene.HorizontalAlignment = HorizontalAlignment.Left; scene.VerticalAlignment = VerticalAlignment.Top;
            var baseScene = Element<Canvas>("BaseScene"); baseScene.Width = 320; baseScene.Height = 180; baseScene.Children.Clear();
            baseScene.Background = new SolidColorBrush(Color.FromRgb(16, 16, 16));
            Element<Canvas>("FrameHost").Width = 320; Element<Canvas>("FrameHost").Height = 180;
            var bubble = Element<System.Windows.Shapes.Rectangle>("CameraBubble"); bubble.Visibility = Visibility.Visible; bubble.Stroke = null;
            var texture = new byte[480 * 360 * 4];
            for (var y = 0; y < 360; y++) for (var x = 0; x < 480; x++)
            {
                var i = (y * 480 + x) * 4; texture[i] = 200; texture[i + 1] = (byte)(50 + 180 * y / 360d);
                texture[i + 2] = (byte)(40 + 180 * x / 480d); texture[i + 3] = 255;
            }
            var bitmap = BitmapSource.Create(480, 360, 96, 96, PixelFormats.Bgra32, null, texture, 480 * 4);
            bubble.Fill = new DrawingBrush { Drawing = new ImageDrawing(bitmap, new Rect(0, 0, 480, 360)), Stretch = Stretch.Fill };
            var track = new CameraSceneTrack(project, 320, 180, 4d / 3);
            foreach (var time in new[] { .35, .8, 1.4, 1.8, 2.3 })
            {
                typeof(PreviewPlayer).GetProperty(nameof(PreviewPlayer.Time))!.SetValue(player, time);
                bubble.Opacity = 1;
                typeof(EditorWindow).GetMethod("ApplyMotionVisual", flags)!.Invoke(editor, null);
                var expected = track.At(time, 0, 2.7);
                if (Math.Abs(bubble.Width - expected.Frame.W) > .01 || Math.Abs(bubble.Height - expected.Frame.H) > .01
                    || Math.Abs(bubble.RadiusX - expected.Radius) > .01 || Math.Abs(bubble.Opacity - expected.Frame.Opacity) > .01)
                    throw new InvalidOperationException("WPF camera shape differs from export at " + time);
                scene.Measure(new(320, 180)); scene.Arrange(new(0, 0, 320, 180)); scene.UpdateLayout();
                await Task.Delay(25);
                var image = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32); image.Render(scene);
                var x = (int)Math.Round(expected.Frame.X + expected.Frame.W * .6);
                var y = (int)Math.Round(expected.Frame.Y + expected.Frame.H * .55);
                var color = new byte[4]; image.CopyPixels(new Int32Rect(x, y, 1, 1), color, 4, 0);
                var source = expected.SourcePoint(x + .5, y + .5);
                var alpha = expected.Frame.Opacity;
                var origin = bubble.TransformToAncestor(scene).Transform(new Point(0, 0));
                if (Math.Abs(origin.X - expected.Frame.X) > .01 || Math.Abs(origin.Y - expected.Frame.Y) > .01)
                    throw new InvalidOperationException("Resizing camera shifted within its previous Canvas layout slot");
                if (Math.Abs(color[2] - (16 + (40 + 180 * source.X - 16) * alpha)) > 4
                    || Math.Abs(color[1] - (16 + (50 + 180 * source.Y - 16) * alpha)) > 4)
                    throw new InvalidOperationException($"WPF mirrored={project.Camera.Mirrored} crop pixels differ at {time}: RGB={color[2]},{color[1]}, expected={16 + (40 + 180 * source.X - 16) * alpha:0.0},{16 + (50 + 180 * source.Y - 16) * alpha:0.0}, source={source}, bounds={expected.Frame}, viewbox={((DrawingBrush)bubble.Fill).Viewbox}, transform={bubble.RenderTransform.Value}, arranged={bubble.RenderSize}, left/top={Canvas.GetLeft(bubble)},{Canvas.GetTop(bubble)}, scene={scene.RenderTransform.Value}, brush={((DrawingBrush)bubble.Fill).Transform.Value}, relative={((DrawingBrush)bubble.Fill).RelativeTransform.Value}");
            }
        }
        finally { player.Dispose(); host.Children.Clear(); }
    }
}
