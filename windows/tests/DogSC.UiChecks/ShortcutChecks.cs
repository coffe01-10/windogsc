using DogSC;
using DogSC.Core;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class ShortcutChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    public static async Task RunAsync(Grid host, string? screenshot)
    {
        var settings = File.Exists(AppPaths.SettingsFile) ? File.ReadAllBytes(AppPaths.SettingsFile) : null;
        var language = SettingsStore.Current.Language;
        var root = Path.Combine(Path.GetTempPath(), "DogSC-shortcuts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var editor = new EditorWindow();
        object Get(string name) => typeof(EditorWindow).GetField(name, Private)!.GetValue(editor)!;
        void Set(string name, object? value) => typeof(EditorWindow).GetField(name, Private)!.SetValue(editor, value);
        object? Call(string name, params object[] args) => typeof(EditorWindow).GetMethod(name, Private)!.Invoke(editor, args);
        ProjectDocument Current() => (ProjectDocument)Get("_project");
        void Key(Key key, ModifierKeys modifiers = ModifierKeys.None, bool repeat = false)
            => Require((bool)Call("HandleEditorShortcut", key, modifiers, repeat)!, "Shortcut not handled: " + key);
        var player = (PreviewPlayer)Get("_player");
        try
        {
            await CheckExportLayout(host, screenshot);
            var asset = new MediaAsset { Video = "test.mp4", Duration = 10, Width = 320, Height = 180 };
            var zoom = new ZoomSpan { Start = 1, Duration = 4, Scale = 1.6 };
            Set("_directory", root); Set("_project", new ProjectDocument { Width = 320, Height = 180, FrameRate = 60,
                Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 10 }], Zooms = [zoom] });
            var view = (FrameworkElement)editor.Content; editor.Content = null; host.Children.Clear(); host.Children.Add(view);
            Call("Commit", true); host.UpdateLayout(); player.Open(Current(), root); player.Seek(2, false);
            Key(System.Windows.Input.Key.Right); Require(Math.Abs(player.Time - (2 + 1d / 60)) < 1e-9, "Arrow did not step one frame.");
            Key(System.Windows.Input.Key.Left, ModifierKeys.Shift); Require(Math.Abs(player.Time - (2 - 4d / 60)) < 1e-9, "Shift arrow did not step five frames.");
            player.Seek(0, false); Key(System.Windows.Input.Key.Left); Require(player.Time == 0, "Step went before timeline start.");
            player.Seek(3, false); Set("_selectedZoom", zoom.Id); Set("_selectedClip", null);
            var history = ((Stack<string>)Get("_undo")).Count;
            Key(System.Windows.Input.Key.S); Require(Current().Zooms.Count == 2 && Current().Clips.Count == 1, "S split primary instead of selected effect.");
            Require(((Stack<string>)Get("_undo")).Count == history + 1, "S produced extra undo steps.");
            Key(System.Windows.Input.Key.Z, ModifierKeys.Control); Require(Current().Zooms.Count == 1, "Undo failed.");
            Key(System.Windows.Input.Key.Z, ModifierKeys.Control | ModifierKeys.Shift); Require(Current().Zooms.Count == 2, "Redo failed.");
            Set("_selectedZoom", Current().Zooms[0].Id); player.Seek(2, false); Key(System.Windows.Input.Key.Q);
            Require(Current().Zooms[0].Start == 2 && TimelineMath.Duration(Current()) == 10, "Q animation trim changed video.");
            var count = Current().Zooms.Count; Key(System.Windows.Input.Key.D, repeat: true); Require(Current().Zooms.Count == count, "Repeated D deleted an effect.");
            Key(System.Windows.Input.Key.D); Require(Current().Zooms.Count == count - 1, "D did not delete selection.");
            Set("_selectedZoom", null); Set("_selectedClip", Current().Clips[0].Id); player.Seek(7, false); Key(System.Windows.Input.Key.W);
            Require(TimelineMath.Duration(Current()) == 7, "W primary trim failed.");
            player.Seek(2, false); Key(System.Windows.Input.Key.Q); Require(TimelineMath.Duration(Current()) == 5 && player.Time == 0, "Q primary trim failed.");
            Key(System.Windows.Input.Key.I, ModifierKeys.Control | ModifierKeys.Alt);
            Require(((ColumnDefinition)editor.FindName("InspectorColumn")).Width.Value == 0 && ((Grid)editor.FindName("InspectorPane")).Visibility == Visibility.Collapsed, "Hidden inspector still occupies space.");
            Key(System.Windows.Input.Key.I, ModifierKeys.Control | ModifierKeys.Alt);
            Require(((Grid)editor.FindName("InspectorPane")).Visibility == Visibility.Visible, "Inspector did not return.");
            var name = (TextBox)editor.FindName("NameBox"); Keyboard.Focus(name);
            Require(Keyboard.FocusedElement == name, "Text focus test could not focus field.");
            var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view)!, Environment.TickCount, System.Windows.Input.Key.S);
            Call("Window_PreviewKeyDown", editor, input);
            Require(!input.Handled && Current().Clips.Count == 1, "Typing S split a clip.");
            Keyboard.Focus((TimelineControl)editor.FindName("Timeline"));
            Console.WriteLine("SHORTCUT_OK WPF frame steps, S/Q/W/D selection routing, repeat protection, undo/redo, inspector and text editing");
            var pair = new ProjectDocument { Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 10 }],
                ScreenMotions = [new() { GroupId = "pair", Timing = new() { StartTime = 1, Duration = 3, LeadInDuration = 1, ReturnDuration = .4 }, Target = new() { Scale = .6 } }],
                CameraMotions = [new() { GroupId = "pair", Timing = new() { StartTime = 1, Duration = 3, LeadInDuration = 1, ReturnDuration = .4 }, Target = new() { Layout = "fullscreen" } }] };
            Set("_project", pair); Set("_selectedDecoration", pair.ScreenMotions[0].Id); Set("_selectedZoom", null); Set("_selectedClip", null);
            var expectedScale = new ScreenMotionTrack(pair.ScreenMotions).SampleLayout(1.5).State.Scale;
            Call("Commit", true); player.Open(Current(), root); player.Seek(1.3, false);
            Key(System.Windows.Input.Key.S);
            Require(Current().ScreenMotions.Count == 2 && Current().CameraMotions.Count == 2, "S did not split both linked tracks.");
            Key(System.Windows.Input.Key.Z, ModifierKeys.Control);
            Require(Current().ScreenMotions.Count == 1 && Current().CameraMotions.Count == 1, "One undo did not restore both tracks.");
            Key(System.Windows.Input.Key.Z, ModifierKeys.Control | ModifierKeys.Shift);
            Call("FlushPendingSave"); var saved = ProjectFile.Load(root);
            Require(saved.ScreenMotions[1].GroupId == saved.CameraMotions[1].GroupId
                && saved.ScreenMotions[0].GroupId != saved.ScreenMotions[1].GroupId, "Saving lost separate left/right layout pairs.");
            Require(Math.Abs(new ScreenMotionTrack(saved.ScreenMotions).SampleLayout(1.5).State.Scale
                - expectedScale) < 1e-8, "Saving reset the retained animation phase.");
            Console.WriteLine("CUT_EDIT_OK WPF linked S, one-step undo/redo and saved left/right pairs");
            await CheckRangeExport(root);
        }
        finally
        {
            ((DispatcherTimer)Get("_saveTimer")).Stop(); player.Dispose(); host.Children.Clear();
            SettingsStore.Current.Language = language; LanguageCatalog.Current.Refresh();
            if (settings is null) File.Delete(AppPaths.SettingsFile); else File.WriteAllBytes(AppPaths.SettingsFile, settings);
            Directory.Delete(root, true);
        }
    }

    static async Task CheckExportLayout(Grid host, string? screenshot)
    {
        var asset = new MediaAsset { Duration = 15.96, Width = 1920, Height = 1080 };
        var p = new ProjectDocument { Width = 1920, Height = 1080, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = asset.Duration }] };
        var preview = new DrawingImage(new GeometryDrawing(Brushes.LightSteelBlue, null, new RectangleGeometry(new Rect(0, 0, 1920, 1080))));
        foreach (var language in new[] { "zh-Hans", "en" })
        {
            SettingsStore.Current.Language = language; LanguageCatalog.Current.Refresh();
            var window = new ExportWindow(p, "", preview);
            var content = (FrameworkElement)window.Content; window.Content = null; content.Width = 842;
            host.Children.Clear(); host.Children.Add(content);
            var status = (TextBlock)window.FindName("Status"); var bar = (ProgressBar)window.FindName("Bar");
            status.Visibility = bar.Visibility = Visibility.Visible; bar.Value = .05;
            foreach (var height in new[] { 635, 520 })
            foreach (var text in new[] { L.Text("正在导出… ") + "5% · 0:02 · 0:38", L.Text("导出完成 · ") + "C:\\Users\\chen\\Videos\\DogSC\\很长的文件夹名称\\Demo recording with long name.mp4" })
            {
                content.Height = height; status.Text = text; host.UpdateLayout(); await Task.Delay(20);
                var area = (Grid)window.FindName("ExportSettings");
                var bottom = status.TranslatePoint(new Point(0, status.ActualHeight), area).Y;
                Require(bottom <= area.ActualHeight + 1, $"Progress clipped: height {height}, {language}, bottom {bottom}, area {area.ActualHeight}");
                Require(((Border)window.FindName("VideoPreview")).ActualHeight > 10, "Preview disappeared.");
            }
            if (screenshot is not null && language == "zh-Hans")
            {
                content.Height = 635; status.Text = "正在导出… 5% · 已用 0:02 · 预计剩余 0:38"; host.UpdateLayout();
                var bitmap = new RenderTargetBitmap(842, 635, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, 842, 635));
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(screenshot); encoder.Save(file);
            }
            host.Children.Clear(); window.Close();
        }
        Console.WriteLine("EXPORT_LAYOUT_OK WPF 635/520 heights, Chinese/English progress and wrapped completion path above footer");
    }

    static async Task CheckRangeExport(string root)
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg();
        if (ffmpeg is null) { Console.WriteLine("RANGE_EXPORT_UNVERIFIED FFmpeg unavailable"); return; }
        async Task Run(params string[] args)
        {
            using var process = FfmpegProcess.Start(ffmpeg, args);
            if (await process.WaitAsync() != 0) throw new InvalidOperationException(process.ErrorText());
        }
        var source = Path.Combine(root, "source.mp4");
        await Run("-y", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=60:d=3", "-c:v", "libx264", source);
        var asset = new MediaAsset { Video = source, Duration = 3, Width = 320, Height = 180 };
        var p = new ProjectDocument { Width = 320, Height = 180, FrameRate = 60, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 3 }],
            Zooms = [new() { Start = .5, Duration = 2, Scale = 1.5, EnterDuration = .7, ExitDuration = .3 }],
            Canvas = new() { Padding = 0, Corner = 0, Shadow = 0, Cursor = new() { Mode = "hidden" } } };
        var full = Path.Combine(root, "full.mp4"); var range = Path.Combine(root, "range.mp4");
        await new ExportService(ffmpeg).ExportAsync(p, root, new() { Fps = 60 }, full, CancellationToken.None);
        await new ExportService(ffmpeg).ExportAsync(p, root, new() { Fps = 60, RangeStart = 1.25, RangeDuration = .75 }, range, CancellationToken.None);
        var a = Path.Combine(root, "full.rgb"); var b = Path.Combine(root, "range.rgb");
        await Run("-y", "-i", full, "-ss", "1.25", "-t", "0.75", "-pix_fmt", "rgb24", "-f", "rawvideo", a);
        await Run("-y", "-i", range, "-pix_fmt", "rgb24", "-f", "rawvideo", b);
        var expected = await File.ReadAllBytesAsync(a); var actual = await File.ReadAllBytesAsync(b);
        Require(actual.Length == 45 * 320 * 180 * 3 && actual.Length == expected.Length, "Range export frame count incorrect.");
        var difference = expected.Zip(actual, (x, y) => Math.Abs(x - y)).Average();
        Require(difference < 5, "Range export reset animation/source phase, difference=" + difference);
        Require(TimelineMath.Duration(p) == 3 && p.Zooms[0].Start == .5, "Range export modified project.");
        Console.WriteLine("RANGE_EXPORT_OK real 60fps 45 frames, original animation/source phase, unchanged project");
        foreach (var fps in new[] { 30, 60 }) await CheckCutExport(root, source, ffmpeg, fps);
    }

    static async Task CheckCutExport(string root, string source, string ffmpeg, int fps)
    {
        async Task<byte[]> Export(ProjectDocument project, string name)
        {
            var output = Path.Combine(root, name + ".mp4"); var raw = Path.Combine(root, name + ".rgb");
            await new ExportService(ffmpeg).ExportAsync(project, root, new() { Fps = fps }, output, CancellationToken.None);
            using var decoder = FfmpegProcess.Start(ffmpeg, ["-y", "-i", output, "-pix_fmt", "rgb24", "-f", "rawvideo", raw]);
            if (await decoder.WaitAsync() != 0) throw new InvalidOperationException(decoder.ErrorText());
            return await File.ReadAllBytesAsync(raw);
        }
        var asset = new MediaAsset { Video = source, Camera = source, Duration = 3, Width = 320, Height = 180 };
        var p = new ProjectDocument { Width = 320, Height = 180, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 3 }],
            Canvas = new() { Padding = 16, Corner = 0, Shadow = 0, Cursor = new() { Mode = "hidden" } },
            Zooms = [new() { Start = .5, Duration = 2, Scale = 1.6, EnterDuration = .7, ExitDuration = .5,
                FocusEffect = new() { Blur = .2, Dimming = .1 } }],
            ScreenMotions = [new() { GroupId = "pair", Timing = new() { StartTime = .5, Duration = 1.5, LeadInDuration = .7, ReturnDuration = .5 },
                Target = new() { Scale = .7, RotationY = 15 } }],
            CameraMotions = [new() { GroupId = "pair", Timing = new() { StartTime = .5, Duration = 1.5, LeadInDuration = .7, ReturnDuration = .5 },
                Target = new() { Layout = "fullscreen", X = .5, Y = .5 } }] };
        var original = await Export(p, "cut-original");
        TimelineShortcutEdits.SplitEffect(p, p.Zooms[0].Id, .8);
        TimelineShortcutEdits.SplitEffect(p, p.ScreenMotions[0].Id, .8);
        p = ScenePreset.Clone(p); ProjectFile.Normalize(p);
        var split = await Export(p, "cut-split");
        Require(split.Length == original.Length && split.Length == 3 * fps * 320 * 180 * 3,
            $"Animation split changed output frame count: original={original.Length}, split={split.Length}, expected={3 * fps * 320 * 180 * 3}.");
        var splitError = original.Zip(split, (a, b) => Math.Abs(a - b)).Average();
        Require(splitError < 5, "Animation split changed rendered screen/camera/focus, RGB error=" + splitError);
        TimelineEdits.TrySplit(p, 1.1); TimelineEdits.TrySplit(p, .8);
        var deleted = TimelineMath.Place(p).Single(c => Math.Abs(c.Start - .8) < 1e-8);
        Require(TimelineEdits.TryDeleteClip(p, deleted.Clip.Id), "Could not remove source interval.");
        var cut = await Export(p, "cut-ripple"); const int stride = 320 * 180 * 3;
        var retainedFrames = (int)Math.Round(2.7 * fps);
        Require(cut.Length == retainedFrames * stride, "Ripple cut changed output frame count.");
        double difference = 0;
        for (var frame = 0; frame < retainedFrames; frame++)
        {
            var expected = (frame < (int)Math.Round(.8 * fps) ? frame : frame + (int)Math.Round(.3 * fps)) * stride;
            for (var pixel = 0; pixel < stride; pixel++) difference += Math.Abs(cut[frame * stride + pixel] - original[expected + pixel]);
        }
        difference /= cut.Length;
        Require(difference < 5, "Ripple cut restarted composed animation, RGB error=" + difference);
        Console.WriteLine($"CUT_EXPORT_OK real {fps}fps screen/camera/focus, split {3 * fps} frames RGB error={splitError:0.###}, ripple {retainedFrames} frames RGB error={difference:0.###}");
    }
}
