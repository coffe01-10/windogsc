using DogSC;
using DogSC.Core;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal static class LayoutPresetChecks
{
    public static async Task RunAsync(Grid host)
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
        var root = Path.Combine(Path.GetTempPath(), "DogSC-layout-presets-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var settingsBytes = File.Exists(AppPaths.SettingsFile) ? File.ReadAllBytes(AppPaths.SettingsFile) : null;
        var editor = new EditorWindow(); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Get(string name) => typeof(EditorWindow).GetField(name, flags)!.GetValue(editor)!;
        void Set(string name, object value) => typeof(EditorWindow).GetField(name, flags)!.SetValue(editor, value);
        object? Call(string name, params object[] args) => typeof(EditorWindow).GetMethod(name, flags)!.Invoke(editor, args);
        ProjectDocument Current() => (ProjectDocument)Get("_project");
        static IEnumerable<DependencyObject> Children(DependencyObject node)
        {
            yield return node;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) foreach (var nested in Children(child)) yield return nested;
        }
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Run(params string[] args)
        {
            using var process = FfmpegProcess.Start(ffmpeg, args);
            if (await process.WaitAsync() != 0) throw new InvalidOperationException(process.ErrorText());
        }
        try
        {
            var screen = Path.Combine(root, "screen.mp4"); var camera = Path.Combine(root, "camera.mp4");
            await Run("-y", "-f", "lavfi", "-i", "color=0x00c050:s=320x180:r=60:d=2.4", "-c:v", "libx264", "-crf", "8", screen);
            await Run("-y", "-f", "lavfi", "-i", "color=0x2850e0:s=240x180:r=60:d=2.4", "-c:v", "libx264", "-crf", "8", camera);
            var asset = new MediaAsset { Video = screen, Camera = camera, Width = 320, Height = 180, Duration = 2.4 };
            var p = new ProjectDocument { Width = 320, Height = 180, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 2.4 }],
                Canvas = new() { Padding = 0, Scale = .7, CenterX = .65, CenterY = .4, Corner = 0, Shadow = 0, Background = "#000000", Cursor = new() { Mode = "hidden" } },
                Camera = new() { Docked = true, Shape = "horizontal", Size = .3, CenterX = .8, CenterY = .45 } };
            Set("_project", p); Set("_directory", root); Set("_cameraMaskAspect", 4d / 3);
            SettingsStore.Current.LayoutPresets.Clear();
            var savePanel = (FrameworkElement)Call("CreateLayoutPresetSavePanel")!; host.Children.Add(savePanel); host.UpdateLayout();
            var name = Children(savePanel).OfType<TextBox>().Single();
            var save = Children(savePanel).OfType<Button>().Single(b => Equals(b.Content, L.Text("保存预设")));
            Require(!save.IsEnabled, "Empty layout name is enabled"); name.Text = "  Interview  "; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(SettingsStore.Current.LayoutPresets.Single().Name == "Interview", "Save did not trim the layout name");
            p.Canvas.Scale = .8; p.Camera.Size = .4;
            name.Text = "Interview";
            Require(Children(savePanel).OfType<TextBlock>().Single(t => t.Text == L.Text("同名预设将被替换。")).Visibility == Visibility.Visible, "Same-name overwrite hint is absent");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var loaded = SettingsFile.Load(AppPaths.SettingsFile).LayoutPresets;
            Require(loaded.Count == 1 && loaded[0].Screen.Scale == .8 && loaded[0].Camera.Size == .4, "Overwrite or settings reload lost layout values");
            SettingsStore.Current.LayoutPresets = loaded; host.Children.Clear();
            var content = (FrameworkElement)editor.Content; editor.Content = null; host.Children.Add(content); host.UpdateLayout(); await Task.Delay(25);
            p.Canvas.Scale = .3; p.Canvas.CenterX = .3; p.Canvas.CenterY = .7; p.Camera.Docked = false; p.Camera.Size = .15; p.Camera.CenterX = .2;
            var player = (PreviewPlayer)Get("_player"); typeof(PreviewPlayer).GetProperty("Time")!.SetValue(player, .4);
            var menu = (ContextMenu)Call("BuildLayoutPresetMenu")!;
            menu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            p = Current();
            Require(p.ScreenMotions.Single().Target.CanvasPlacement && p.ScreenMotions[0].Target.Scale == .8
                && p.ScreenMotions[0].GroupId == p.CameraMotions.Single().GroupId && p.Canvas.Scale == .3, "Applying preset changed the base or failed to pair tracks");
            Require(((Stack<string>)Get("_undo")).Count == 1, "Applying layout created multiple undo steps");
            Call("Undo_Click", editor, new RoutedEventArgs()); Require(Current().ScreenMotions.Count == 0 && Current().CameraMotions.Count == 0, "One undo did not remove the pair");
            Call("Redo_Click", editor, new RoutedEventArgs()); p = Current();
            Require(p.ScreenMotions.Count == 1 && p.CameraMotions.Count == 1, "Redo lost the preset pair");
            menu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); p = Current();
            Require(p.ScreenMotions.Count == 1 && p.CameraMotions.Count == 1, "Reapplying preset duplicated an active pair");
            Call("FlushPendingSave"); var reopened = ProjectFile.Load(root);
            Require(reopened.ScreenMotions[0].Target.CanvasPlacement && reopened.ScreenMotions[0].GroupId == reopened.CameraMotions[0].GroupId, "Saved project lost layout semantics");
            // Evaluate actual WPF transforms at a settled target on a non-centred
            // base; using canvas coordinates as a local pivot shifts this case.
            Set("_selectedDecoration", null!); typeof(PreviewPlayer).GetProperty("Time")!.SetValue(player, p.ScreenMotions[0].Timing.EndTime);
            Call("ApplyZoomVisual"); host.UpdateLayout();
            var frame = (Canvas)editor.FindName("FrameHost"); var scene = (Canvas)editor.FindName("SceneCanvas");
            var center = frame.TransformToAncestor(scene).Transform(new Point(frame.Width / 2, frame.Height / 2));
            var scale = (double)Get("_pvFrameScale");
            Require(Math.Abs(center.X / scale - .65 * 320) < 1 && Math.Abs(center.Y / scale - .4 * 180) < 1,
                $"WPF preset screen center drifted: {center.X / scale:0.00},{center.Y / scale:0.00}");
            await CheckExportsAsync(p, root, ffmpeg, Run);
            await CameraMorphChecks.CheckPreviewAsync(host, p, [.5, .65, .9, 1.05]);
            var redo = (Stack<string>)Get("_redo"); redo.Push("preserve-redo"); var count = redo.Count;
            var tiny = ScenePreset.Clone(p); tiny.ScreenMotions.Clear(); tiny.CameraMotions.Clear(); tiny.Clips[0].SourceEnd = .1;
            Set("_project", tiny); typeof(PreviewPlayer).GetProperty("Time")!.SetValue(player, .05);
            Call("ApplySavedLayoutPreset", loaded[0]); Require(redo.Count == count && Current() == tiny, "No-space layout application erased redo or changed the project");
            Set("_project", p);
            var delete = ((ContextMenu)Call("BuildLayoutPresetMenu")!).Items.OfType<MenuItem>().Last(); delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Require(SettingsFile.Load(AppPaths.SettingsFile).LayoutPresets.Count == 0, "Deleting preset did not persist");
            Console.WriteLine("LAYOUT_PRESET_OK WPF save/overwrite, settings reload, paired application/update, one-step undo/redo, no-space redo preservation, cross-base preview and project save; delete persisted");
        }
        finally
        {
            ((DispatcherTimer)Get("_saveTimer")).Stop(); ((PreviewPlayer)Get("_player")).Dispose(); host.Children.Clear();
            if (settingsBytes is null) File.Delete(AppPaths.SettingsFile); else File.WriteAllBytes(AppPaths.SettingsFile, settingsBytes);
            Directory.Delete(root, recursive: true);
        }
    }

    static async Task CheckExportsAsync(ProjectDocument p, string root, string ffmpeg, Func<string[], Task> run)
    {
        var placement = CanvasLayout.Place(320, 180, p.Width, p.Height, p.Canvas);
        var viewport = new ScreenAnchorViewport(320, 180, placement.Width, placement.Height,
            BaseCenterX: (placement.X + placement.Width / 2d) / 320, BaseCenterY: (placement.Y + placement.Height / 2d) / 180, BaseScale: p.Canvas.Scale);
        var track = new ScreenMotionTrack(p.ScreenMotions); var rest = track.SampleLayout(-1, null, viewport);
        var cam = new CameraSceneTrack(p, 320, 180, 4d / 3); var samples = 0;
        foreach (var fps in new[] { 30, 60 })
        {
            var output = Path.Combine(root, "layout-" + fps + ".mp4"); var raw = Path.Combine(root, "frames.rgb");
            await new ExportService(ffmpeg).ExportAsync(p, root, new() { Fps = fps }, output, CancellationToken.None);
            try
            {
                await run(["-y", "-i", output, "-pix_fmt", "rgb24", "-f", "rawvideo", raw]); var pixels = await File.ReadAllBytesAsync(raw);
                var frames = (int)Math.Round(TimelineMath.Duration(p) * fps);
                if (pixels.Length != frames * 320 * 180 * 3) throw new InvalidOperationException("Layout export changed frame count");
                for (var f = 0; f < frames; f++)
                {
                    var time = f / (double)fps;
                    var corners = MotionExport.ScreenCorners(track.SampleLayout(time, null, viewport, p.Motion.ScreenSpringMass, p.Motion.ScreenSpringStiffness, p.Motion.ScreenSpringDamping), rest,
                        320, 180, placement.X, placement.Y, placement.Width, placement.Height);
                    var map = MotionExport.Homography.FromQuad([(0, 0), (320, 0), (0, 180), (320, 180)], corners);
                    var lt = map.Apply(placement.X, placement.Y); var rb = map.Apply(placement.X + placement.Width, placement.Y + placement.Height);
                    var camera = cam.At(time, 0, TimelineMath.Duration(p)); var checkedPixels = 0; var mismatches = 0;
                    var cameraPixels = 0; var cameraMismatches = 0;
                    for (var y = 0; y < 180; y += 2) for (var x = 0; x < 320; x += 2)
                    {
                        if (Math.Abs(x - lt.X) < 3 || Math.Abs(x - rb.X) < 3 || Math.Abs(y - lt.Y) < 3 || Math.Abs(y - rb.Y) < 3) continue;
                        var i = (f * 320 * 180 + y * 320 + x) * 3;
                        var coverage = camera.Coverage(x + .5, y + .5);
                        if (coverage > .99 || camera.ShapeCoverage(x + .5, y + .5, 3) == 0)
                        {
                            var observedCamera = pixels[i + 2] > 180 && pixels[i + 2] > pixels[i + 1] + 50;
                            if (observedCamera != (coverage > .99)) cameraMismatches++;
                            cameraPixels++;
                        }
                        if (camera.ShapeCoverage(x + .5, y + .5, 3) > 0) continue;
                        var expected = x > lt.X && x < rb.X && y > lt.Y && y < rb.Y;
                        var observed = pixels[i + 1] > pixels[i + 2] + 60 && pixels[i + 1] > 100;
                        if (expected != observed) mismatches++; checkedPixels++;
                    }
                    if (mismatches > checkedPixels * .01) throw new InvalidOperationException($"{fps}fps layout frame {f}: {mismatches} wrong screen placement pixels");
                    if (cameraMismatches > cameraPixels * .01) throw new InvalidOperationException($"{fps}fps layout frame {f}: {cameraMismatches} wrong camera layout pixels");
                    samples += checkedPixels;
                }
            }
            finally { File.Delete(output); File.Delete(raw); }
        }
        Console.WriteLine($"LAYOUT_EXPORT_OK real 30/60fps absolute size, translated base, entrance/return and paired camera; {samples} sampled screen pixels; exact frame count");
    }
}
