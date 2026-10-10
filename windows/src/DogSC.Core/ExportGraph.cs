using System.Globalization;
using System.Text;

namespace DogSC.Core;

public sealed class ExportOptions
{
    public bool AudioOnly { get; init; }
    public string Resolution { get; init; } = "source";
    public int Fps { get; init; } = 30;
}

/// <param name="Margin">烘焙图四边留边占内容宽的比例（对称留边，中心对齐）。</param>
public sealed record ResolvedSticker(string Path, double Aspect, double Margin = 0);
public sealed record RegionMask(int Index, int Width, int Height, SoftRegion Region);
/// <summary>动态壁纸层输入：烘好的透明 PNG 按逐帧表达式位移叠加到背景上。</summary>
public sealed record WallpaperLayerInput(int Index, string X, string Y);

public sealed class ResolvedMedia
{
    public required IReadOnlyDictionary<string, string> Videos { get; init; }
    public required IReadOnlyDictionary<string, string> Microphones { get; init; }
    public required IReadOnlyDictionary<string, string> Systems { get; init; }
    public required IReadOnlyDictionary<string, string> Cameras { get; init; }
    /// <summary>壁纸图片的实际路径；null = 用纯色背景。</summary>
    public string? Wallpaper { get; init; }
    /// <summary>贴图实际路径；null = 不画贴图。</summary>
    public string? Sticker { get; init; }
    /// <summary>贴图原始宽高比（宽/高），解析不出来就 1。</summary>
    public double StickerAspect { get; init; } = 1;
    public IReadOnlyDictionary<string, ResolvedSticker> Stickers { get; init; } = new Dictionary<string, ResolvedSticker>();
    public double CameraAspect { get; set; } = 16d / 9;
    public string CursorCommands { get; set; } = "cursor.commands";
}

public sealed class ExportPlan
{
    public List<RegionMask> RegionMasks { get; init; } = [];
    public List<int> StickerIndices { get; init; } = [];
    public required List<string> Inputs { get; init; }
    public required string FilterScript { get; init; }
    public string? VideoLabel { get; init; }
    public string? AudioLabel { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Duration { get; init; }
    public int Fps { get; init; }
    public FramePlacement Placement { get; init; }
    public int FrameMaskIndex { get; init; } = -1;
    public int ProjectionMaskIndex { get; init; } = -1;
    public int CameraMaskIndex { get; init; } = -1;
    public int FocusMaskIndex { get; init; } = -1;
    /// <summary>光标贴图输入（-1 = 不画光标）。</summary>
    public int CursorIndex { get; init; } = -1;
    /// <summary>点击反馈圈贴图输入（-1 = 不画）。</summary>
    public int RippleIndex { get; init; } = -1;
    /// <summary>光标贴图叠加位置：画面帧的左上角在画布上的偏移。</summary>
    public int CursorOffsetX { get; init; }
    public int CursorOffsetY { get; init; }
    /// <summary>壁纸图片输入（-1 = 纯色背景）。</summary>
    public int WallpaperIndex { get; init; } = -1;
    /// <summary>动态壁纸层（输入索引 + overlay 逐帧位移表达式），App 侧按序烘 PNG 回填路径。</summary>
    public List<WallpaperLayerInput> WallpaperLayers { get; init; } = [];
    /// <summary>壁纸是视频（输入加 -stream_loop -1，不加 -framerate）。</summary>
    public bool WallpaperIsVideo { get; init; }
    /// <summary>屏幕外框 chrome PNG 输入（-1 = 无外框）。</summary>
    public int ChromeIndex { get; init; } = -1;
    /// <summary>外框规格（ChromeIndex >= 0 时非空，App 侧据此烘 PNG）。</summary>
    public ChromeSpec? Chrome { get; init; }
    /// <summary>贴图输入（-1 = 不画）。</summary>
    public int StickerIndex { get; init; } = -1;
    /// <summary>局部柔化遮罩输入（-1 = 不柔化）。</summary>
    public int SoftMaskIndex { get; init; } = -1;
    /// <summary>贴图目标像素矩形（导出画布坐标）。</summary>
    public (int X, int Y, int W, int H) StickerRect { get; init; }
    /// <summary>贴纸背景通道 mask 视频输入（-1 = 无压制/虚化需求）。</summary>
    public int StickerMatteIndex { get; init; } = -1;
    /// <summary>摄像头虚化通道 mask 视频输入（-1 = 无）；qtrle 预乘会毁掉 A<255 的 RGB，拆单列。</summary>
    public int StickerMatteCamIndex { get; init; } = -1;
    /// <summary>动画贴纸预渲染整帧流（StickerAnimatedExport）的输入序号；-1 表示没有。</summary>
    public int StickerAnimIndex { get; init; } = -1;
    /// <summary>局部柔化矩形（画布像素）。</summary>
    public (int X, int Y, int W, int H) SoftRect { get; init; }
    /// <summary>摄像头形状：circle / square / none（none 时 mask 是全白）。</summary>
    public string CameraShape { get; init; } = "circle";
    /// <summary>方形圆角像素半径。</summary>
    public int CameraCorner { get; init; }
    public BubblePlacement Bubble { get; init; }
}

public static class ExportGraph
{
    public static ExportPlan Build(ProjectDocument project, ExportOptions options, ResolvedMedia media)
    {
        ProjectFile.Normalize(project);
        var placed = TimelineMath.Place(project);
        if (placed.Count == 0)
        {
            throw new InvalidDataException("时间线上没有片段。");
        }

        var slices = BuildPieces(project, placed, media);
        var motion = new ZoomMotion(project);
        var duration = placed[^1].End;
        var fps = options.Fps is 60 ? 60 : 30;
        var (canvasWidth, canvasHeight) = CanvasLayout.OutputSize(project.Width, project.Height, project.Canvas.Aspect, options.Resolution);
        var placement = CanvasLayout.Place(canvasWidth, canvasHeight, project.Width, project.Height, project.Canvas);
        // 屏幕外框：启用后卡片圆角改成 chrome 内容圆角，让卡片底角贴合外框轮廓。
        var chrome = ChromeGeometry.Resolve(project.Canvas, placement, canvasWidth, canvasHeight);
        if (chrome is not null)
        {
            placement = placement with { Radius = (int)Math.Round(chrome.ContentRadius) };
        }
        var bubble = CanvasLayout.Bubble(canvasWidth, canvasHeight, project.Camera, media.CameraAspect);
        var inputs = new List<string>();
        var videoIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var micIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var systemIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cameraIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int IndexFor(Dictionary<string, int> map, string path)
        {
            if (map.TryGetValue(path, out var index))
            {
                return index;
            }

            index = inputs.Count;
            inputs.Add(path);
            map[path] = index;
            return index;
        }

        foreach (var slice in slices)
        {
            IndexFor(videoIndex, slice.VideoPath);
            if (slice.MicrophonePath is not null)
            {
                IndexFor(micIndex, slice.MicrophonePath);
            }

            if (slice.SystemPath is not null)
            {
                IndexFor(systemIndex, slice.SystemPath);
            }

            if (slice.CameraPath is not null)
            {
                IndexFor(cameraIndex, slice.CameraPath);
            }
        }

        // 任一片段有摄像头就导出画中画；缺摄像头的片段留空，由 overlay 的时间窗跳过。
        var includeCamera = !options.AudioOnly && !project.Camera.Hidden && slices.Any(slice => slice.CameraPath is not null);
        var script = new StringBuilder();
        var videoLabels = new List<string>();
        var regionMasks = new List<RegionMask>();
        var regionStep = 0;
        var stickerIndices = new List<int>();
        var stickerMatteIndex = -1;
        var stickerAnimIndex = -1;
        var stickerMatteCamIndex = -1;
        var cameraSlices = new List<(int Index, string Label, double Start, double End)>();
        if (!options.AudioOnly)
        {
            for (var index = 0; index < slices.Count; index++)
            {
                var slice = slices[index];
                var label = "v" + index;
                script.Append('[').Append(videoIndex[slice.VideoPath]).Append(":v]")
                    .Append("trim=start=").Append(F(slice.SourceStart)).Append(":end=").Append(F(slice.SourceEnd))
                    .Append(",setpts=(PTS-STARTPTS)/").Append(F(slice.Speed)).Append(",fps=").Append(fps)
                    .Append(",scale=").Append(project.Width).Append(':').Append(project.Height).Append("[source").Append(index).AppendLine("]");
                var sourceLabel = "source" + index;
                foreach (var soft in project.Canvas.SoftRegions.Where(x => x.Enabled && x.CoordinateSpace == "source"))
                    sourceLabel = ApplyRegion(script, inputs, regionMasks, sourceLabel, soft, project.Width, project.Height,
                        fps, slice.TimelineDuration, slice.TimelineStart, regionStep++);
                script.Append('[').Append(sourceLabel).Append(']')
                    .Append(VideoChain(slice, project.Width, project.Height, fps, project.Canvas.Crop, motion))
                    .Append('[').Append(label).AppendLine("]");
                videoLabels.Add(label);
                if (includeCamera && slice.CameraPath is not null)
                {
                    var cameraLabel = "c" + index;
                    script.Append('[').Append(cameraIndex[slice.CameraPath]).Append(":v]")
                        .Append(CameraChain(slice, bubble.Width, bubble.Height, fps, project.Camera))
                        .Append('[').Append(cameraLabel).AppendLine("]");
                    cameraSlices.Add((index, cameraLabel, slice.TimelineStart, slice.TimelineStart + slice.TimelineDuration));
                }
            }
        }

        string? videoMap = null;
        var frameMaskIndex = -1;
        var projectionMaskIndex = -1;
        var cameraMaskIndex = -1;
        var focusMaskIndex = -1;
        var cursorIndex = -1;
        var rippleIndex = -1;
        var softMaskIndex = -1;
        var stickerIndex = -1;
        var chromeIndex = -1;
        var wallpaperIndex = -1;
        var wallpaperLayers = new List<WallpaperLayerInput>();
        var wallpaperIsVideo = false;
        var stickerRect = (0, 0, 0, 0);
        var softRect = (0, 0, 0, 0);
        if (!options.AudioOnly)
        {
            var concatenated = Join(script, videoLabels, "vcat", video: true);
            if (project.Canvas.Cursor.Mode != "hidden" && project.Assets.Any(asset => asset.Cursor.Count > 0))
            {
                cursorIndex = inputs.Count;
                inputs.Add("");
                var commands = media.CursorCommands.Replace('\\', '/').Replace(":", "\\:").Replace("'", "'\\''");
                script.Append(concatenated).Append("sendcmd=f='").Append(commands).AppendLine("'[cursorcommands]");
                script.Append('[').Append(cursorIndex).AppendLine(":v]setpts=PTS-STARTPTS[cursorimg]");
                script.AppendLine("[cursorcommands][cursorimg]overlay@pointer=x=-10000:y=-10000:format=auto:shortest=1[cursored]");
                concatenated = "[cursored]";
                if (project.Canvas.Cursor.ClickRipple && project.Assets.Any(asset => asset.Cursor.Any(point => point.Click)))
                {
                    rippleIndex = inputs.Count;
                    inputs.Add("");
                    script.Append('[').Append(rippleIndex).AppendLine(":v]setpts=PTS-STARTPTS[rippleimg]");
                    script.Append(concatenated).AppendLine("[rippleimg]overlay@ripple=x=-10000:y=-10000:format=auto:shortest=1[withclick]");
                    concatenated = "[withclick]";
                }
            }
            var needsMask = placement.Radius > 0 || placement.ShadowBlur > 0;
            if (needsMask)
            {
                frameMaskIndex = inputs.Count;
                inputs.Add("");
            }

            script.Append(concatenated).Append("scale=")
                .Append(placement.Width).Append(':').Append(placement.Height)
                .Append(":flags=lanczos,format=rgba[scaled]");
            script.AppendLine();
            if (needsMask)
            {
                var split = placement.ShadowBlur > 0 ? ",split[framea][shadowsrc]" : "[framea]";
                script.Append('[').Append(frameMaskIndex).Append(":v]format=rgba,loop=loop=-1:size=1:start=0,trim=duration=")
                    .Append(F(Math.Max(duration, 0.1)))
                    .Append(",setpts=PTS-STARTPTS,fps=").Append(fps).Append(",alphaextract").Append(split)
                    .AppendLine();
                script.Append("[scaled][framea]alphamerge[rounded]").AppendLine();
            }
            else
            {
                script.Append("[scaled]format=rgba[rounded]").AppendLine();
            }

            var under = "bg";
            var proceduralWallpaper = PatternLibrary.IsProcedural(media.Wallpaper);
            var wallpaperBake = proceduralWallpaper
                ? PatternLibrary.Resolve(media.Wallpaper, canvasWidth, canvasHeight,
                    project.Canvas.PatternScale, project.Canvas.PatternOpacity)
                : null;
            if (!string.IsNullOrWhiteSpace(media.Wallpaper) && (proceduralWallpaper || File.Exists(media.Wallpaper)))
            {
                // 壁纸：图片单帧循环或视频（输入侧 -stream_loop 循环）cover 铺满画布，可选高斯模糊。
                // 程序生成背景（pattern:/flow:）用烘焙好的 PNG，路径由导出服务回填，不糊化。
                wallpaperIndex = inputs.Count;
                inputs.Add(media.Wallpaper);
                wallpaperIsVideo = !proceduralWallpaper && IsVideoPath(media.Wallpaper);
                var blur = proceduralWallpaper ? 0 : Math.Clamp(project.Canvas.WallpaperBlur, 0, 1);
                script.Append('[').Append(wallpaperIndex).Append(":v]scale=")
                    .Append(canvasWidth).Append(':').Append(canvasHeight)
                    .Append(":force_original_aspect_ratio=increase,crop=")
                    .Append(canvasWidth).Append(':').Append(canvasHeight)
                    .Append(",format=rgba,");
                if (!wallpaperIsVideo)
                {
                    script.Append("loop=loop=-1:size=1:start=0,");
                }
                script.Append("trim=duration=")
                    .Append(F(Math.Max(duration, 0.1))).Append(",setpts=PTS-STARTPTS,fps=").Append(fps);
                if (blur > 0.03)
                {
                    script.Append(",gblur=sigma=").Append(F(6 + blur * 30));
                }

                var hasLayers = wallpaperBake is { Layers.Count: > 0 };
                // 有动态层时基底先入 [bg0]，由层链合成出 [bg]——label 只能被引用一次，
                // 直接产 [bg] 再让 overlay 消费会导致后续卡片垫层拿到未合成流。
                script.Append('[').Append(hasLayers ? "bg0" : "bg").AppendLine("]");
                if (wallpaperBake is { Layers.Count: > 0 })
                {
                    // 动态层：每层一个 PNG 输入 + overlay eval=frame 逐帧位移表达式。
                    var previous = "bg0";
                    for (var layerIndex = 0; layerIndex < wallpaperBake.Layers.Count; layerIndex++)
                    {
                        var layer = wallpaperBake.Layers[layerIndex];
                        var inputIndex = inputs.Count;
                        inputs.Add("wp-layer" + layerIndex);
                        wallpaperLayers.Add(new WallpaperLayerInput(inputIndex, layer.X, layer.Y));
                        var stream = "wpl" + layerIndex;
                        var next = layerIndex == wallpaperBake.Layers.Count - 1 ? "bg" : "bgw" + layerIndex;
                        script.Append('[').Append(inputIndex).Append(":v]format=rgba,loop=loop=-1:size=1:start=0,trim=duration=")
                            .Append(F(Math.Max(duration, 0.1))).Append(",fps=").Append(fps)
                            .Append('[').Append(stream).AppendLine("]");
                        script.Append('[').Append(previous).Append("][").Append(stream)
                            .Append("]overlay=x='").Append(layer.X).Append("':y='").Append(layer.Y)
                            .Append("':format=auto:eval=frame[").Append(next).AppendLine("]");
                        previous = next;
                    }
                }
            }
            else
            {
                var background = ProjectFile.NormalizeColor(project.Canvas.Background)[1..];
                script.Append("color=c=0x").Append(background)
                    .Append(":s=").Append(canvasWidth).Append('x').Append(canvasHeight)
                    .Append(":r=").Append(fps)
                    .Append(":d=").Append(F(Math.Max(duration, 0.1)))
                    .AppendLine(",format=rgba[bg]");
            }


            if (placement.ShadowBlur > 0)
            {
                script.Append("[shadowsrc]boxblur=").Append(Math.Max(1, placement.ShadowBlur)).Append(":2[shadowa]").AppendLine();
                script.Append("color=c=black:s=").Append(placement.Width).Append('x').Append(placement.Height)
                    .Append(":r=").Append(fps)
                    .Append(":d=").Append(F(Math.Max(duration, 0.1)))
                    .AppendLine(",format=rgba[shadowc]");
                script.Append("[shadowc][shadowa]alphamerge,colorchannelmixer=aa=")
                    .Append(F(placement.ShadowAlpha))
                    .AppendLine("[shadow]");
                script.Append("[bg][shadow]overlay=")
                    .Append(placement.X + placement.ShadowOffset / 3).Append(':')
                    .Append(placement.Y + placement.ShadowOffset)
                    .AppendLine(":format=auto:shortest=1[bgsh]");
                under = "bgsh";
            }

            // 倾斜/透视：对圆角内容帧做四角透视变换，再叠到背景。
            var tilted = "rounded";
            if (Math.Abs(project.Canvas.TiltX) > 0.01 || Math.Abs(project.Canvas.TiltY) > 0.01)
            {
                var corners = PerspectiveGeometry.Corners(placement.Width, placement.Height, project.Canvas.TiltX, project.Canvas.TiltY);
                script.Append('[').Append(tilted).Append("]perspective=")
                    .Append(string.Join(":", corners.SelectMany(p => new[] { F(p.X), F(p.Y) })))
                    .AppendLine(":sense=destination:eval=init[tilted]");
                tilted = "tilted";
                projectionMaskIndex = inputs.Count;
                inputs.Add("");
                script.AppendLine("[tilted]format=rgba,split[tiltcolor][tiltalpha0]");
                script.AppendLine("[tiltalpha0]alphaextract[tiltalpha]");
                script.Append('[').Append(projectionMaskIndex).AppendLine(":v]format=gray[quadmask]");
                script.AppendLine("[tiltalpha][quadmask]blend=all_mode=multiply[projectedalpha]");
                script.AppendLine("[tiltcolor][projectedalpha]alphamerge[projected]");
                tilted = "projected";
            }

            if (project.Canvas.Intro == "zoom")
            {
                var factor = "(0.9+0.1*min(t/" + F(CanvasLayout.IntroDuration) + "\\,1))";
                script.Append('[').Append(tilted).Append("]scale=w='trunc(iw*").Append(factor)
                    .Append("/2)*2':h='trunc(ih*").Append(factor).Append("/2)*2':eval=frame,pad=")
                    .Append(placement.Width).Append(':').Append(placement.Height)
                    .AppendLine(":(ow-iw)/2:(oh-ih)/2:color=0x00000000:eval=frame[introzoom]");
                tilted = "introzoom";
            }
            // 开场淡入：fade alpha 让 rgba 内容帧从透明淡入，叠到背景上。
            if (project.Canvas.Intro is "fade" or "rise" or "zoom")
            {
                script.Append('[').Append(tilted).Append("]fade=t=in:st=0:d=")
                    .Append(F(CanvasLayout.IntroDuration)).AppendLine(":alpha=1[intro]");
                tilted = "intro";
            }

            // 生成流（color 源）做主、解码视频做辅时 ffmpeg overlay 的帧配对会漂移，
            // 这里不用 overlay，改走 pad+maskedmerge：先把画面垫到全画布透明帧上，
            // 再用它的 alpha 当蒙版和背景合并，时间轴严格按 pts 对齐。
            var rise = project.Canvas.Intro == "rise";
            var riseDy = CanvasLayout.Even((int)Math.Round(placement.Height * 0.08));
            var padH = placement.Height + placement.Y + riseDy;
            var padH2 = CanvasLayout.Even(Math.Max(padH, canvasHeight + riseDy));
            script.Append('[').Append(tilted).Append("]pad=")
                .Append(canvasWidth).Append(':').Append(padH2).Append(':')
                .Append(placement.X).Append(':').Append(placement.Y + riseDy)
                .AppendLine(":color=black@0,format=rgba[pl0]");
            if (rise)
            {
                script.Append("[pl0]crop=").Append(canvasWidth).Append(':').Append(canvasHeight)
                    .Append(":0:'").Append(riseDy).Append("*min(t/")
                    .Append(F(CanvasLayout.IntroDuration)).Append("\\,1)'[placed]").AppendLine();
            }
            else
            {
                script.Append("[pl0]crop=").Append(canvasWidth).Append(':').Append(canvasHeight)
                    .AppendLine(":0:0[placed]");
            }
            var placedSrc = "placed";
            // 屏幕外框 chrome：静态 PNG 叠到透明画布卡片上，放在运动透视之前跟随运镜一起变。
            if (chrome is not null)
            {
                chromeIndex = inputs.Count;
                inputs.Add("");
                script.Append('[').Append(chromeIndex).Append(":v]format=rgba,loop=loop=-1:size=1:start=0,trim=duration=")
                    .Append(F(Math.Max(duration, 0.1))).Append(",setpts=PTS-STARTPTS,fps=").Append(fps).AppendLine("[chrome]");
                script.Append("[placed][chrome]overlay=0:0:format=auto:shortest=1").AppendLine("[plchr]");
                placedSrc = "plchr";
            }
            // 屏幕运动轨道：ffmpeg 9.0.2 的 perspective 不支持逐帧表达式，
            // 切成 15Hz 常量角点分段（trim+perspective(eval=init) 再 concat），
            // 每段取中点采样的单应四角（tilt+缩放+rotZ+位移）。
            if (project.ScreenMotions.Count > 0)
            {
                var viewport = new ScreenAnchorViewport(canvasWidth, canvasHeight, placement.Width, placement.Height);
                var screenTrack = new ScreenMotionTrack(project.ScreenMotions);
                var baseSample = screenTrack.SampleLayout(-1, null, viewport);
                const double segDur = 1.0 / 15;
                var segCount = Math.Max(1, (int)Math.Ceiling(duration / segDur - 0.0001));
                script.Append('[').Append(placedSrc).Append("]split=").Append(segCount);
                for (var i = 0; i < segCount; i++) script.Append("[pms").Append(i).Append(']');
                script.AppendLine();
                for (var i = 0; i < segCount; i++)
                {
                    var s0 = i * segDur;
                    var mid = Math.Min(s0 + segDur / 2, duration - 0.0001);
                    var corners = MotionExport.ScreenCorners(
                        screenTrack.SampleLayout(mid, null, viewport), baseSample,
                        canvasWidth, canvasHeight, placement.X, placement.Y, placement.Width, placement.Height);
                    script.Append("[pms").Append(i).Append("]trim=start=").Append(F(s0))
                        .Append(":end=").Append(F(Math.Min(s0 + segDur, duration)))
                        .Append(",setpts=PTS-STARTPTS+").Append(F(s0)).Append("/TB,perspective=")
                        .Append(string.Join(":", corners.SelectMany(p => new[] { F(p.X), F(p.Y) })))
                        .Append(":sense=destination:eval=init[pmsq").Append(i).AppendLine("]");
                }
                for (var i = 0; i < segCount; i++) script.Append("[pmsq").Append(i).Append(']');
                script.Append("concat=n=").Append(segCount).AppendLine(":v=1:a=0[pmot]");
                placedSrc = "pmot";
            }
            // placedSrc 要被引用两次（挖洞 alpha + 合成主输入）：label 只能用一次，
            // 二次引用会被 ffmpeg 静默改链到未命名输入流——先 split 出两份。
            script.Append('[').Append(placedSrc).AppendLine("]split[plmain][plmask]");
            script.AppendLine("[plmask]alphaextract,negate[bgn]");
            // —— 贴纸背景通道（压屏幕/虚化）：先把背景拆出一份供压制合并 ——
            var stickerTrack = new StickerMotion.Track(project.Canvas.Stickers);
            var backdropMax = StickerBackdropMax(stickerTrack, duration, fps);
            // 背景挖洞（screen 区域 alpha=0）后叠在画面流上，视频流做主避免 overlay 帧配对漂移。
            script.Append('[').Append(under).AppendLine("]format=rgba[bgrgb]");
            script.AppendLine("[bgrgb][bgn]alphamerge[bga]");
            script.Append("[plmain][bga]overlay=0:0:format=auto:shortest=1")
                .AppendLine("[composed]");
            videoMap = "composed";
            var camOn = includeCamera && cameraSlices.Count > 0;
            var camBlur = backdropMax.CameraBlur > 0 && camOn;
            var needMatte = (backdropMax.CameraSuppression > 0 && camOn) || backdropMax.ScreenBlur > 0;
            if (needMatte)
            {
                stickerMatteIndex = inputs.Count;
                inputs.Add("");
                var planeList = string.Join("+",
                    new[] { backdropMax.CameraSuppression > 0 && camOn ? "g" : null,
                        backdropMax.ScreenBlur > 0 ? "b" : null }.Where(p => p is not null));
                var planeTags = string.Concat(
                    new[] { backdropMax.CameraSuppression > 0 && camOn ? "[bdcs]" : null,
                        backdropMax.ScreenBlur > 0 ? "[bdsb]" : null }.Where(p => p is not null));
                script.Append('[').Append(stickerMatteIndex)
                    .Append(":v]format=argb,extractplanes=").Append(planeList).Append(planeTags).AppendLine();
                if (camBlur)
                {
                    stickerMatteCamIndex = inputs.Count;
                    inputs.Add("");
                    script.Append('[').Append(stickerMatteCamIndex).AppendLine(":v]format=gray[bdcb]");
                }
                if (backdropMax.ScreenBlur > 0)
                {
                    // 虚化：模糊画面贴 blur 蒙版作 alpha，按帧盖在清晰画面上。
                    script.Append("[bdsb]scale=").Append(canvasWidth).Append(':').Append(canvasHeight)
                        .AppendLine(",format=gray[bdsbm]");
                    script.Append('[').Append(videoMap).Append("]gblur=sigma=")
                        .Append(F(backdropMax.ScreenBlur)).AppendLine(",format=rgb24[vblur0]");
                    script.AppendLine("[vblur0][bdsbm]alphamerge[vblura]");
                    script.Append('[').Append(videoMap).AppendLine("][vblura]overlay=0:0:format=auto[vbdbl]");
                    videoMap = "vbdbl";
                }
            }
            if (includeCamera && cameraSlices.Count > 0)
            {
                cameraMaskIndex = inputs.Count;
                inputs.Add("");
                // 圆形 mask 只循环一次，再 split 给每个有摄像头的片段。
                script.Append('[').Append(cameraMaskIndex).Append(":v]format=rgba,loop=loop=-1:size=1:start=0,fps=")
                    .Append(fps).Append(",alphaextract,split=")
                    .Append(cameraSlices.Count);
                foreach (var cam in cameraSlices)
                {
                    script.Append("[cama").Append(cam.Index).Append(']');
                }

                script.AppendLine();
                var cameraUnder = videoMap;
                var step = 0;
                var entry = project.Camera.Entry;
                // 摄像头运动轨道：以项目摄像头外观为基态，逐帧几何表达式。
                CameraMotionTrack? camTrack = project.CameraMotions.Count > 0
                    ? new CameraMotionTrack(project.CameraMotions) : null;
                var camBase = new CameraMotionState
                {
                    Layout = project.Camera.Shape is "circle" or "horizontal" or "vertical" or "original"
                        ? project.Camera.Shape : "square",
                    X = project.Camera.CenterX, Y = project.Camera.CenterY,
                    Size = project.Camera.Size, Roundness = project.Camera.Corner * 2,
                    Opacity = project.Camera.Hidden ? 0 : 1,
                };
                // 进出动画时长：跟开场一致，太长会吃掉短片段。
                var anim = Math.Min(0.5, Math.Max(0.12, cameraSlices.Min(c => c.End - c.Start) / 4));
                foreach (var cam in cameraSlices)
                {
                    var src = "camr" + cam.Index;
                    script.Append('[').Append(cam.Label).Append("][cama").Append(cam.Index)
                        .Append("]alphamerge[").Append(src).AppendLine("]");
                    // 贴纸压制摄像头 / 摄像头虚化：mask 平面缩到画中画尺寸后 maskedmerge。
                    if (backdropMax.CameraSuppression > 0 && includeCamera)
                    {
                        script.Append("color=c=black@0:s=").Append(bubble.Width).Append('x')
                            .Append(bubble.Height).Append(":r=").Append(fps).Append(":d=")
                            .Append(F(Math.Max(duration, 0.1))).Append(",format=rgb24[cblk]").AppendLine();
                        script.Append("[bdcs]scale=").Append(bubble.Width).Append(':').Append(bubble.Height)
                            .AppendLine(",format=gray[bdcsm]");
                        script.AppendLine("[cblk][bdcsm]alphamerge[cblkam]");
                        script.Append('[').Append(src).Append("][cblkam]overlay=0:0:format=auto[csup]").AppendLine();
                        src = "csup";
                    }
                    if (backdropMax.CameraBlur > 0 && includeCamera)
                    {
                        var camSigma = F(Math.Max(0.5, backdropMax.CameraBlur * bubble.Height / canvasHeight));
                        script.Append("[bdcb]scale=").Append(bubble.Width).Append(':').Append(bubble.Height)
                            .AppendLine(",format=gray[bdcbm]");
                        script.Append('[').Append(src).Append("]gblur=sigma=").Append(camSigma)
                            .AppendLine(",format=rgb24[cblur]");
                        script.AppendLine("[cblur][bdcbm]alphamerge[cbluram]");
                        script.Append('[').Append(src).AppendLine("][cbluram]overlay=0:0:format=auto[cblu]");
                        src = "cblu";
                    }
                    // 进出场：fade=对合并后的 rgba 做 alpha 淡入淡出。
                    if (entry is "fade" or "rise")
                    {
                        var fadeOut = Math.Max(cam.Start, cam.End - anim);
                        script.Append('[').Append(src).Append("]fade=t=in:st=").Append(F(cam.Start))
                            .Append(":d=").Append(F(anim)).Append(":alpha=1,fade=t=out:st=")
                            .Append(F(fadeOut)).Append(":d=").Append(F(anim))
                            .Append(":alpha=1[camf").Append(cam.Index).AppendLine("]");
                        src = "camf" + cam.Index;
                    }

                    var next = step == cameraSlices.Count - 1 ? "vfinal" : "cover" + step;
                    if (camTrack is not null)
                    {
                        // 运动轨道：ffmpeg 9.0.2 scale 不能逐帧改输出尺寸——改走
                        // 15Hz 常量分段链（trim+scale+geq 透明度+定点 overlay），
                        // 与屏幕运动的 perspective 分段同一逼近策略。
                        var segCount = Math.Max(1, (int)Math.Ceiling((cam.End - cam.Start) * 15));
                        var window = MotionExport.SampleTrack(cam.Start, cam.End, fps, segCount,
                            t => MotionExport.CameraPixels(
                                camTrack.Sample(t, camBase, media.CameraAspect,
                                    (double)canvasWidth / canvasHeight), canvasWidth, canvasHeight));
                        script.Append('[').Append(src).Append("]split=").Append(segCount);
                        for (var i = 0; i < segCount; i++) script.Append("[cms").Append(cam.Index).Append('_').Append(i).Append(']');
                        script.AppendLine();
                        var segDuration = (cam.End - cam.Start) / segCount;
                        for (var i = 0; i < segCount; i++)
                        {
                            var segStart = cam.Start + i * segDuration;
                            var segEnd = i == segCount - 1 ? cam.End : segStart + segDuration;
                            var frame = window[Math.Min(i, window.Count - 1)].Value;
                            var label = "cmm" + cam.Index + "_" + i;
                            script.Append("[cms").Append(cam.Index).Append('_').Append(i).Append(']');
                            script.Append("trim=start=").Append(F(segStart)).Append(":end=").Append(F(segEnd))
                                .Append(",setpts=PTS-STARTPTS+").Append(F(segStart)).Append("/TB,scale=")
                                .Append(Math.Max(2, (int)Math.Round(frame.W))).Append(':')
                                .Append(Math.Max(2, (int)Math.Round(frame.H))).Append(",format=rgba");
                            var opacity = Math.Clamp(frame.Opacity, 0, 1);
                            if (opacity < 0.999)
                            {
                                script.Append(",geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*")
                                    .Append(F(opacity)).Append('\'');
                            }
                            script.Append('[').Append(label).AppendLine("]");
                            var underLabel = i == 0 ? cameraUnder : "cmo" + cam.Index + "_" + (i - 1);
                            var over = i == segCount - 1 ? next : "cmo" + cam.Index + "_" + i;
                            script.Append('[').Append(underLabel).Append("][").Append(label)
                                .Append("]overlay=").Append(F(frame.X)).Append(':').Append(F(frame.Y))
                                .Append(":format=auto:eof_action=pass:repeatlast=0:enable='gte(t,")
                                .Append(F(segStart)).Append(")*lt(t,").Append(F(segEnd)).Append(")'")
                                .Append('[').Append(over).AppendLine("]");
                        }
                    }
                    else
                    {
                        // rise：在入场窗口内 y 从下方滑到位。
                        var oxExpr = bubble.X.ToString();
                        var oyExpr = entry == "rise"
                            ? F(bubble.Y) + "+(1-min((t-" + F(cam.Start) + ")/" + F(anim) + "\\,1))*" + F(bubble.Height * 0.4)
                            : bubble.Y.ToString();
                        // 只在该片段的时间窗内叠加画中画，缺摄像头的片段保持背景画面。
                        script.Append('[').Append(cameraUnder).Append("][").Append(src)
                            .Append("]overlay=").Append(oxExpr).Append(':').Append(oyExpr)
                            .Append(":format=auto:eval=frame:eof_action=pass:repeatlast=0:enable='gte(t,").Append(F(cam.Start))
                            .Append(")*lt(t,").Append(F(cam.End)).Append(")'")
                            .Append('[').Append(next).AppendLine("]");
                    }
                    cameraUnder = next;
                    step++;
                }

                videoMap = "vfinal";
            }

            foreach (var soft in project.Canvas.SoftRegions.Where(x => x.Enabled && x.CoordinateSpace == "canvas"))
            {
                videoMap = ApplyRegion(script, inputs, regionMasks, videoMap!, soft, canvasWidth, canvasHeight, fps, duration, 0, regionStep++);
                if (softMaskIndex < 0)
                {
                    softMaskIndex = regionMasks[^1].Index;
                    softRect = ((int)Math.Round(soft.X * canvasWidth), (int)Math.Round(soft.Y * canvasHeight),
                        (int)Math.Round(soft.Width * canvasWidth), (int)Math.Round(soft.Height * canvasHeight));
                }
            }

            // 移轴虚化：径向 mask 把边缘混进 gblur 的模糊层。
            if (project.Canvas.FocusBlur > 0.02)
            {
                focusMaskIndex = inputs.Count;
                inputs.Add("");
                var sigma = F(4 + project.Canvas.FocusBlur * 26);
                script.Append('[').Append(videoMap).AppendLine("]format=gbrp,split[fclear][fblur0]");
                script.Append("[fblur0]gblur=sigma=").Append(sigma).AppendLine(",format=rgb24[fblur]");
                script.Append('[').Append(focusMaskIndex).Append(":v]scale=")
                    .Append(canvasWidth).Append('x').Append(canvasHeight)
                    .AppendLine(",format=gray[fmask]");
                script.AppendLine("[fblur][fmask]alphamerge[fbluram]");
                script.AppendLine("[fclear][fbluram]overlay=0:0:format=auto[focused]");
                videoMap = "focused";
            }

            if (project.Canvas.Stickers.Any(s =>
                    (s.Animation is { Length: > 0 } a && a != "none")
                    || (s.ExitAnimation is { Length: > 0 } e && e != "none")))
            {
                // 动画贴纸整帧透明流输入占位，实际文件由 StickerAnimatedExport 写入。
                stickerAnimIndex = inputs.Count;
                inputs.Add("");
            }

            foreach (var sticker in project.Canvas.Stickers)
            {
                var resolvedSticker = media.Stickers.GetValueOrDefault(sticker.Id)
                    ?? (sticker == project.Canvas.Sticker && media.Sticker is not null ? new ResolvedSticker(media.Sticker, media.StickerAspect) : null);
                if (resolvedSticker is null || !File.Exists(resolvedSticker.Path)) continue;
                var box = CanvasLayout.Sticker(canvasWidth, canvasHeight, sticker, resolvedSticker.Aspect);
                var fx = StickerFx(sticker, stickerTrack, box.X, box.Y, canvasWidth, canvasHeight);
                if (fx.Transformed)
                {
                    // 动画贴纸由 StickerAnimatedExport 预渲染成整帧透明流（stickerAnim 输入），
                    // 不走逐贴纸滤镜：ffmpeg 表达式做大图缩放/位移/透明度太脆弱。
                    continue;
                }
                var stickerPath = resolvedSticker.Path;
                var suffix = stickerIndices.Count.ToString();
                stickerIndex = inputs.Count;
                inputs.Add(stickerPath);
                stickerIndices.Add(stickerIndex);
                // 烘焙图 = 内容 + 对称留边：输入宽 = 内容宽 × (1+2·margin)。
                var w = CanvasLayout.Even((int)Math.Round(box.Width));
                var h = CanvasLayout.Even((int)Math.Round(box.Height));
                var pngW = CanvasLayout.Even((int)Math.Round(w * (1 + 2 * resolvedSticker.Margin)));
                var pngH = CanvasLayout.Even((int)Math.Round(h * (1 + 2 * resolvedSticker.Margin)));
                var sx = box.X - w / 2; var sy = box.Y - h / 2;
                stickerRect = ((int)Math.Round(sx), (int)Math.Round(sy), w, h);
                {
                    script.Append('[').Append(stickerIndex).Append(":v]scale=").Append(pngW).Append(':').Append(pngH)
                        .Append(",format=rgba,loop=loop=-1:size=1:start=0,trim=duration=")
                        .Append(F(Math.Max(duration, 0.1))).Append(",setpts=PTS-STARTPTS,fps=").Append(fps);
                    if (sticker.Opacity < 0.99 || fx.Alpha != "1")
                    {
                        script.Append(",format=gbrap,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*(")
                            .Append(fx.Alpha).Append(")*").Append(F(sticker.Opacity)).Append("',format=rgba");
                    }
                }
                if (Math.Abs(sticker.Rotation) > .01)
                {
                    var angle = F(sticker.Rotation * Math.PI / 180);
                    script.Append(",rotate=").Append(angle).Append(":ow=rotw(").Append(angle).Append("):oh=roth(").Append(angle).Append("):c=none");
                }

                script.Append("[stickerimg").Append(suffix).AppendLine("]");
                script.Append('[').Append(videoMap).Append("][stickerimg").Append(suffix).Append("]overlay=")
                    .Append(fx.CenterX).Append("-w/2+(").Append(fx.OffsetX).Append(")*W:")
                    .Append(fx.CenterY).Append("-h/2+(").Append(fx.OffsetY).Append(")*H:format=auto:enable='")
                    .Append(fx.Enable)
                    .Append("'[stickered").Append(suffix).AppendLine("]");
                videoMap = "stickered" + suffix;
            }

            if (stickerAnimIndex >= 0)
            {
                script.Append('[').Append(videoMap).Append("][").Append(stickerAnimIndex)
                    .Append(":v]overlay=0:0:format=auto[stickeranim]").AppendLine();
                videoMap = "stickeranim";
            }

            script.Append('[').Append(videoMap).Append("]format=yuv420p[vout]").AppendLine();
            videoMap = "vout";
        }

        var audioLabels = new List<string>();
        var wantMic = project.MicrophoneGain > 0.001;
        var wantSystem = project.SystemGain > 0.001;
        // 任一片段有声音就导出音轨；缺轨的片段补等长静音，保证 concat 后时长一致。
        var hasAnyAudio = slices.Any(slice =>
            (wantMic && slice.MicrophonePath is not null) || (wantSystem && slice.SystemPath is not null));
        for (var index = 0; index < slices.Count && hasAnyAudio; index++)
        {
            var slice = slices[index];
            string? mic = null;
            string? system = null;
            if (wantMic && slice.MicrophonePath is not null)
            {
                mic = "m" + index;
                script.Append('[').Append(micIndex[slice.MicrophonePath]).Append(":a]")
                    .Append(AudioChain(slice, project.MicrophoneGain, slice.MicrophoneOffset))
                    .Append('[').Append(mic).AppendLine("]");
            }

            if (wantSystem && slice.SystemPath is not null)
            {
                system = "s" + index;
                script.Append('[').Append(systemIndex[slice.SystemPath]).Append(":a]")
                    .Append(AudioChain(slice, project.SystemGain))
                    .Append('[').Append(system).AppendLine("]");
            }

            if (mic is null && system is null)
            {
                var silent = "q" + index;
                script.Append("anullsrc=channel_layout=stereo:sample_rate=48000,atrim=0:")
                    .Append(F(slice.TimelineDuration)).Append(",asetpts=PTS-STARTPTS[")
                    .Append(silent).AppendLine("]");
                audioLabels.Add(silent);
                continue;
            }

            if (mic is not null && system is not null)
            {
                script.Append('[').Append(mic).Append("][").Append(system)
                    .Append("]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[a")
                    .Append(index).AppendLine("]");
                audioLabels.Add("a" + index);
            }
            else
            {
                audioLabels.Add(mic ?? system!);
            }
        }

        string? audioMap = null;
        if (audioLabels.Count == slices.Count && audioLabels.Count > 0)
        {
            audioMap = Join(script, audioLabels, "aout", video: false).Trim().Trim('[', ']');
        }

        return new ExportPlan
        {
            RegionMasks = regionMasks,
            StickerIndices = stickerIndices,
            StickerMatteIndex = stickerMatteIndex,
            StickerMatteCamIndex = stickerMatteCamIndex,
            StickerAnimIndex = stickerAnimIndex,
            Inputs = inputs,
            FilterScript = script.ToString().Trim(),
            VideoLabel = videoMap,
            AudioLabel = audioMap,
            Width = canvasWidth,
            Height = canvasHeight,
            Duration = duration,
            Fps = fps,
            Placement = placement,
            FrameMaskIndex = frameMaskIndex,
            ProjectionMaskIndex = projectionMaskIndex,
            CameraMaskIndex = cameraMaskIndex,
            FocusMaskIndex = focusMaskIndex,
            CursorIndex = cursorIndex,
            RippleIndex = rippleIndex,
            CursorOffsetX = placement.X,
            CursorOffsetY = placement.Y,
            WallpaperIndex = wallpaperIndex,
            WallpaperLayers = wallpaperLayers,
            WallpaperIsVideo = wallpaperIsVideo,
            ChromeIndex = chromeIndex,
            Chrome = chrome,
            StickerIndex = stickerIndex,
            SoftMaskIndex = softMaskIndex,
            StickerRect = stickerRect,
            SoftRect = softRect,
            CameraShape = project.Camera.Shape,
            CameraCorner = (int)Math.Round(Math.Min(bubble.Width, bubble.Height) * Math.Clamp(project.Camera.Corner, 0, 0.5)),
            Bubble = bubble,
        };
    }

    /// <summary>贴纸背景通道的采样最大值：决定导出图要不要接 matte 与各支路常量。</summary>
    static (bool Any, double ScreenSuppression, double CameraSuppression, double ScreenBlur, double CameraBlur)
        StickerBackdropMax(StickerMotion.Track track, double duration, int fps)
    {
        var max = new StickerMotion.Backdrop();
        var steps = Math.Max(1, (int)Math.Ceiling(duration * fps));
        for (var i = 0; i <= steps; i++)
        {
            var (_, backdrop) = track.SampleAt(i / (double)fps);
            max.Merge(backdrop);
        }
        return (max.ScreenSuppression > 0.001 || max.CameraSuppression > 0.001
            || max.ScreenBlur > 0.001 || max.CameraBlur > 0.001,
            max.ScreenSuppression, max.CameraSuppression, max.ScreenBlur, max.CameraBlur);
    }

    /// <summary>
    /// 每张贴图的 ffmpeg 表达式：可见度（含交接）、缩放（pop）、归一化位移（slide）、
    /// enable 窗口。返回值字符串内逗号已转义（\\,），可直接拼进滤镜参数。
    /// </summary>
    static (string Alpha, string Scale, string OffsetX, string OffsetY, string CenterX, string CenterY,
        string Enable, bool Transformed) StickerFx(StickerImage sticker, StickerMotion.Track track,
            double cx, double cy, int canvasWidth, int canvasHeight)
    {
        var start = sticker.Timing.Start;
        var end = start + sticker.Timing.Duration;
        if (sticker.Timing.Duration <= 0)
        {
            return ("1", "1", "0", "0", F(cx), F(cy), "1", false);
        }
        var w = track.Windows(sticker);
        var enterDur = w.EnterDuration;
        var exitDur = w.ExitDuration;
        var exitPreset = StickerMotion.ExitPreset(sticker);
        var gateEnter = sticker.Animation != "none";
        var gateExit = exitPreset != "none";
        string Clip(string x) => "clip(" + x + "\\,0\\,1)";
        // geq 的时间变量是大写 T，overlay/enable 是小写 t —— 同一套进度要按场景替换。
        string Prog(string tv, string which)
        {
            string linEnter = w.JoinEnter is { } je
                ? Clip("(" + tv + "-" + F(je.Start) + ")/" + F(Math.Max(je.End - je.Start, 0.0001)))
                : enterDur > 0 ? Clip("(" + tv + "-" + F(start) + ")/" + F(enterDur)) : "1";
            string linExit = w.JoinExit is { } jx
                ? "1-" + Clip("(" + tv + "-" + F(jx.Start) + ")/" + F(Math.Max(jx.End - jx.Start, 0.0001)))
                : exitDur > 0 ? Clip("(" + F(end) + "-" + tv + ")/" + F(exitDur)) : "1";
            return which == "enter" ? CurveExpr(linEnter, sticker.AnimationCurve)
                : CurveExpr(linExit, sticker.AnimationCurve);
        }
        string Min(string a, string b) => "if(lt(" + a + "\\," + b + ")\\," + a + "\\," + b + ")";
        var alpha = "clip(" + Min(gateEnter ? Prog("T", "enter") : "1",
            gateExit ? Prog("T", "exit") : "1") + "\\,0\\,1)";
        var exitStart = w.JoinExit?.Start ?? (gateExit && exitDur > 0 ? end - exitDur : 1e9);
        var exiting = "gte(t\\," + F(exitStart) + ")";
        var enterPt = Prog("t", "enter");
        var exitPt = Prog("t", "exit");
        var halfExtent = Math.Max(sticker.Width, 0.02) / 2;
        var outsideLeading = Math.Min(-0.85, -halfExtent - 0.06);
        var outsideTrailing = Math.Max(1.85, 1 + halfExtent + 0.06);
        double Dx(string preset) => preset switch
        {
            "slideLeft" or "slideTopLeft" or "slideBottomLeft" => outsideLeading - sticker.X,
            "slideRight" or "slideTopRight" or "slideBottomRight" => outsideTrailing - sticker.X,
            _ => 0,
        };
        double Dy(string preset) => preset switch
        {
            "slideUp" or "slideTopLeft" or "slideTopRight" => outsideLeading - sticker.Y,
            "slideDown" or "slideBottomLeft" or "slideBottomRight" => outsideTrailing - sticker.Y,
            _ => 0,
        };
        string ScaleOf(string preset, string p) => preset == "pop" ? "0.72+0.28*(" + p + ")" : "1";
        string OffXOf(string preset, string p) => F(Dx(preset)) + "*(1-(" + p + "))";
        string OffYOf(string preset, string p) => F(Dy(preset)) + "*(1-(" + p + "))";
        var hasTransform = PresetMoves(sticker.Animation) || PresetMoves(exitPreset);
        var scale = !hasTransform ? "1" : "if(" + exiting + "\\," + ScaleOf(exitPreset, exitPt)
            + "\\," + ScaleOf(sticker.Animation, enterPt) + ")";
        var offX = !hasTransform ? "0" : "if(" + exiting + "\\," + OffXOf(exitPreset, exitPt)
            + "\\," + OffXOf(sticker.Animation, enterPt) + ")";
        var offY = !hasTransform ? "0" : "if(" + exiting + "\\," + OffYOf(exitPreset, exitPt)
            + "\\," + OffYOf(sticker.Animation, enterPt) + ")";
        var (ws, we) = track.VisibleWindow(sticker);
        var enable = "gte(t," + F(ws) + ")*lt(t," + F(we) + ")";
        return (alpha, scale, offX, offY, F(cx), F(cy), enable, hasTransform);
    }

    static bool PresetMoves(string? preset) =>
        preset is "pop" or "slideLeft" or "slideRight" or "slideUp" or "slideDown"
            or "slideTopLeft" or "slideTopRight" or "slideBottomLeft" or "slideBottomRight";

    static string CurveExpr(string linear, string? curve) => curve switch
    {
        "smooth" => "(" + linear + ")*(" + linear + ")*(" + linear + ")*((" + linear + ")*((" + linear + ")*6-15)+10)",
        "gentle" => "1-pow(1-(" + linear + ")\\,3)",
        "linear" => "(" + linear + ")",
        _ => "1-pow(1-(" + linear + ")\\,4)", // swift
    };

    private static string ApplyRegion(StringBuilder script, List<string> inputs, List<RegionMask> masks,
        string input, SoftRegion soft, int width, int height, int fps, double duration, double start, int step)
    {
        var mask = masks.FirstOrDefault(x => x.Region.Id == soft.Id && x.Width == width && x.Height == height);
        var index = mask?.Index ?? inputs.Count;
        if (mask is null) { inputs.Add(""); masks.Add(new(index, width, height, soft)); }
        var prefix = "region" + index + "_" + step;
        script.Append('[').Append(input).Append("]format=gbrp,split[").Append(prefix).Append("clear][").Append(prefix).AppendLine("fx0]");
        script.Append('[').Append(prefix).Append("fx0]");
        if (soft.Style == "spotlight")
        {
            var factor = F(1 - soft.Dimming);
            script.Append("lutrgb=r='val*").Append(factor).Append("':g='val*").Append(factor).Append("':b='val*").Append(factor).Append("'");
        }
        else script.Append("gblur=sigma=").Append(F(4 + soft.Strength * 24));
        script.Append('[').Append(prefix).AppendLine("fx]");
        script.Append('[').Append(index).Append(":v]loop=loop=-1:size=1:start=0,setpts=N/").Append(fps)
            .Append("/TB,trim=duration=").Append(F(duration)).Append(",format=gray,geq=lum='")
            .Append(soft.Style == "spotlight" ? "(255-p(X,Y))" : "p(X,Y)")
            .Append("*(").Append(EffectTiming.Expression(soft.Timing, "(T+" + F(start) + ")")).Append(")'[").Append(prefix).AppendLine("mask]");
        script.Append('[').Append(prefix).Append("fx]format=rgb24[").Append(prefix).AppendLine("fxa]");
        script.Append('[').Append(prefix).Append("fxa][").Append(prefix).Append("mask]alphamerge[")
            .Append(prefix).AppendLine("fxam]");
        script.Append('[').Append(prefix).Append("clear][").Append(prefix)
            .Append("fxam]overlay=0:0:format=auto[").Append(prefix).AppendLine("out]");
        return prefix + "out";
    }

    private static string Join(StringBuilder script, List<string> labels, string output, bool video)
    {
        if (labels.Count == 1)
        {
            return "[" + labels[0] + "]";
        }

        foreach (var label in labels)
        {
            script.Append('[').Append(label).Append(']');
        }

        script.Append("concat=n=").Append(labels.Count)
            .Append(video ? ":v=1:a=0" : ":v=0:a=1")
            .Append('[').Append(output).AppendLine("]");
        return "[" + output + "]";
    }

    private static string VideoChain(Piece slice, int width, int height, int fps, SourceCrop sourceCrop, ZoomMotion motion)
    {
        var chain = new StringBuilder();
        var crop = CanvasLayout.Crop(width, height, sourceCrop);
        chain.Append("crop=").Append(crop.Width).Append(':').Append(crop.Height).Append(':').Append(crop.X).Append(':').Append(crop.Y);
        width = crop.Width;
        height = crop.Height;
        if (motion.HasMotion(slice.TimelineStart, slice.TimelineDuration))
        {
            var (scale, focusX, focusY) = motion.Expressions(slice.TimelineStart, slice.TimelineDuration, "(" + F(slice.TimelineStart) + "+time)");
            // zoompan 的裁切原点取整数；小素材先放大可减少慢速运镜的一像素跳动。
            if (Math.Max(width, height) <= 1920)
                chain.Append(",scale=").Append(width * 2).Append(':').Append(height * 2).Append(":flags=bicubic");
            chain.Append(",zoompan=z='").Append(scale)
                .Append("':x='(iw-iw/zoom)*(").Append(focusX)
                .Append(")':y='(ih-ih/zoom)*(").Append(focusY)
                .Append(")':d=1:s=").Append(width).Append('x').Append(height).Append(":fps=").Append(fps);
        }

        chain.Append(",scale=").Append(width).Append(':').Append(height)
            .Append(":flags=lanczos,setsar=1,fps=").Append(fps).Append(",format=yuv420p");
        return chain.ToString();
    }

    private static string CameraChain(Piece slice, int width, int height, int fps, CameraBubble camera)
    {
        var chain = new StringBuilder();
        // 分段同步点把源窗口两端各自偏移，校正窗口映射回原输出时长
        // （等价 Mac sourceTimeScale = correctedDuration/duration）。
        var startOffset = slice.CameraStartOffset + camera.TimingAdvance;
        var endOffset = slice.CameraEndOffset + camera.TimingAdvance;
        var start = Math.Max(0, slice.SourceStart + startOffset);
        var end = Math.Max(0, slice.SourceEnd + endOffset);
        var sourceSpan = Math.Max(slice.SourceEnd - slice.SourceStart, 0.000001);
        var rate = slice.Speed * Math.Max(end - start, 0.000001) / sourceSpan;
        var delay = Math.Max(0, -slice.SourceStart - startOffset) / rate;
        chain.Append("trim=start=").Append(F(start))
            .Append(":end=").Append(F(end))
            .Append(",setpts=(PTS-STARTPTS)/").Append(F(rate))
            .Append(",scale=").Append(CanvasLayout.Even((int)Math.Ceiling(width * camera.ContentScale))).Append(':').Append(CanvasLayout.Even((int)Math.Ceiling(height * camera.ContentScale)))
            .Append(":force_original_aspect_ratio=increase,crop=")
            .Append(width).Append(':').Append(height)
            .Append(":x=(iw-ow)*").Append(F(camera.Mirrored ? 1 - camera.ContentX : camera.ContentX))
            .Append(":y=(ih-oh)*").Append(F(camera.ContentY))
            .Append(",setsar=1,fps=").Append(fps).Append(camera.Mirrored ? ",hflip" : "")
            .Append(",format=yuv420p,setpts=PTS+").Append(F(slice.TimelineStart + delay)).Append("/TB");
        return chain.ToString();
    }

    private static string AudioChain(Piece slice, double gain, double offset = 0)
    {
        var chain = new StringBuilder();
        chain.Append("aformat=channel_layouts=stereo,aresample=48000,atrim=start=")
            .Append(F(Math.Max(0, slice.SourceStart + offset))).Append(":end=").Append(F(Math.Max(0, slice.SourceEnd + offset)))
            .Append(",asetpts=PTS-STARTPTS");
        if (Math.Abs(slice.Speed - 1) > 0.001)
        {
            var remaining = slice.Speed;
            while (remaining > 2) { chain.Append(",atempo=2"); remaining /= 2; }
            chain.Append(",atempo=").Append(F(remaining));
        }

        if (Math.Abs(gain - 1) > 0.001)
        {
            chain.Append(",volume=").Append(F(gain));
        }

        var delay = Math.Max(0, -slice.SourceStart - offset) / slice.Speed;
        if (delay > 0) chain.Append(",adelay=").Append((int)Math.Round(delay * 1000)).Append(":all=1");
        chain.Append(",apad=pad_dur=").Append(F(slice.TimelineDuration)).Append(",atrim=0:").Append(F(slice.TimelineDuration));
        return chain.ToString();
    }

    private static List<Piece> BuildPieces(ProjectDocument project, IReadOnlyList<PlacedClip> placed, ResolvedMedia media)
    {
        var slices = new List<Piece>();
        foreach (var clip in placed)
        {
            if (!media.Videos.TryGetValue(clip.Asset.Id, out var video))
                throw new InvalidDataException("找不到片段对应的画面文件。");
            media.Microphones.TryGetValue(clip.Asset.Id, out var microphone);
            media.Systems.TryGetValue(clip.Asset.Id, out var system);
            media.Cameras.TryGetValue(clip.Asset.Id, out var camera);
            var anchors = clip.Asset.CameraSyncAnchors;
            var camStartOffset = clip.Asset.CameraOffset + SyncAnchorCurve.Offset(anchors, clip.Clip.SourceStart);
            var camEndOffset = clip.Asset.CameraOffset + SyncAnchorCurve.Offset(anchors, clip.Clip.SourceEnd);
            slices.Add(new Piece(video, microphone, system, camera, clip.Clip.SourceStart, clip.Clip.SourceEnd,
                clip.Clip.Speed, clip.Start, clip.Duration, camStartOffset, camEndOffset, clip.Asset.MicrophoneOffset));
        }

        if (slices.Count == 0)
        {
            throw new InvalidDataException("片段短得无法导出。");
        }

        return slices;
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>壁纸路径是不是视频（视频走 -stream_loop 循环输入，不再单帧 loop）。</summary>
    internal static bool IsVideoPath(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".mp4" or ".mov" or ".m4v" or ".webm" or ".mkv" or ".avi" or ".wmv";

    private sealed record Piece(
        string VideoPath,
        string? MicrophonePath,
        string? SystemPath,
        string? CameraPath,
        double SourceStart,
        double SourceEnd,
        double Speed,
        double TimelineStart,
        double TimelineDuration,
        double CameraStartOffset,
        double CameraEndOffset,
        double MicrophoneOffset);
}
