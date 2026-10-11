using DogSC.Core;

namespace DogSC;

static class SpatialFocusExport
{
    public static async Task PrepareAsync(string ffmpeg, ProjectDocument project, ExportPlan plan,
        string directory, CancellationToken cancellation)
    {
        if (plan.SpatialFocusInputs.Count == 0) return;
        var track = new FocusTrack(project);
        var width = Math.Min(640, plan.Width);
        var height = Math.Max(1, (int)Math.Round(width * plan.Height / (double)plan.Width));
        var empty = new byte[width * height];
        foreach (var input in plan.SpatialFocusInputs)
        {
            var path = Path.Combine(directory, "focus-" + input.Index + ".mkv");
            using var encoder = FfmpegProcess.Start(ffmpeg, ["-y", "-hide_banner", "-loglevel", "error",
                "-f", "rawvideo", "-pixel_format", "gray", "-video_size", width + "x" + height,
                "-framerate", plan.Fps.ToString(), "-i", "pipe:0", "-c:v", "ffv1", "-pix_fmt", "gray", path]);
            try
            {
                for (var frame = 0; frame < Math.Ceiling(plan.Duration * plan.Fps); frame++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var time = frame / (double)plan.Fps;
                    var scene = track.At(time);
                    var mask = scene?.Id == input.Id ? new FocusRaster(project, scene, plan.Placement,
                        plan.Width, plan.Height, time, track.PoseAt(time)).Gray(width, height, plan.Width, plan.Height) : empty;
                    await encoder.WriteInputAsync(mask, cancellation);
                }
                encoder.CompleteInput();
                if (await encoder.WaitAsync(cancellation) != 0)
                    throw new InvalidOperationException(L.Text("聚焦效果准备失败：") + encoder.ErrorText());
                plan.Inputs[input.Index] = path;
            }
            finally { if (!encoder.HasExited) await encoder.StopAsync(graceful: false); }
        }
    }
}
