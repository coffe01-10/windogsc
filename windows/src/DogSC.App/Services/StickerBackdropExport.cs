using System.Diagnostics;
using System.IO;
using DogSC.Core;

namespace DogSC;

/// <summary>
/// 贴纸背景通道：把压制/虚化的四条通道（R=压屏幕 G=压摄像头 B=屏虚化/最大 A=摄虚化/最大）
/// 逐帧写进一条 64×36 argb mask 视频，导出图里按平面拆出给 maskedmerge。
/// </summary>
public static class StickerBackdropExport
{
    public static async Task PrepareAsync(string ffmpeg, ProjectDocument project, ExportPlan plan,
        string directory, CancellationToken cancellation)
    {
        if (plan.StickerMatteIndex < 0) return;
        var track = new StickerMotion.Track(project.Canvas.Stickers);
        var frames = Math.Max(1, (int)Math.Ceiling(plan.Duration * plan.Fps));
        var max = new StickerMotion.Backdrop();
        var samples = new StickerMotion.Backdrop[frames];
        for (var i = 0; i < frames; i++)
        {
            var (_, backdrop) = track.SampleAt(i / (double)plan.Fps);
            samples[i] = backdrop;
            max.Merge(backdrop);
        }
        if (max.ScreenSuppression <= 0) max.ScreenSuppression = 1;
        if (max.CameraSuppression <= 0) max.CameraSuppression = 1;
        if (max.ScreenBlur <= 0) max.ScreenBlur = 1;
        if (max.CameraBlur <= 0) max.CameraBlur = 1;

        const int w = 64, h = 36;
        var path = Path.Combine(directory, "sticker-bd.mov");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = "-y -f rawvideo -pixel_format bgra -video_size " + w + "x" + h
                    + " -framerate " + plan.Fps + " -i pipe:0 -c:v qtrle -pix_fmt argb \"" + path + "\"",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        var frame = new byte[w * h * 4];
        for (var i = 0; i < frames; i++)
        {
            var b = samples[i];
            var r = (byte)Math.Round(Math.Clamp(b.ScreenSuppression, 0, 1) * 255);
            var g = (byte)Math.Round(Math.Clamp(b.CameraSuppression, 0, 1) * 255);
            var bl = (byte)Math.Round(Math.Clamp(b.ScreenBlur / max.ScreenBlur, 0, 1) * 255);
            var a = (byte)Math.Round(Math.Clamp(b.CameraBlur / max.CameraBlur, 0, 1) * 255);
            for (var p = 0; p < w * h; p++)
            {
                var o = p * 4;
                frame[o] = bl; frame[o + 1] = g; frame[o + 2] = r; frame[o + 3] = a;
            }
            await process.StandardInput.BaseStream.WriteAsync(frame, cancellation);
        }
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellation);
        plan.Inputs[plan.StickerMatteIndex] = path;
    }
}
