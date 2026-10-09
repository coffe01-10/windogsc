using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DogSC.Core;

namespace DogSC;

/// <summary>
/// 把带动画的贴纸按 Mac StickerTransition/ElementMotion 逐帧渲染成全画布
/// 透明 overlay 视频（qtrle），导出时只做一次 overlay=0:0。
/// 静止贴纸仍走 ExportGraph 的单帧贴图路径。
/// </summary>
public static class StickerAnimatedExport
{
    /// <summary>贴纸是否需要逐帧渲染（有进出动画或位置变换）。</summary>
    public static bool NeedsAnimation(StickerImage s)
        => !string.IsNullOrEmpty(s.Animation) && s.Animation != "none"
           || !string.IsNullOrEmpty(s.ExitAnimation) && s.ExitAnimation != "none";

    /// <summary>渲染动画贴纸帧到 dir/sticker-anim.mov；无动画贴纸返回 null。</summary>
    public static async Task<string?> PrepareAsync(
        string ffmpeg, ProjectDocument project, ExportPlan plan,
        IReadOnlyDictionary<string, ResolvedSticker> baked,
        double canvasW, double canvasH, string dir, CancellationToken ct)
    {
        var animated = project.Canvas.Stickers.Where(NeedsAnimation).ToList();
        var track = new StickerMotion.Track(project.Canvas.Stickers);
        var needsSuppression = project.Canvas.Stickers.Any(s => s.HidesScreen);
        if (animated.Count == 0 && !needsSuppression) return null;
        var bg = (Color)ColorConverter.ConvertFromString(project.Canvas.Background);
        var bgBrush = new SolidColorBrush(bg);
        bgBrush.Freeze();
        var w = Math.Max(2, (int)Math.Round(canvasW / 2) * 2);
        var h = Math.Max(2, (int)Math.Round(canvasH / 2) * 2);
        var fps = plan.Fps <= 0 ? 30 : plan.Fps;
        var duration = plan.Duration;
        var frames = Math.Max(1, (int)Math.Ceiling(duration * fps));
        var path = Path.Combine(dir, "sticker-anim.mov");
        var args =
            $"-hide_banner -loglevel warning -y -f rawvideo -pixel_format bgra -video_size {w}x{h} -framerate {fps} -i pipe:0" +
            $" -c:v qtrle -pix_fmt argb \"{path}\"";
        using var proc = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(ffmpeg, args)
            { RedirectStandardInput = true, UseShellExecute = false },
        };
        proc.Start();
        var frame = new byte[w * h * 4];
        var animatedIds = animated.Select(s => s.Id).ToHashSet();
        var bmps = new Dictionary<string, BitmapImage>();
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)fps;
            Array.Clear(frame);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var (_, bd) = track.SampleAt(t, suppressesInitialEntry: false);
                // 压屏幕：本机 ffmpeg 帧配对对生成流不稳，直接把背景色按压制度烘进 overlay 帧。
                if (bd.ScreenSuppression > 0)
                {
                    dc.PushOpacity(Math.Clamp(bd.ScreenSuppression, 0, 1));
                    dc.DrawRectangle(bgBrush, null, new Rect(0, 0, w, h));
                    dc.Pop();
                }
                foreach (var st in project.Canvas.Stickers)
                {
                    if (!animatedIds.Contains(st.Id) || !baked.TryGetValue(st.Id, out var r)) continue;
                    var (samples, _) = track.SampleAt(t, suppressesInitialEntry: false);
                    foreach (var s in samples.Where(x => x.Clip == st))
                    {
                        if (s.Visibility <= 0) continue;
                        var (scale, ox, oy) = StickerMotion.Transform(
                            s.Preset, s.Progress, st.X, st.Y, st.Width);
                        var wb = st.Width * canvasW * scale * (1 + 2 * r.Margin);
                        var hb = wb / r.Aspect;
                        if (wb < 1 || hb < 1) continue;
                        if (!bmps.TryGetValue(st.Id, out var bmp))
                        {
                            bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(r.Path, UriKind.Absolute);
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            bmp.Freeze();
                            bmps[st.Id] = bmp;
                        }
                        var cx = (st.X + ox) * canvasW;
                        var cy = (st.Y + oy) * canvasH;
                        dc.PushOpacity(s.Visibility * st.Opacity);
                        if (st.Rotation != 0)
                            dc.PushTransform(new RotateTransform(st.Rotation, cx, cy));
                        dc.DrawImage(bmp, new Rect(cx - wb / 2, cy - hb / 2, wb, hb));
                        if (st.Rotation != 0) dc.Pop();
                        dc.Pop();
                    }
                }
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.CopyPixels(frame, w * 4, 0);
            await proc.StandardInput.BaseStream.WriteAsync(frame, ct);
        }
        proc.StandardInput.Close();
        await proc.WaitForExitAsync(ct);
        return proc.ExitCode == 0 ? path : null;
    }
}
