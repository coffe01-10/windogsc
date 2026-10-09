using System.Globalization;
using System.Text;
using DogSC.Core;

namespace DogSC;

static class CursorExport
{
    public static async Task PrepareAsync(string ffmpeg, ProjectDocument project, ExportPlan plan, string directory, CancellationToken cancellation)
    {
        if (plan.CursorIndex < 0) return;
        var fx = project.Canvas.Cursor;
        var maximumZoom = project.Zooms.Select(z => z.Scale).DefaultIfEmpty(1).Max();
        var cursorSize = CanvasLayout.Even((int)Math.Ceiling(18 * fx.Size * maximumZoom + 2));
        var dot = fx.Mode == "dot";
        var cursorHeight = dot ? cursorSize : cursorSize * 3 / 2;
        var cursorPath = Path.Combine(directory, "cursor.mov");
        using var cursor = Sprite(cursorPath, cursorSize, cursorHeight);
        plan.Inputs[plan.CursorIndex] = cursorPath;

        var ringSize = CanvasLayout.Even((int)Math.Ceiling(44 * fx.ClickScale * maximumZoom + 4));
        var ringPath = Path.Combine(directory, "ripple.mov");
        using var ring = plan.RippleIndex < 0 ? null : Sprite(ringPath, ringSize, ringSize);
        var timeline = new CursorTimeline(project);
        var source = CanvasLayout.Crop(project.Width, project.Height, project.Canvas.Crop);
        await using var commands = new StreamWriter(Path.Combine(directory, "cursor.commands"), false, new UTF8Encoding(false));
        try
        {
            for (var frame = 0; frame < Math.Ceiling(plan.Duration * plan.Fps); frame++)
            {
                cancellation.ThrowIfCancellationRequested();
                var time = frame / (double)plan.Fps;
                var activeZoom = project.Zooms.FirstOrDefault(z => time >= z.Start && time < z.Start + z.Duration);
                var zoom = activeZoom is null ? 1 : CanvasLayout.ZoomScaleAt(activeZoom, time);
                var sample = timeline.At(time);
                var pointer = sample.Pointer is { } point ? timeline.InFrame(point, time) : ((double X, double Y)?)null;
                var click = sample.Click is { } location ? timeline.InFrame(location, time) : ((double X, double Y)?)null;
                var stamp = F(Math.Max(0, time - 0.000001));
                var px = pointer is { } p ? F(p.X * source.Width - (dot ? cursorSize / 2d : 0)) : "-10000";
                var py = pointer is { } q ? F(q.Y * source.Height - (dot ? cursorSize / 2d : 0)) : "-10000";
                await commands.WriteLineAsync($"{stamp} [enter] overlay@pointer x {px}, [enter] overlay@pointer y {py};");
                var size = Math.Max(8, (int)Math.Round(18 * fx.Size * zoom));
                var height = dot ? size : size * 3 / 2;
                var pixels = MaskImage.CreateCursor(size, dot);
                var canvas = new byte[cursorSize * cursorHeight * 4];
                var left = dot ? (cursorSize - size) / 2 : 0;
                var top = dot ? (cursorHeight - height) / 2 : 0;
                for (var row = 0; row < height; row++)
                    Buffer.BlockCopy(pixels, row * size * 4, canvas, ((row + top) * cursorSize + left) * 4, size * 4);
                await cursor.WriteInputAsync(canvas, cancellation);
                if (ring is not null)
                {
                    var rx = click is { } r ? F(r.X * source.Width - ringSize / 2d) : "-10000";
                    var ry = click is { } s ? F(s.Y * source.Height - ringSize / 2d) : "-10000";
                    await commands.WriteLineAsync($"{stamp} [enter] overlay@ripple x {rx}, [enter] overlay@ripple y {ry};");
                    await ring.WriteInputAsync(MaskImage.CreateClickFrame(ringSize, sample.Click is null ? null : sample.Progress, fx, zoom), cancellation);
                }
            }
            cursor.CompleteInput();
            var cursorResult = await cursor.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (cursorResult != 0) throw new InvalidOperationException("光标准备失败：" + cursor.ErrorText());
            if (ring is not null)
            {
                ring.CompleteInput();
                var result = await ring.WaitAsync(cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (result != 0) throw new InvalidOperationException("点击反馈准备失败：" + ring.ErrorText());
                plan.Inputs[plan.RippleIndex] = ringPath;
            }
        }
        finally
        {
            if (!cursor.HasExited) await cursor.StopAsync(graceful: false);
            if (ring is not null && !ring.HasExited) await ring.StopAsync(graceful: false);
        }
        FfmpegProcess Sprite(string path, int width, int height) => FfmpegProcess.Start(ffmpeg, [
            "-y", "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", width + "x" + height,
            "-framerate", plan.Fps.ToString(), "-i", "pipe:0", "-c:v", "qtrle", "-pix_fmt", "argb", path]);
    }

    static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
