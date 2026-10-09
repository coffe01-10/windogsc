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
        var clickStyle = CursorCatalog.ResolveClick(fx);
        var maximumZoom = project.Zooms.Select(z => z.Scale).DefaultIfEmpty(1).Max();
        // Mac 规格：基准高度 = 短边 ×44/1080 ×Size（再乘缩放峰值，保证放大时清晰）。
        var source = CanvasLayout.Crop(project.Width, project.Height, project.Canvas.Crop);
        var shortEdge = Math.Min(source.Width, source.Height);
        var refHeight = Math.Max(8, 44 * fx.Size * maximumZoom * shortEdge / 1080);
        var cursorSize = CanvasLayout.Even((int)Math.Ceiling(refHeight * 1.9 + 8));
        var cursorPath = Path.Combine(directory, "cursor.mov");
        using var cursor = Sprite(cursorPath, cursorSize, cursorSize);
        plan.Inputs[plan.CursorIndex] = cursorPath;

        var clickH = refHeight * 1.45 * fx.ClickScale;
        var ringSize = plan.RippleIndex < 0 ? 0
            : CanvasLayout.Even((int)Math.Ceiling(CursorCatalog.ClickFootprint(clickStyle, clickH) + 8));
        var ringPath = Path.Combine(directory, "ripple.mov");
        using var ring = plan.RippleIndex < 0 ? null : Sprite(ringPath, ringSize, ringSize);
        var timeline = new CursorTimeline(project);
        var color = CursorCatalog.ParseColor(fx.ClickColor);
        var spriteCache = new Dictionary<(string Asset, int Rot), CursorSprite>();
        await using var commands = new StreamWriter(Path.Combine(directory, "cursor.commands"), false, new UTF8Encoding(false));
        try
        {
            for (var frame = 0; frame < Math.Ceiling(plan.Duration * plan.Fps); frame++)
            {
                cancellation.ThrowIfCancellationRequested();
                var time = frame / (double)plan.Fps;
                var zoom = timeline.Motion.At(time).Scale;
                var sample = timeline.At(time);
                var pointer = sample.Pointer is { } point ? timeline.InFrame(point, time) : ((double X, double Y)?)null;
                var click = sample.Click is { } location ? timeline.InFrame(location, time) : ((double X, double Y)?)null;
                var stamp = F(Math.Max(0, time - 0.000001));
                var h = Math.Max(8, (int)Math.Round(44 * fx.Size * zoom * shortEdge / 1080));
                CursorSprite? sprite = null;
                if (pointer is not null && sample.Asset is { } assetId)
                {
                    var baseSprite = CursorArt.Render(assetId, h) ?? CursorArt.Render("system.arrow", h);
                    if (baseSprite is not null)
                    {
                        var rotKey = (int)Math.Round(sample.Rotation * 180 / Math.PI * 2) / 2;
                        var cacheKey = (assetId, rotKey);
                        if (!spriteCache.TryGetValue(cacheKey, out sprite))
                        {
                            sprite = CursorArt.Rotate(baseSprite, rotKey * Math.PI / 180);
                            spriteCache[cacheKey] = sprite;
                        }
                    }
                }
                var px = pointer is { } p ? F(p.X * source.Width - cursorSize / 2d) : "-10000";
                var py = pointer is { } q ? F(q.Y * source.Height - cursorSize / 2d) : "-10000";
                await commands.WriteLineAsync($"{stamp} [enter] overlay@pointer x {px}, [enter] overlay@pointer y {py};");
                var canvas = new byte[cursorSize * cursorSize * 4];
                if (sprite is not null)
                {
                    var left = (int)Math.Round(cursorSize / 2d - sprite.HotspotX);
                    var top = (int)Math.Round(cursorSize / 2d - sprite.HotspotY);
                    Blit(canvas, cursorSize, cursorSize, sprite, left, top);
                }
                await cursor.WriteInputAsync(canvas, cancellation);
                if (ring is not null)
                {
                    var rx = click is { } r ? F(r.X * source.Width - ringSize / 2d) : "-10000";
                    var ry = click is { } s ? F(s.Y * source.Height - ringSize / 2d) : "-10000";
                    await commands.WriteLineAsync($"{stamp} [enter] overlay@ripple x {rx}, [enter] overlay@ripple y {ry};");
                    var framePixels = click is null || sample.Progress >= 1
                        ? new byte[ringSize * ringSize * 4]
                        : CursorCatalog.RenderClick(ringSize, sample.ClickStyle, sample.Progress, color,
                            fx.ClickOpacity, clickH);
                    await ring.WriteInputAsync(framePixels, cancellation);
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

        static void Blit(byte[] dst, int dw, int dh, CursorSprite sprite, int left, int top)
        {
            for (var y = 0; y < sprite.Height; y++)
            {
                var dy = top + y;
                if (dy < 0 || dy >= dh) continue;
                for (var x = 0; x < sprite.Width; x++)
                {
                    var dx = left + x;
                    if (dx < 0 || dx >= dw) continue;
                    var si = (y * sprite.Width + x) * 4;
                    var di = (dy * dw + dx) * 4;
                    var a = sprite.Bgra[si + 3] / 255d;
                    if (a <= 0) continue;
                    var dstA = dst[di + 3] / 255d;
                    var blend = a + dstA * (1 - a);
                    if (blend <= 0) continue;
                    for (var ch = 0; ch < 3; ch++)
                        dst[di + ch] = (byte)Math.Round((sprite.Bgra[si + ch] * a + dst[di + ch] * dstA * (1 - a)) / blend);
                    dst[di + 3] = (byte)Math.Round(blend * 255);
                }
            }
        }
    }

    static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
