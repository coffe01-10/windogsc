using System.Globalization;
using System.Text;

namespace DogSC.Core;

public sealed class ExportOptions
{
    public bool AudioOnly { get; init; }
    public string Resolution { get; init; } = "source";
    public int Fps { get; init; } = 30;
}

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
}

public sealed class ExportPlan
{
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
    /// <summary>贴图输入（-1 = 不画）。</summary>
    public int StickerIndex { get; init; } = -1;
    /// <summary>局部柔化遮罩输入（-1 = 不柔化）。</summary>
    public int SoftMaskIndex { get; init; } = -1;
    /// <summary>贴图目标像素矩形（导出画布坐标）。</summary>
    public (int X, int Y, int W, int H) StickerRect { get; init; }
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
        var duration = placed[^1].End;
        var fps = options.Fps is 60 ? 60 : 30;
        var (canvasWidth, canvasHeight) = CanvasLayout.OutputSize(project.Width, project.Height, project.Canvas.Aspect, options.Resolution);
        var placement = CanvasLayout.Place(canvasWidth, canvasHeight, project.Width, project.Height, project.Canvas);
        var bubble = CanvasLayout.Bubble(canvasWidth, canvasHeight, project.Camera);
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
        var includeCamera = !options.AudioOnly && slices.Any(slice => slice.CameraPath is not null);
        var script = new StringBuilder();
        var videoLabels = new List<string>();
        var cameraSlices = new List<(int Index, string Label, double Start, double End)>();
        if (!options.AudioOnly)
        {
            for (var index = 0; index < slices.Count; index++)
            {
                var slice = slices[index];
                var label = "v" + index;
                script.Append('[').Append(videoIndex[slice.VideoPath]).Append(":v]")
                    .Append(VideoChain(slice, project.Width, project.Height, fps))
                    .Append('[').Append(label).AppendLine("]");
                videoLabels.Add(label);
                if (includeCamera && slice.CameraPath is not null)
                {
                    var cameraLabel = "c" + index;
                    script.Append('[').Append(cameraIndex[slice.CameraPath]).Append(":v]")
                        .Append(CameraChain(slice, bubble.Size, fps))
                        .Append('[').Append(cameraLabel).AppendLine("]");
                    cameraSlices.Add((index, cameraLabel, slice.TimelineStart, slice.TimelineStart + slice.TimelineDuration));
                }
            }
        }

        string? videoMap = null;
        var frameMaskIndex = -1;
        var cameraMaskIndex = -1;
        var focusMaskIndex = -1;
        var cursorIndex = -1;
        var rippleIndex = -1;
        var softMaskIndex = -1;
        var stickerIndex = -1;
        var wallpaperIndex = -1;
        var stickerRect = (0, 0, 0, 0);
        var softRect = (0, 0, 0, 0);
        if (!options.AudioOnly)
        {
            var concatenated = Join(script, videoLabels, "vcat", video: true);
            var needsMask = placement.Radius > 0 || placement.ShadowBlur > 0;
            if (needsMask)
            {
                frameMaskIndex = inputs.Count;
                inputs.Add("");
            }

            // 开场「zoom」预设：内容流轻微放大进场（zoompan 逐帧插值）。
            if (project.Canvas.Intro == "zoom")
            {
                var rampz = "min(time/" + F(CanvasLayout.IntroDuration) + ",1)";
                script.Append(concatenated)
                    .Append("zoompan=z='0.9+0.1*").Append(rampz)
                    .Append("':x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':d=1:s=")
                    .Append(project.Width).Append('x').Append(project.Height)
                    .Append(":fps=").Append(fps).AppendLine("[introzoom]");
                concatenated = "[introzoom]";
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
                    .Append(",setpts=PTS-STARTPTS,alphaextract").Append(split)
                    .AppendLine();
                script.Append("[scaled][framea]alphamerge[rounded]").AppendLine();
            }
            else
            {
                script.Append("[scaled]format=rgba[rounded]").AppendLine();
            }

            var under = "bg";
            if (!string.IsNullOrWhiteSpace(media.Wallpaper) && File.Exists(media.Wallpaper))
            {
                // 壁纸：cover 铺满画布，可选高斯模糊做氛围底。
                wallpaperIndex = inputs.Count;
                inputs.Add(media.Wallpaper);
                var blur = Math.Clamp(project.Canvas.WallpaperBlur, 0, 1);
                script.Append('[').Append(wallpaperIndex).Append(":v]scale=")
                    .Append(canvasWidth).Append(':').Append(canvasHeight)
                    .Append(":force_original_aspect_ratio=increase,crop=")
                    .Append(canvasWidth).Append(':').Append(canvasHeight)
                    .Append(",format=rgba,loop=loop=-1:size=1:start=0,trim=duration=")
                    .Append(F(Math.Max(duration, 0.1))).Append(",setpts=PTS-STARTPTS");
                if (blur > 0.03)
                {
                    script.Append(",gblur=sigma=").Append(F(6 + blur * 30));
                }

                script.AppendLine("[bg]");
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
            if (Math.Abs(project.Canvas.TiltX) > 0.5 || Math.Abs(project.Canvas.TiltY) > 0.5)
            {
                var pw = placement.Width;
                var ph = placement.Height;
                var sy = Math.Abs(project.Canvas.TiltY) * Math.PI / 180;
                var sx = Math.Abs(project.Canvas.TiltX) * Math.PI / 180;
                var hd = (int)Math.Round(pw * Math.Sin(sy) * 0.55);
                var vd = (int)Math.Round(ph * Math.Sin(sy) * 0.16);
                var vdX = (int)Math.Round(ph * Math.Sin(sx) * 0.55);
                var hdX = (int)Math.Round(pw * Math.Sin(sx) * 0.16);
                // 四角顺序：左上 右上 左下 右下。
                int x0 = 0, y0 = 0, x1 = pw, y1 = 0, x2 = 0, y2 = ph, x3 = pw, y3 = ph;
                if (project.Canvas.TiltY > 0)
                {
                    x1 = pw - hd; y1 = vd; x3 = pw - hd; y3 = ph - vd;
                }
                else if (project.Canvas.TiltY < 0)
                {
                    x0 = hd; y0 = vd; x2 = hd; y2 = ph - vd;
                }

                if (project.Canvas.TiltX > 0)
                {
                    x2 = Math.Max(x2, hdX); y2 = ph - vdX; x3 = Math.Min(x3, pw - hdX); y3 = ph - vdX;
                }
                else if (project.Canvas.TiltX < 0)
                {
                    x0 = Math.Max(x0, hdX); y0 = vdX; x1 = Math.Min(x1, pw - hdX); y1 = vdX;
                }

                script.Append('[').Append(tilted).Append("]perspective=")
                    .Append(x0).Append(':').Append(y0).Append(':')
                    .Append(x1).Append(':').Append(y1).Append(':')
                    .Append(x2).Append(':').Append(y2).Append(':')
                    .Append(x3).Append(':').Append(y3)
                    .AppendLine(":eval=frame[tilted]");
                tilted = "tilted";
            }

            // 开场淡入：fade alpha 让 rgba 内容帧从透明淡入，叠到背景上。
            if (project.Canvas.Intro is "fade" or "rise" or "zoom")
            {
                script.Append('[').Append(tilted).Append("]fade=t=in:st=0:d=")
                    .Append(F(CanvasLayout.IntroDuration)).AppendLine(":alpha=1[intro]");
                tilted = "intro";
            }

            // rise 预设：overlay 的 y 逐帧表达式做上移入场。
            // 表达式里的逗号要转义，否则会被滤镜参数分隔符吃掉。
            var overlayY = project.Canvas.Intro == "rise"
                ? placement.Y + "+(1-min(t/" + F(CanvasLayout.IntroDuration) + "\\,1))*" + F(placement.Height * 0.08)
                : placement.Y.ToString();
            script.Append('[').Append(under).Append("][").Append(tilted).Append("]overlay=")
                .Append(placement.X).Append(':').Append(overlayY)
                .AppendLine(":format=auto:shortest=1[composed]");
            videoMap = "composed";
            if (includeCamera && cameraSlices.Count > 0)
            {
                cameraMaskIndex = inputs.Count;
                inputs.Add("");
                // 圆形 mask 只循环一次，再 split 给每个有摄像头的片段。
                script.Append('[').Append(cameraMaskIndex).Append(":v]format=rgba,loop=loop=-1:size=1:start=0,alphaextract,split=")
                    .Append(cameraSlices.Count);
                foreach (var cam in cameraSlices)
                {
                    script.Append("[cama").Append(cam.Index).Append(']');
                }

                script.AppendLine();
                var cameraUnder = "composed";
                var step = 0;
                var entry = project.Camera.Entry;
                // 进出动画时长：跟开场一致，太长会吃掉短片段。
                var anim = Math.Min(0.5, Math.Max(0.12, cameraSlices.Min(c => c.End - c.Start) / 4));
                foreach (var cam in cameraSlices)
                {
                    var src = "camr" + cam.Index;
                    script.Append('[').Append(cam.Label).Append("][cama").Append(cam.Index)
                        .Append("]alphamerge[").Append(src).AppendLine("]");
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
                    // rise：在入场窗口内 y 从下方滑到位。
                    var oy = entry == "rise"
                        ? F(bubble.Y) + "+(1-min((t-" + F(cam.Start) + ")/" + F(anim) + "\\,1))*" + F(bubble.Size * 0.4)
                        : bubble.Y.ToString();
                    // 只在该片段的时间窗内叠加画中画，缺摄像头的片段保持背景画面。
                    script.Append('[').Append(cameraUnder).Append("][").Append(src)
                        .Append("]overlay=").Append(bubble.X).Append(':').Append(oy)
                        .Append(":format=auto:eval=frame:enable='gte(t,").Append(F(cam.Start))
                        .Append(")*lt(t,").Append(F(cam.End)).Append(")'")
                        .Append('[').Append(next).AppendLine("]");
                    cameraUnder = next;
                    step++;
                }

                videoMap = "vfinal";
            }

            // 局部柔化：矩形 mask 把选中区域混进 gblur 的模糊层。
            var soft = project.Canvas.SoftRegion;
            if (soft.Enabled && soft.Width > 0.02 && soft.Height > 0.02 && soft.Strength > 0.02)
            {
                softMaskIndex = inputs.Count;
                inputs.Add("");
                softRect = (
                    (int)Math.Round(soft.X * canvasWidth),
                    (int)Math.Round(soft.Y * canvasHeight),
                    Math.Max(2, (int)Math.Round(soft.Width * canvasWidth)),
                    Math.Max(2, (int)Math.Round(soft.Height * canvasHeight)));
                var sigma = F(4 + soft.Strength * 24);
                script.Append('[').Append(videoMap).AppendLine("]split[sclear][sblur0]");
                script.Append("[sblur0]gblur=sigma=").Append(sigma).AppendLine("[sblur]");
                script.Append('[').Append(softMaskIndex).Append(":v]scale=")
                    .Append(canvasWidth).Append('x').Append(canvasHeight)
                    .AppendLine(",format=gray[smask]");
                script.AppendLine("[sclear][sblur][smask]maskedmerge[softed]");
                videoMap = "softed";
            }

            // 移轴虚化：径向 mask 把边缘混进 gblur 的模糊层。
            if (project.Canvas.FocusBlur > 0.02)
            {
                focusMaskIndex = inputs.Count;
                inputs.Add("");
                var sigma = F(4 + project.Canvas.FocusBlur * 26);
                script.Append('[').Append(videoMap).AppendLine("]format=yuv420p,split[fclear][fblur0]");
                script.Append("[fblur0]gblur=sigma=").Append(sigma).AppendLine("[fblur]");
                script.Append('[').Append(focusMaskIndex).Append(":v]scale=")
                    .Append(canvasWidth).Append('x').Append(canvasHeight)
                    .AppendLine(",format=gray[fmask]");
                script.AppendLine("[fclear][fblur][fmask]maskedmerge[focused]");
                videoMap = "focused";
            }

            // 光标重绘：轨迹折线表达式驱动 overlay 位置，点击处叠加反馈圈。
            if (CursorTrack.OverlayExpression(project) is { } cursorExpr)
            {
                cursorIndex = inputs.Count;
                inputs.Add("");
                script.Append('[').Append(cursorIndex).Append(":v]loop=loop=-1:size=1:start=0")
                    .AppendLine(",setpts=PTS-STARTPTS[cursorimg]");
                var enable = CursorTrack.EnableExpression(project);
                script.Append('[').Append(videoMap).Append("][cursorimg]overlay=x='")
                    .Append(placement.X).Append("+(").Append(cursorExpr.X).Append(")*").Append(placement.Width)
                    .Append("-overlay_w/2':y='").Append(placement.Y).Append("+(").Append(cursorExpr.Y).Append(")*").Append(placement.Height)
                    .Append("-overlay_h/2':eval=frame");
                if (enable != "1")
                {
                    script.Append(":enable='").Append(enable).Append('\'');
                }

                script.AppendLine("[cursor1]");
                videoMap = "cursor1";
                if (CursorClickOverlay(project, placement, out var rippleX, out var rippleY, out var rippleEnable))
                {
                    rippleIndex = inputs.Count;
                    inputs.Add("");
                    script.Append('[').Append(rippleIndex).Append(":v]loop=loop=-1:size=1:start=0")
                        .AppendLine(",setpts=PTS-STARTPTS[rippleimg]");
                    script.Append('[').Append(videoMap).Append("][rippleimg]overlay=x='")
                        .Append(rippleX).Append("':y='").Append(rippleY)
                        .Append("':eval=frame:enable='").Append(rippleEnable).AppendLine("'[cursor2]");
                    videoMap = "cursor2";
                }
            }

            // 贴图：静帧图按比例缩放到目标宽，盖在画面最上层（光标之下亦可接受，
            // 但贴图一般是水印性质，放最后简单可预期）。
            if (media.Sticker is { Length: > 0 } stickerPath && File.Exists(stickerPath)
                && project.Canvas.Sticker is { } sticker)
            {
                stickerIndex = inputs.Count;
                inputs.Add(stickerPath);
                var w = Math.Max(4, (int)Math.Round(sticker.Width * canvasWidth)) & ~1;
                var h = Math.Max(4, (int)Math.Round(w / Math.Max(0.1, media.StickerAspect))) & ~1;
                var sx = Math.Clamp((int)Math.Round(sticker.X * canvasWidth - w / 2.0), 0, Math.Max(0, canvasWidth - w));
                var sy = Math.Clamp((int)Math.Round(sticker.Y * canvasHeight - h / 2.0), 0, Math.Max(0, canvasHeight - h));
                stickerRect = (sx, sy, w, h);
                script.Append('[').Append(stickerIndex).Append(":v]scale=").Append(w).Append(':').Append(h)
                    .Append(",format=rgba,loop=loop=-1:size=1:start=0,trim=duration=")
                    .Append(F(Math.Max(duration, 0.1))).Append(",setpts=PTS-STARTPTS");
                if (sticker.Opacity < 0.99)
                {
                    script.Append(",colorchannelmixer=aa=").Append(F(sticker.Opacity));
                }

                script.AppendLine("[stickerimg]");
                script.Append('[').Append(videoMap).Append("][stickerimg]overlay=")
                    .Append(sx).Append(':').Append(sy).AppendLine(":format=auto[stickered]");
                videoMap = "stickered";
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
                    .Append(AudioChain(slice, project.MicrophoneGain))
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
            CameraMaskIndex = cameraMaskIndex,
            FocusMaskIndex = focusMaskIndex,
            CursorIndex = cursorIndex,
            RippleIndex = rippleIndex,
            CursorOffsetX = placement.X,
            CursorOffsetY = placement.Y,
            WallpaperIndex = wallpaperIndex,
            StickerIndex = stickerIndex,
            SoftMaskIndex = softMaskIndex,
            StickerRect = stickerRect,
            SoftRect = softRect,
            CameraShape = project.Camera.Shape,
            CameraCorner = (int)Math.Round(bubble.Size * Math.Clamp(project.Camera.Corner, 0, 0.5)),
            Bubble = bubble,
        };
    }

    /// <summary>
    /// 点击反馈圈 overlay 表达式：每次点击一个 between 时段，位置随点击点。
    /// 没点击或关了开关返回 false。
    /// </summary>
    private static bool CursorClickOverlay(ProjectDocument project, FramePlacement placement,
        out string xExpr, out string yExpr, out string enableExpr)
    {
        xExpr = yExpr = enableExpr = "";
        if (!project.Canvas.Cursor.ClickRipple)
        {
            return false;
        }

        var clicks = new List<(double Time, double X, double Y)>();
        foreach (var clip in TimelineMath.Place(project))
        {
            foreach (var point in clip.Asset.Cursor)
            {
                if (point.Click && point.Time >= clip.Clip.SourceStart && point.Time <= clip.Clip.SourceEnd)
                {
                    clicks.Add((clip.Start + (point.Time - clip.Clip.SourceStart) / clip.Clip.Speed,
                        Math.Clamp(point.X, 0, 1), Math.Clamp(point.Y, 0, 1)));
                }
            }
        }

        if (clicks.Count == 0)
        {
            return false;
        }

        clicks = clicks.Take(24).ToList();
        // enable：任一点击窗口内为真；x/y：当前点击窗口内取对应点击位，
        // 嵌套 if 兜底最后一段的位置（enable=0 时不显示，取啥都行）。
        // 表达式内逗号一律 \, 转义。
        var enable = new StringBuilder();
        var xb = new StringBuilder();
        var yb = new StringBuilder();
        for (var i = 0; i < clicks.Count; i++)
        {
            var (t, cx, cy) = clicks[i];
            var window = "between(t\\," + F(t) + "\\," + F(t + CursorTrack.ClickDuration) + ")";
            if (i > 0)
            {
                enable.Append('+');
            }

            enable.Append(window);
            var px = F(placement.X + cx * placement.Width);
            var py = F(placement.Y + cy * placement.Height);
            xb.Append("if(").Append(window).Append("\\,").Append(px).Append("-overlay_w/2\\,");
            yb.Append("if(").Append(window).Append("\\,").Append(py).Append("-overlay_h/2\\,");
        }

        var lastX = F(placement.X + clicks[^1].X * placement.Width);
        var lastY = F(placement.Y + clicks[^1].Y * placement.Height);
        xb.Append(lastX).Append("-overlay_w/2");
        yb.Append(lastY).Append("-overlay_h/2");
        for (var i = 0; i < clicks.Count; i++)
        {
            xb.Append(')');
            yb.Append(')');
        }

        enableExpr = enable.ToString();
        xExpr = xb.ToString();
        yExpr = yb.ToString();
        return true;
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

    private static string VideoChain(Piece slice, int width, int height, int fps, bool zoom = true)
    {
        var chain = new StringBuilder();
        chain.Append("trim=start=").Append(F(slice.SourceStart))
            .Append(":end=").Append(F(slice.SourceEnd))
            .Append(",setpts=(PTS-STARTPTS)/").Append(F(slice.Speed));
        if (zoom && slice.Zoom is { Scale: > 1.01 } span)
        {
            // zoompan 的表达式逐帧求值：time 是片段本地秒，全局时间线时刻
            // = TimelineStart + time。ramp 与预览端 CanvasLayout.ZoomRamp 同公式。
            var ease = Math.Max(0.05, Math.Min(0.4, span.Duration / 3));
            var ramp = "clip((" + F(slice.TimelineStart) + "+time-" + F(span.Start) + ")/" + F(ease) + ",0,1)"
                + "*clip((" + F(span.Start + span.Duration) + "-" + F(slice.TimelineStart) + "-time)/" + F(ease) + ",0,1)";
            var scale = "1+" + F(span.Scale - 1) + "*(" + ramp + ")";
            var focusX = "0.5+" + F(span.FocusX - 0.5) + "*(" + ramp + ")";
            var focusY = "0.5+" + F(span.FocusY - 0.5) + "*(" + ramp + ")";
            chain.Append(",zoompan=z='").Append(scale)
                .Append("':x='(iw-iw/zoom)*(").Append(focusX)
                .Append(")':y='(ih-ih/zoom)*(").Append(focusY)
                .Append(")':d=1:s=").Append(width).Append('x').Append(height).Append(":fps=").Append(fps);
        }

        chain.Append(",scale=").Append(width).Append(':').Append(height)
            .Append(":flags=lanczos,setsar=1,fps=").Append(fps).Append(",format=yuv420p");
        return chain.ToString();
    }

    private static string CameraChain(Piece slice, int size, int fps)
    {
        var chain = new StringBuilder();
        chain.Append("trim=start=").Append(F(slice.SourceStart))
            .Append(":end=").Append(F(slice.SourceEnd))
            .Append(",setpts=(PTS-STARTPTS)/").Append(F(slice.Speed))
            .Append(",scale=").Append(size).Append(':').Append(size)
            .Append(":force_original_aspect_ratio=increase,crop=")
            .Append(size).Append(':').Append(size)
            .Append(",setsar=1,fps=").Append(fps).Append(",format=yuv420p");
        return chain.ToString();
    }

    private static string AudioChain(Piece slice, double gain)
    {
        var chain = new StringBuilder();
        chain.Append("aformat=channel_layouts=stereo,aresample=48000,atrim=start=")
            .Append(F(slice.SourceStart)).Append(":end=").Append(F(slice.SourceEnd))
            .Append(",asetpts=PTS-STARTPTS");
        if (Math.Abs(slice.Speed - 1) > 0.001)
        {
            chain.Append(",atempo=").Append(F(slice.Speed));
        }

        if (Math.Abs(gain - 1) > 0.001)
        {
            chain.Append(",volume=").Append(F(gain));
        }

        chain.Append(",apad=pad_dur=2,atrim=0:").Append(F(slice.TimelineDuration));
        return chain.ToString();
    }

    private static List<Piece> BuildPieces(ProjectDocument project, IReadOnlyList<PlacedClip> placed, ResolvedMedia media)
    {
        var zooms = project.Zooms.OrderBy(item => item.Start).ToList();
        var slices = new List<Piece>();
        foreach (var clip in placed)
        {
            if (!media.Videos.TryGetValue(clip.Asset.Id, out var video))
            {
                throw new InvalidDataException("找不到片段对应的画面文件。");
            }

            media.Microphones.TryGetValue(clip.Asset.Id, out var microphone);
            media.Systems.TryGetValue(clip.Asset.Id, out var system);
            media.Cameras.TryGetValue(clip.Asset.Id, out var camera);
            var cuts = new List<double> { clip.Start, clip.End };
            foreach (var zoom in zooms)
            {
                var start = Math.Max(zoom.Start, clip.Start);
                var end = Math.Min(zoom.Start + zoom.Duration, clip.End);
                if (end - start > 0.04)
                {
                    cuts.Add(start);
                    cuts.Add(end);
                }
            }

            cuts.Sort();
            for (var index = 0; index < cuts.Count - 1; index++)
            {
                var start = cuts[index];
                var end = cuts[index + 1];
                if (end - start < 0.04)
                {
                    continue;
                }

                if (index > 0 && Math.Abs(cuts[index] - cuts[index - 1]) < 0.0001)
                {
                    continue;
                }

                var midpoint = (start + end) / 2;
                var zoom = zooms.FirstOrDefault(item => midpoint >= item.Start && midpoint < item.Start + item.Duration);
                var sourceStart = clip.Clip.SourceStart + (start - clip.Start) * clip.Clip.Speed;
                var sourceEnd = clip.Clip.SourceStart + (end - clip.Start) * clip.Clip.Speed;
                slices.Add(new Piece(video, microphone, system, camera, sourceStart, sourceEnd, clip.Clip.Speed, start, end - start, zoom));
            }
        }

        if (slices.Count == 0)
        {
            throw new InvalidDataException("片段短得无法导出。");
        }

        return slices;
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

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
        ZoomSpan? Zoom);
}
