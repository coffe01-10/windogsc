using DogSC.Core;

namespace DogSC;

public sealed class ExportService
{
    readonly string _ffmpeg;
    FfmpegProcess? _process;
    public event Action<double>? Progress;
    public event Action<string>? Stage;

    public ExportService(string ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    public async Task<string> ExportAsync(ProjectDocument project, string projectDirectory, ExportOptions options, string outputPath, CancellationToken cancellation)
    {
        var resolved = Resolve(project, projectDirectory);
        var temp = Path.Combine(Path.GetTempPath(), "DogSC", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var partial = Path.Combine(Path.GetDirectoryName(outputPath)!, ".DogSC-" + Guid.NewGuid().ToString("N") + Path.GetExtension(outputPath));
        try
        {
            Stage?.Invoke("正在准备光标和效果…");
            resolved.CursorCommands = Path.Combine(temp, "cursor.commands");
            var plan = ExportGraph.Build(project, options, resolved);
            await CursorExport.PrepareAsync(_ffmpeg, project, plan, temp, cancellation);
            if (plan.FrameMaskIndex >= 0)
            {
                var mask = Path.Combine(temp, "frame.bmp");
                MaskImage.WriteBmp(mask, plan.Placement.Width, plan.Placement.Height, plan.Placement.Radius, circle: false);
                plan.Inputs[plan.FrameMaskIndex] = mask;
            }
            if (plan.ProjectionMaskIndex >= 0)
            {
                var mask = Path.Combine(temp, "projection.bmp");
                var width = plan.Placement.Width;
                var height = plan.Placement.Height;
                MaskImage.WriteBmp(mask, MaskImage.CreateProjectionMask(width, height,
                    PerspectiveGeometry.Corners(width, height, project.Canvas.TiltX, project.Canvas.TiltY)), width, height);
                plan.Inputs[plan.ProjectionMaskIndex] = mask;
            }

            if (plan.CameraMaskIndex >= 0)
            {
                var mask = Path.Combine(temp, "camera.bmp");
                // 形状：circle=圆形 mask；square=圆角矩形；none=直角矩形（radius=0）。
                var circle = plan.CameraShape == "circle";
                var radius = plan.CameraShape == "square" ? plan.CameraCorner : 0;
                MaskImage.WriteBmp(mask, plan.Bubble.Size, plan.Bubble.Size, radius, circle);
                plan.Inputs[plan.CameraMaskIndex] = mask;
            }

            if (plan.FocusMaskIndex >= 0)
            {
                var mask = Path.Combine(temp, "focus.bmp");
                MaskImage.WriteRadialBmp(mask, 256, 144);
                plan.Inputs[plan.FocusMaskIndex] = mask;
            }

            foreach (var region in plan.RegionMasks)
            {
                var mask = Path.Combine(temp, "region" + region.Index + ".bmp");
                MaskImage.WriteBmp(mask, MaskImage.CreateRegionMask(region.Width, region.Height, region.Region), region.Width, region.Height);
                plan.Inputs[region.Index] = mask;
            }

            var graph = string.Join(";", plan.FilterScript.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var graphPath = Path.Combine(temp, "filters.txt");
            await File.WriteAllTextAsync(graphPath, graph, cancellation);

            var args = new List<string> { "-y", "-hide_banner", "-loglevel", "warning", "-progress", "pipe:1", "-nostats" };
            for (var index = 0; index < plan.Inputs.Count; index++)
            {
                if (index == plan.FrameMaskIndex || index == plan.ProjectionMaskIndex || index == plan.CameraMaskIndex || index == plan.FocusMaskIndex
                    || plan.RegionMasks.Any(mask => mask.Index == index)
                    || index == plan.WallpaperIndex || plan.StickerIndices.Contains(index))
                {
                    args.Add("-framerate");
                    args.Add(plan.Fps.ToString());
                }

                args.Add("-i");
                args.Add(plan.Inputs[index]);
            }

            args.Add("-/filter_complex");
            args.Add(graphPath);
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

            args.AddRange(["-movflags", "+faststart", partial]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            Stage?.Invoke("正在渲染成片…");
            _process = FfmpegProcess.Start(_ffmpeg, args);
            _process.Progress += seconds =>
            {
                if (plan.Duration > 0)
                {
                    Progress?.Invoke(Math.Clamp(seconds / plan.Duration, 0, 0.99));
                }
            };
            var code = await _process.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            var error = _process.ErrorText();
            if (code != 0 || !File.Exists(partial) || new FileInfo(partial).Length < 128)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "导出失败。" : error.Trim());
            }

            File.Move(partial, outputPath, overwrite: true);
            Progress?.Invoke(1);
            return outputPath;
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            if (File.Exists(partial)) File.Delete(partial);
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

        // 壁纸/贴图支持项目相对路径和绝对路径两种写法。
        string? Decoration(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var trimmed = path.Trim();
            var absolute = Path.IsPathRooted(trimmed) ? trimmed : ProjectFile.Media(projectDirectory, trimmed);
            return File.Exists(absolute) ? absolute : null;
        }

        string? wallpaper = Decoration(project.Canvas.Wallpaper);
        var stickers = new Dictionary<string, ResolvedSticker>();
        foreach (var image in project.Canvas.Stickers)
        {
            var path = Decoration(image.Path);
            if (path is null) continue;
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(path),
                System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            stickers[image.Id] = new(path, frame.PixelWidth / (double)frame.PixelHeight);
        }

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

        return new ResolvedMedia
        {
            Videos = videos,
            Microphones = mics,
            Systems = systems,
            Cameras = cameras,
            Wallpaper = wallpaper,
            Stickers = stickers,
        };
    }
}
