using DogSC.Core;

namespace DogSC;

public sealed class ExportService
{
    readonly string _ffmpeg;
    FfmpegProcess? _process;
    public event Action<double>? Progress;

    public ExportService(string ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    public async Task<string> ExportAsync(ProjectDocument project, string projectDirectory, ExportOptions options, string outputPath, CancellationToken cancellation)
    {
        var resolved = Resolve(project, projectDirectory);
        var plan = ExportGraph.Build(project, options, resolved);
        var temp = Path.Combine(Path.GetTempPath(), "DogSC", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            if (plan.FrameMaskIndex >= 0)
            {
                var mask = Path.Combine(temp, "frame.bmp");
                MaskImage.WriteBmp(mask, plan.Placement.Width, plan.Placement.Height, plan.Placement.Radius, circle: false);
                plan.Inputs[plan.FrameMaskIndex] = mask;
            }

            if (plan.CameraMaskIndex >= 0)
            {
                var mask = Path.Combine(temp, "camera.bmp");
                MaskImage.WriteBmp(mask, plan.Bubble.Size, plan.Bubble.Size, 0, circle: true);
                plan.Inputs[plan.CameraMaskIndex] = mask;
            }

            var graph = string.Join(";", plan.FilterScript.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (graph.Length > 24000)
            {
                throw new InvalidOperationException("滤镜图太长，这段时间线暂时导不出。可以先少放几段缩放再试。");
            }

            var args = new List<string> { "-y", "-hide_banner", "-loglevel", "warning", "-progress", "pipe:1", "-nostats" };
            for (var index = 0; index < plan.Inputs.Count; index++)
            {
                if (index == plan.FrameMaskIndex || index == plan.CameraMaskIndex)
                {
                    args.Add("-framerate");
                    args.Add(plan.Fps.ToString());
                }

                args.Add("-i");
                args.Add(plan.Inputs[index]);
            }

            args.Add("-filter_complex");
            args.Add(graph);
            if (!options.AudioOnly && plan.VideoLabel is not null)
            {
                args.Add("-map");
                args.Add("[" + plan.VideoLabel + "]");
                args.AddRange(["-c:v", "libx264", "-preset", "fast", "-crf", "18", "-pix_fmt", "yuv420p", "-r", plan.Fps.ToString()]);
            }

            if (plan.AudioLabel is not null)
            {
                args.Add("-map");
                args.Add("[" + plan.AudioLabel + "]");
                args.AddRange(["-c:a", "aac", "-b:a", "192k"]);
            }
            else if (options.AudioOnly)
            {
                throw new InvalidOperationException("这个项目没有可导出的声音。");
            }

            args.AddRange(["-movflags", "+faststart", outputPath]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            _process = FfmpegProcess.Start(_ffmpeg, args);
            _process.Progress += seconds =>
            {
                if (plan.Duration > 0)
                {
                    Progress?.Invoke(Math.Clamp(seconds / plan.Duration, 0, 0.99));
                }
            };
            var code = await _process.WaitAsync(cancellation);
            var error = _process.ErrorText();
            if (code != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length < 128)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "导出失败。" : error.Trim());
            }

            Progress?.Invoke(1);
            return outputPath;
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // Temp masks can wait for the next cleanup.
            }
        }
    }

    public static ResolvedMedia Resolve(ProjectDocument project, string projectDirectory)
    {
        string? PathOf(string? relative) =>
            string.IsNullOrWhiteSpace(relative) ? null : ProjectFile.Media(projectDirectory, relative);

        var videos = new Dictionary<string, string>();
        var mics = new Dictionary<string, string>();
        var systems = new Dictionary<string, string>();
        var cameras = new Dictionary<string, string>();
        foreach (var asset in project.Assets)
        {
            var video = PathOf(asset.Video);
            if (video is not null)
            {
                videos[asset.Id] = video;
            }

            var mic = PathOf(asset.Microphone);
            if (mic is not null && File.Exists(mic))
            {
                mics[asset.Id] = mic;
            }

            var system = PathOf(asset.SystemAudio);
            if (system is not null && File.Exists(system))
            {
                systems[asset.Id] = system;
            }

            var camera = PathOf(asset.Camera);
            if (camera is not null && File.Exists(camera))
            {
                cameras[asset.Id] = camera;
            }
        }

        return new ResolvedMedia { Videos = videos, Microphones = mics, Systems = systems, Cameras = cameras };
    }
}
