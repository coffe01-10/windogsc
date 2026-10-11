using DogSC;
using DogSC.Core;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class FocusParityChecks
{
    public static async Task RunAsync(Grid host)
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
        var root = Path.Combine(Path.GetTempPath(), "DogSC-focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        async Task Run(params string[] args)
        {
            using var process = FfmpegProcess.Start(ffmpeg, args);
            if (await process.WaitAsync() != 0) throw new InvalidOperationException(process.ErrorText());
        }
        void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        try
        {
            var source = Path.Combine(root, "checker.mp4"); var camera = Path.Combine(root, "camera.mp4");
            await Run("-y", "-f", "lavfi", "-i", "nullsrc=s=320x180:r=30:d=2.5,geq=lum='if(mod(floor(X/4)+floor(Y/4),2),235,16)':cb=100:cr=160",
                "-c:v", "libx264", "-crf", "12", "-pix_fmt", "yuv420p", source);
            await Run("-y", "-f", "lavfi", "-i", "color=red:s=80x80:r=30:d=2.5", "-c:v", "libx264", camera);
            var stickerPath = Path.Combine(root, "sticker.bmp");
            var blue = new byte[32 * 32 * 4]; for (var i = 0; i < blue.Length; i += 4) { blue[i] = blue[i + 3] = 255; }
            MaskImage.WriteBmp(stickerPath, blue, 32, 32);
            var asset = new MediaAsset { Video = source, Camera = camera, Width = 320, Height = 180, Duration = 2.5 };
            var zoom = new ZoomSpan { Start = .2, Duration = 1.6, Scale = 1, EnterDuration = .2, ExitDuration = .2 };
            var project = new ProjectDocument { Width = 320, Height = 180, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 2.5 }],
                Zooms = [zoom], Canvas = new() { Padding = 16, Corner = 0, Shadow = 0, Background = "#FFFFFF", Cursor = new() { Mode = "hidden" } },
                Camera = new() { CenterX = .85, CenterY = .75, Size = .25, Shape = "square", Corner = 0, Entry = "none" } };
            project.Canvas.Stickers = [new() { Path = stickerPath, X = .1, Y = .8, Width = .15, Animation = "none", BackdropBlur = 0, ShadowRadius = 0, ShadowOpacity = 0 }];
            var baseline = Path.Combine(root, "baseline.mp4"); var focused = Path.Combine(root, "focused.mp4");
            await new ExportService(ffmpeg).ExportAsync(project, root, new(), baseline, CancellationToken.None);
            zoom.FocusEffect = new() { Target = "fixed", Size = .35, Blur = 1, Softness = .4, Dimming = .6 };
            await new ExportService(ffmpeg).ExportAsync(project, root, new(), focused, CancellationToken.None);
            async Task<byte[]> Pixels(string file, double time)
            {
                var raw = Path.Combine(root, "frame.rgb");
                try
                {
                    await Run("-y", "-ss", time.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", file,
                        "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", raw);
                    return await File.ReadAllBytesAsync(raw);
                }
                finally { if (File.Exists(raw)) File.Delete(raw); }
            }
            var original = await Pixels(baseline, .9); var rendered = await Pixels(focused, .9);
            double Detail(byte[] p, int y, int x, int count) => Enumerable.Range(x, count).Average(i => Math.Abs(p[(y * 320 + i) * 3] - p[(y * 320 + i - 1) * 3]));
            double Mean(byte[] p, int x, int y, int width, int height) => Enumerable.Range(y, height)
                .SelectMany(row => Enumerable.Range(x, width).Select(col => p[(row * 320 + col) * 3])).Average(v => (double)v);
            double Difference(byte[] a, byte[] b, int x, int y, int w, int h) => Enumerable.Range(y, h)
                .SelectMany(row => Enumerable.Range(x, w).SelectMany(col => Enumerable.Range(0, 3).Select(c =>
                    Math.Abs(a[(row * 320 + col) * 3 + c] - b[(row * 320 + col) * 3 + c])))).Average();
            var clear = Detail(rendered, 90, 152, 16); var outside = Detail(rendered, 90, 55, 32);
            Require(clear > 35 && outside < clear * .25, $"Radial focus failed: clear={clear}, outside={outside}");
            Require(Mean(rendered, 45, 50, 30, 20) < Mean(original, 45, 50, 30, 20) * .65, "Vignette did not darken the perimeter");
            Require(Difference(original, rendered, 150, 84, 20, 12) < 12, "Focus changed the clear center");
            Require(Difference(original, rendered, 260, 120, 20, 20) < 12, "Focus blurred or dimmed the camera");
            Require(Difference(original, rendered, 24, 140, 16, 12) < 12, "Focus blurred or dimmed the sticker");
            var restored = await Pixels(focused, 2.1); var originalTail = await Pixels(baseline, 2.1);
            Require(Difference(originalTail, restored, 40, 45, 200, 80) < 12, "Focus leaked beyond its clip");
            zoom.FocusEffect.Blur = 0;
            await new ExportService(ffmpeg).ExportAsync(project, root, new(), focused, CancellationToken.None);
            var dimOnly = await Pixels(focused, .9);
            Require(Detail(dimOnly, 90, 55, 32) > 8, "Dimming-only focus unexpectedly blurred the video");
            CheckPreview(host, project);
            Console.WriteLine($"FOCUS_OK real export radial clear/perimeter={clear:0.0}/{outside:0.0}, vignette, camera/sticker exclusion, exit restore, dimming without blur, WPF preview");
            await SelfTest.CheckVideoReferenceAsync(ffmpeg, FfmpegTools.LocateFfprobe(ffmpeg)!, root);
        }
        finally { host.Children.Clear(); Directory.Delete(root, recursive: true); }
    }

    static void CheckPreview(Grid host, ProjectDocument project)
    {
        var editor = new EditorWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(EditorWindow).GetField(name, flags)!.SetValue(editor, value);
        T Element<T>(string name) where T : FrameworkElement => (T)editor.FindName(name);
        Set("_project", project); Set("_focusTrack", new FocusTrack(project));
        Set("_pvFrame", new FramePlacement(0, 0, 320, 180, 0, 0, 0, 0)); Set("_pvFrameScale", 1d); Set("_pvOutW", 320); Set("_pvOutH", 180);
        Set("_selectedZoom", project.Zooms[0].Id);
        var scene = Element<Canvas>("SceneCanvas");
        ((Panel)scene.Parent).Children.Remove(scene); host.Children.Add(scene);
        scene.Width = 320; scene.Height = 180; scene.HorizontalAlignment = HorizontalAlignment.Left; scene.VerticalAlignment = VerticalAlignment.Top;
        var baseScene = Element<Canvas>("BaseScene"); baseScene.Width = 320; baseScene.Height = 180; baseScene.Children.Clear();
        baseScene.Background = Brushes.White;
        var camera = Element<System.Windows.Shapes.Rectangle>("CameraBubble"); camera.Width = camera.Height = 32;
        camera.Fill = Brushes.Red; camera.Visibility = Visibility.Visible; Canvas.SetLeft(camera, 270); Canvas.SetTop(camera, 130);
        var canvas = Element<Canvas>("PreviewCanvas"); canvas.Width = 320; canvas.Height = 180;
        typeof(EditorWindow).GetMethod("UpdateSpatialFocus", flags)!.Invoke(editor, [new ZoomPose(1, .5, .5), project.Zooms[0]]);
        scene.Measure(new(320, 180)); scene.Arrange(new(0, 0, 320, 180)); scene.UpdateLayout();
        var bitmap = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(scene);
        byte[] Pixel(int x, int y) { var p = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), p, 4, 0); return p; }
        var center = Pixel(160, 90); var edge = Pixel(40, 40); var cameraPixel = Pixel(285, 145);
        if (center[0] < 240 || edge[0] > 130 || cameraPixel[2] < 240 || cameraPixel[0] > 10)
            throw new InvalidOperationException($"WPF focus composition is wrong: center={center[0]}, outside={edge[0]}, camera={string.Join(',', cameraPixel)}");
        host.Children.Clear();
    }
}
