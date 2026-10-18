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
        var resolved = await Resolve(project, projectDirectory, options);
        if (!options.AudioOnly && FfmpegTools.LocateFfprobe(_ffmpeg) is { } probe)
        {
            var aspects = new Dictionary<string, double>();
            foreach (var cameraPath in resolved.Cameras.Values.Distinct())
            {
                if (!aspects.TryGetValue(cameraPath, out var aspect) && await FfmpegTools.Probe(probe, cameraPath) is { Width: > 0, Height: > 0 } info)
                    aspects[cameraPath] = aspect = info.Width / (double)info.Height;
                if (aspect > 0) resolved.CameraAspects[cameraPath] = aspect;
            }
            resolved.CameraAspect = resolved.CameraAspects.Values.FirstOrDefault(a => a > 0, resolved.CameraAspect);
        }
        var temp = Path.Combine(Path.GetTempPath(), "DogSC", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var partial = Path.Combine(Path.GetDirectoryName(outputPath)!, ".DogSC-" + Guid.NewGuid().ToString("N") + Path.GetExtension(outputPath));
        try
        {
            Stage?.Invoke(L.Text("正在准备光标和效果…"));
            resolved.CursorCommands = Path.Combine(temp, "cursor.commands");
            var plan = ExportGraph.Build(project, options, resolved);
            await SpatialFocusExport.PrepareAsync(_ffmpeg, project, plan, temp, cancellation);
            await CursorExport.PrepareAsync(_ffmpeg, project, plan, temp, cancellation);
            await StickerBackdropExport.PrepareAsync(_ffmpeg, project, plan, temp, cancellation);
            if (plan.StickerAnimIndex >= 0
                && await StickerAnimatedExport.PrepareAsync(_ffmpeg, project, plan, resolved.Stickers,
                    plan.Width, plan.Height, temp, cancellation) is { } animPath)
            {
                plan.Inputs[plan.StickerAnimIndex] = animPath;
            }
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
                var radius = plan.CameraShape != "circle" && plan.CameraShape != "none" ? plan.CameraCorner : 0;
                MaskImage.WriteBmp(mask, plan.Bubble.Width, plan.Bubble.Height, radius, circle);
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

            if (plan.ChromeIndex >= 0 && plan.Chrome is not null)
            {
                plan.Inputs[plan.ChromeIndex] = ChromeArt.Bake(plan.Chrome, plan.Width, plan.Height,
                    Path.Combine(temp, "chrome.png"));
            }

            if (plan.WallpaperIndex >= 0 && PatternLibrary.IsProcedural(plan.Inputs[plan.WallpaperIndex]))
            {
                // pattern:/flow: 占位串解析成烘焙单：底层 PNG 回填壁纸输入，动态层回填各自输入。
                var bake = PatternLibrary.Resolve(project.Canvas.Wallpaper, plan.Width, plan.Height,
                    project.Canvas.PatternScale, project.Canvas.PatternOpacity);
                if (bake is not null)
                {
                    var (bakedBase, bakedLayers) = await Task.Run(
                        () => PatternArt.Bake(bake, plan.Width, plan.Height, Path.Combine(temp, "wp")), cancellation);
                    plan.Inputs[plan.WallpaperIndex] = bakedBase;
                    for (var layerIndex = 0; layerIndex < bakedLayers.Count; layerIndex++)
                    {
                        plan.Inputs[plan.WallpaperLayers[layerIndex].Index] = bakedLayers[layerIndex];
                    }
                }
            }

            var graph = string.Join(";", plan.FilterScript.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var graphPath = Path.Combine(temp, "filters.txt");
            await File.WriteAllTextAsync(graphPath, graph, cancellation);

            var args = new List<string> { "-y", "-hide_banner", "-loglevel", "warning", "-progress", "pipe:1", "-nostats" };
            for (var index = 0; index < plan.Inputs.Count; index++)
            {
                if (index == plan.StickerMatteIndex || index == plan.StickerMatteCamIndex)
                {
                    // 贴纸背景通道是 64×36 bgra 裸流（qtrle 多帧编码在本机 ffmpeg 上会毁值）。
                    args.Add("-f");
                    args.Add("rawvideo");
                    args.Add("-pixel_format");
                    args.Add("bgra");
                    args.Add("-video_size");
                    args.Add(StickerBackdropExport.Width + "x" + StickerBackdropExport.Height);
                    args.Add("-framerate");
                    args.Add(plan.Fps.ToString());
                }
                else if (index == plan.FrameMaskIndex || index == plan.ProjectionMaskIndex || index == plan.CameraMaskIndex || index == plan.FocusMaskIndex
                    || plan.RegionMasks.Any(mask => mask.Index == index) || index == plan.ChromeIndex
                    || plan.WallpaperLayers.Any(layer => layer.Index == index)
                    || (index == plan.WallpaperIndex && !plan.WallpaperIsVideo) || plan.StickerIndices.Contains(index))
                {
                    args.Add("-framerate");
                    args.Add(plan.Fps.ToString());
                }
                else if (index == plan.WallpaperIndex && plan.WallpaperIsVideo)
                {
                    // 视频壁纸：无限循环铺满时间线，时长由滤镜侧 trim 截断。
                    args.Add("-stream_loop");
                    args.Add("-1");
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
                throw new InvalidOperationException(L.Text("这个项目没有可导出的声音。"));
            }

            // Trim the composed output, so cursor and motion retain the full
            // project's clock and an animation crossing the range starts in phase.
            if (options.RangeStart is { } rangeStart)
                args.AddRange(["-ss", rangeStart.ToString("R", System.Globalization.CultureInfo.InvariantCulture)]);
            if (options.RangeDuration is { } rangeDuration)
                args.AddRange(["-t", rangeDuration.ToString("R", System.Globalization.CultureInfo.InvariantCulture)]);
            args.AddRange(["-movflags", "+faststart", partial]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            Stage?.Invoke(L.Text("正在渲染成片…"));
            _process = FfmpegProcess.Start(_ffmpeg, args);
            _process.Progress += seconds =>
            {
                var exportDuration = options.RangeDuration ?? plan.Duration;
                if (exportDuration > 0)
                {
                    Progress?.Invoke(Math.Clamp(seconds / exportDuration, 0, 0.99));
                }
            };
            var code = await _process.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            var error = _process.ErrorText();
            if (code != 0 || !File.Exists(partial) || new FileInfo(partial).Length < 128)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? L.Text("导出失败。") : error.Trim());
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
                if (Environment.GetEnvironmentVariable("DOGSC_KEEP_TEMP") is null)
                    Directory.Delete(temp, recursive: true);
            }
            catch
            {
                // Temp masks can wait for the next cleanup.
            }
        }
    }

    public static async Task<ResolvedMedia> Resolve(ProjectDocument project, string projectDirectory, ExportOptions options)
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

        // 程序生成背景（pattern:/flow:）是规格占位串不是文件，原样放行给导出图解析。
        string? wallpaper = PatternLibrary.IsProcedural(project.Canvas.Wallpaper)
            ? project.Canvas.Wallpaper
            : Decoration(project.Canvas.Wallpaper);
        var stickers = new Dictionary<string, ResolvedSticker>();
        var (canvasW, canvasH) = CanvasLayout.OutputSize(project.Width, project.Height,
            project.Canvas.Aspect, options.Resolution);
        var bakeDir = Path.Combine(Path.GetTempPath(), "DogSC-StickerBake");
        foreach (var image in project.Canvas.Stickers)
        {
            var path = Decoration(image.Path);
            if (path is null) continue;
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(path),
                System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var aspect = frame.PixelWidth / (double)frame.PixelHeight;
            var contentW = CanvasLayout.Even((int)Math.Round(Math.Max(image.Width, 0.02) * canvasW));
            var contentH = CanvasLayout.Even(Math.Max(2, (int)Math.Round(contentW / aspect)));
            var baked = await Task.Run(() => StickerArt.Bake(path, image, contentW, contentH, bakeDir));
            stickers[image.Id] = new(baked.Path, aspect, baked.Margin);
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
