using DogSC;
using DogSC.Core;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Reflection;

internal static class NativeWindowCaptureChecks
{
    [DllImport("user32.dll", EntryPoint = "SetPhysicalCursorPos")] static extern bool SetCursorPos(int x, int y);

    public static async Task RunAsync()
    {
        var ffmpeg = FfmpegTools.LocateFfmpeg() ?? throw new InvalidOperationException("FFmpeg unavailable");
        var ffprobe = FfmpegTools.LocateFfprobe(ffmpeg) ?? throw new InvalidOperationException("FFprobe unavailable");
        var root = Path.Combine(Path.GetTempPath(), "DogSC-native-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var surface = new Grid(); surface.ColumnDefinitions.Add(new()); surface.ColumnDefinitions.Add(new());
        var left = new Border { Background = Brushes.Cyan }; var right = new Border { Background = Brushes.Yellow };
        Grid.SetColumn(right, 1); surface.Children.Add(left); surface.Children.Add(right);
        var target = new Window { Title = "DogSC native capture test", Content = surface, Width = 320, Height = 180,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Left = 120, Top = 140, Topmost = true };
        var occluder = new Window { Background = Brushes.Magenta, WindowStyle = WindowStyle.None, Width = 480, Height = 300,
            ShowActivated = false, ShowInTaskbar = false, Left = 100, Top = 120, Topmost = true };
        RecordingSession? recording = null;
        var savedPointer = Native.CursorPos();
        void QuarterPointer(IntPtr hwnd)
        {
            var rect = Native.WindowRect(hwnd)!;
            SetCursorPos(rect.Left + rect.Width / 4, rect.Top + rect.Height / 4);
        }
        async Task<byte[]> Pixels(string file, double time, int width, int height)
        {
            var path = Path.Combine(root, "frame.rgb");
            try
            {
                using var p = FfmpegProcess.Start(ffmpeg, ["-y", "-ss", time.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", file,
                    "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", path]);
                if (await p.WaitAsync() != 0) throw new InvalidOperationException(p.ErrorText());
                var bytes = await File.ReadAllBytesAsync(path);
                if (bytes.Length != width * height * 3) throw new InvalidOperationException("Native recording frame dimensions changed unexpectedly");
                return bytes;
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        static void Colors(byte[] bytes, int width, int height, string stage)
        {
            var a = ((height / 2) * width + width / 4) * 3;
            var b = ((height / 2) * width + width * 3 / 4) * 3;
            if (bytes[a] > 60 || bytes[a + 1] < 170 || bytes[a + 2] < 170
                || bytes[b] < 170 || bytes[b + 1] < 170 || bytes[b + 2] > 60)
                throw new InvalidOperationException($"Native target lost at {stage}: left={bytes[a]},{bytes[a+1]},{bytes[a+2]}, right={bytes[b]},{bytes[b+1]},{bytes[b+2]}");
        }
        try
        {
            target.Show(); await Task.Delay(250);
            var hwnd = new WindowInteropHelper(target).Handle;
            var bounds = Native.WindowRect(hwnd)!;
            var captureTarget = new CaptureTarget(CaptureKind.Window, "Native window", bounds.Left, bounds.Top, bounds.Width, bounds.Height, null)
                { WindowHandle = hwnd };
            var failedDirectory = Path.Combine(root, "failed-start");
            try
            {
                await using var failed = new RecordingSession(Path.Combine(root, "missing-ffmpeg.exe"), ffprobe, captureTarget, new(), failedDirectory);
                try { await failed.StartAsync(); throw new InvalidOperationException("Missing encoder unexpectedly started"); }
                catch (System.ComponentModel.Win32Exception) { }
                if (failed.IsRunning || typeof(RecordingSession).GetField("_windowCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(failed) is not null)
                    throw new InvalidOperationException("Failed encoder startup retained its native capture session");
            }
            finally { Directory.Delete(failedDirectory, recursive: true); }
            recording = new RecordingSession(ffmpeg, ffprobe, captureTarget, new() { FrameRate = 30 }, root);
            await recording.StartAsync();
            QuarterPointer(hwnd);
            await Task.Delay(500); var baseline = recording.Elapsed - .2;
            occluder.Show(); await Task.Delay(600); var covered = recording.Elapsed - .2;
            occluder.Hide();
            target.Left += 350; target.Top += 60;
            await Task.Delay(100); QuarterPointer(hwnd);
            await Task.Delay(600); var moved = recording.Elapsed - .2;
            target.Width = 400; target.Height = 220;
            await Task.Delay(100); QuarterPointer(hwnd);
            await Task.Delay(600); var resized = recording.Elapsed - .2;
            var resizedBounds = Native.WindowRect(hwnd)!;
            target.WindowState = WindowState.Minimized;
            await Task.Delay(600); var minimized = recording.Elapsed - .2;
            target.WindowState = WindowState.Normal;
            await Task.Delay(250);
            recording.Mark("after-resize");
            await recording.PauseAsync();
            var firstDuration = recording.Elapsed;
            target.Width = 360; target.Height = 200;
            await Task.Delay(150); await recording.ResumeAsync(); await Task.Delay(450);
            target.Close(); await Task.Delay(500); var closed = recording.Elapsed - firstDuration - .2;
            var project = await recording.FinishAsync();
            var info = await FfmpegTools.Probe(ffprobe, Path.Combine(root, project.Assets[0].Video));
            if (info is null) throw new InvalidOperationException("Native recording did not produce a video");
            var first = Path.Combine(root, project.Assets[0].Video);
            foreach (var stage in new[] { ("initial", baseline), ("covered", covered), ("moved", moved), ("resized", resized), ("minimized", minimized) })
                Colors(await Pixels(first, stage.Item2, info.Width, info.Height), info.Width, info.Height, stage.Item1);
            if (project.Assets.Count != 2 || project.Markers.All(m => m.Note != "after-resize"))
                throw new InvalidOperationException("Native pause/resume lost media or recording markers");
            var second = await FfmpegTools.Probe(ffprobe, Path.Combine(root, project.Assets[1].Video));
            if (second is null || second.Width == info.Width) throw new InvalidOperationException("Resumed native recording did not adopt its new window size");
            Colors(await Pixels(Path.Combine(root, project.Assets[1].Video), closed, second.Width, second.Height), second.Width, second.Height, "closed");
            CursorPoint PointerAt(double time) => project.Assets[0].Cursor.Last(p => p.Time <= time);
            // The desktop mouse is shared with the user. Verify the injected
            // position was sampled in its stage, without requiring it to stay
            // stationary until the later video probe timestamp.
            foreach (var stage in new[] { (Start: 0d, End: baseline), (Start: covered, End: moved) })
            {
                if (!project.Assets[0].Cursor.Any(p => p.Time >= stage.Start && p.Time <= stage.End && p.Cursor != "system.hidden"
                    && Math.Abs(p.X - .25) < .01 && Math.Abs(p.Y - .25) < .01))
                    throw new InvalidOperationException("Native pointer coordinates did not follow the moved window");
            }
            var fittedHeight = resizedBounds.Height * Math.Min(info.Width / (double)resizedBounds.Width, info.Height / (double)resizedBounds.Height);
            var expectedY = ((info.Height - fittedHeight) / 2 + fittedHeight / 4) / info.Height;
            if (!project.Assets[0].Cursor.Any(p => p.Time > moved && p.Time <= resized && p.Cursor != "system.hidden"
                && Math.Abs(p.X - .25) < .01 && Math.Abs(p.Y - expectedY) < .01))
                throw new InvalidOperationException("Native pointer coordinates ignored resize letterboxing; expected .25," + expectedY.ToString("0.000")
                    + "; samples " + string.Join("; ", project.Assets[0].Cursor.Where(p => p.Time > moved && p.Time <= resized)
                        .Select(p => $"{p.Time:0.000}:{p.X:0.000},{p.Y:0.000}/{p.Cursor}")));
            if (PointerAt(covered).Cursor != "system.hidden" || PointerAt(minimized).Cursor != "system.hidden")
                throw new InvalidOperationException("Obscured or minimized target recorded another window's pointer");
            Console.WriteLine($"NATIVE_WINDOW_OK real HWND recording: {info.Width}x{info.Height}, occlusion, move, resize, minimized/closed hold, pointer mapping and visibility, pause/resume {second.Width}x{second.Height}, marker retained, failed startup cleaned");
        }
        finally
        {
            if (recording is not null) await recording.DisposeAsync();
            SetCursorPos(savedPointer.X, savedPointer.Y);
            occluder.Close(); target.Close();
            Directory.Delete(root, recursive: true);
        }
    }
}
