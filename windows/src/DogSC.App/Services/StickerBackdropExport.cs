using System.IO;
using DogSC.Core;

namespace DogSC;

/// <summary>
/// 贴纸背景通道：把压制/虚化的通道逐帧写成 64×36 bgra 裸流，
/// 导出图里按平面拆出做 alpha 合并。不用 qtrle——本机 ffmpeg 的多帧 qtrle 会毁值。
/// R=压屏幕 G=压摄像头 B=屏虚化/最大；摄像头虚化单独一条 sticker-bd-cam.raw。
/// </summary>
public static class StickerBackdropExport
{
    public const int Width = 64, Height = 36;

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

        var path = Path.Combine(directory, "sticker-bd.raw");
        var pathCam = plan.StickerMatteCamIndex >= 0 ? Path.Combine(directory, "sticker-bd-cam.raw") : null;
        await using var stream = File.Create(path);
        await using var streamCam = pathCam is null ? null : File.Create(pathCam);
        var frame = new byte[Width * Height * 4];
        var frameCam = new byte[Width * Height * 4];
        for (var i = 0; i < frames; i++)
        {
            var b = samples[i];
            var r = (byte)Math.Round(Math.Clamp(b.ScreenSuppression, 0, 1) * 255);
            var g = (byte)Math.Round(Math.Clamp(b.CameraSuppression, 0, 1) * 255);
            var bl = (byte)Math.Round(Math.Clamp(b.ScreenBlur / max.ScreenBlur, 0, 1) * 255);
            var cb = (byte)Math.Round(Math.Clamp(b.CameraBlur / max.CameraBlur, 0, 1) * 255);
            for (var p = 0; p < Width * Height; p++)
            {
                var o = p * 4;
                frame[o] = bl; frame[o + 1] = g; frame[o + 2] = r; frame[o + 3] = 255;
                frameCam[o] = cb; frameCam[o + 1] = cb; frameCam[o + 2] = cb; frameCam[o + 3] = 255;
            }
            await stream.WriteAsync(frame, cancellation);
            if (streamCam is not null) await streamCam.WriteAsync(frameCam, cancellation);
        }
        plan.Inputs[plan.StickerMatteIndex] = path;
        if (pathCam is not null) plan.Inputs[plan.StickerMatteCamIndex] = pathCam;
    }
}
