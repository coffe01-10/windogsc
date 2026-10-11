using DogSC;
using DogSC.Core;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class LayoutLinkChecks
{
    public static async Task RunAsync(Grid host)
    {
        var root = Path.Combine(Path.GetTempPath(), "DogSC-layout-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settingsPath = AppPaths.SettingsFile;
        var settingsBytes = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var editor = new EditorWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Get(string name) => typeof(EditorWindow).GetField(name, flags)!.GetValue(editor)!;
        void Set(string name, object value) => typeof(EditorWindow).GetField(name, flags)!.SetValue(editor, value);
        void Call(string name, params object[] args) => typeof(EditorWindow).GetMethod(name, flags)!.Invoke(editor, args);
        ProjectDocument Current() => (ProjectDocument)Get("_project");
        static IEnumerable<DependencyObject> Descendants(DependencyObject value)
        {
            yield return value;
            foreach (var child in LogicalTreeHelper.GetChildren(value).OfType<DependencyObject>())
                foreach (var nested in Descendants(child)) yield return nested;
        }
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        try
        {
            var asset = new MediaAsset { Video = "screen.mp4", Camera = "camera.mp4", Duration = 10, Width = 320, Height = 180 };
            Set("_directory", root);
            Set("_project", new ProjectDocument { Width = 320, Height = 180, Assets = [asset], Clips = [new() { AssetId = asset.Id, SourceEnd = 10 }] });
            var player = (PreviewPlayer)Get("_player");
            typeof(PreviewPlayer).GetProperty("Time")!.SetValue(player, 2d);
            var content = (FrameworkElement)editor.Content; editor.Content = null; host.Children.Add(content);
            Set("_tool", "camera"); Call("RebuildInspector");
            host.UpdateLayout(); await Task.Delay(25);
            var inspector = (Panel)editor.FindName("Inspector");
            var link = Descendants(inspector).OfType<Button>().Single(b => Equals(b.Content, L.Text("屏幕与摄像头联动")));
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var p = Current(); var screen = p.ScreenMotions.Single(); var camera = p.CameraMotions.Single();
            Require(screen.GroupId is not null && screen.GroupId == camera.GroupId
                && screen.Timing.StartTime == camera.Timing.StartTime && screen.Timing.EndTime == camera.Timing.EndTime,
                "Layout button did not create one paired screen/camera window");
            var originalStart = screen.Timing.StartTime; var originalDuration = screen.Timing.Duration;
            var timeline = (TimelineControl)editor.FindName("Timeline");
            var handler = (EventHandler<ZoomMove>)typeof(TimelineControl).GetField("DecorationFinished", flags)!.GetValue(timeline)!;
            handler(timeline, new(camera.Id, 4, 2 + camera.Timing.ReturnDuration));
            Require(screen.Timing.StartTime == 4 && screen.Timing.Duration == 2, "Timeline camera move did not update its screen partner");
            Require(((Stack<string>)Get("_undo")).Count == 2, "Paired edit created extra undo steps");
            Call("Undo_Click", editor, new RoutedEventArgs());
            Require(Current().ScreenMotions[0].Timing.StartTime == originalStart && Current().CameraMotions[0].Timing.StartTime == originalStart
                && Current().ScreenMotions[0].Timing.Duration == originalDuration && Current().CameraMotions[0].Timing.Duration == originalDuration,
                "One undo did not restore both linked motion ranges");
            Call("Redo_Click", editor, new RoutedEventArgs());
            Require(Current().ScreenMotions[0].Timing.StartTime == 4 && Current().CameraMotions[0].Timing.StartTime == 4,
                "Redo lost linked motion timing");
            Call("FlushPendingSave");
            var saved = ProjectFile.Load(root);
            Require(saved.ScreenMotions[0].GroupId == saved.CameraMotions[0].GroupId && saved.ScreenMotions[0].Timing.StartTime == 4,
                "Saving the editor lost its linked layout");
            Console.WriteLine("LAYOUT_LINK_OK WPF layout button, camera timeline move, shared range, one-step undo/redo and saved pair");
        }
        finally
        {
            ((DispatcherTimer)Get("_saveTimer")).Stop();
            ((PreviewPlayer)Get("_player")).Dispose(); host.Children.Clear();
            if (settingsBytes is null) File.Delete(settingsPath); else File.WriteAllBytes(settingsPath, settingsBytes);
            Directory.Delete(root, recursive: true);
        }
    }
}
