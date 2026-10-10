using System.Text.Json;

namespace DogSC.Core;

/// <summary>
/// 导入 macOS 版 .dogscproject 的 project.json，映射到 Windows 的
/// ProjectDocument。macOS 模型见 Sources/RecorderCore/ProjectModel.swift
/// （RecorderProject / ProjectTimeline / RecordingSegment / ZoomAnimationClip）。
/// 不认识的字段收集进 DroppedFields 清单，不中断导入。
/// </summary>
public static class MacProjectImporter
{
    /// <summary>导入结果：项目文档 + 被丢弃的字段说明。</summary>
    public sealed class Result
    {
        public required ProjectDocument Project { get; init; }
        /// <summary>macOS 端有但 Windows 端没对应实现、被丢弃的字段路径。</summary>
        public List<string> DroppedFields { get; } = [];
        /// <summary>media.screen.relativePath 等源引用（调用方用它去拷媒体文件）。</summary>
        public List<string> MediaReferences { get; } = [];
        public string? PointerPath { get; init; }
        public double PointerAdvance { get; init; }
        public bool FullRecording { get; init; }
    }

    /// <summary>从 macOS project.json 文本导入。dir 仅用于把相对路径解析成可读性提示，不做 IO。</summary>
    public static Result Import(string projectJson)
    {
        var dropped = new List<string>();
        using var document = JsonDocument.Parse(projectJson);
        var root = document.RootElement;

        var project = new ProjectDocument
        {
            Platform = ProjectSchema.Platform,
            Name = Text(root, "title") ?? "导入的 macOS 项目",
        };

        // —— 媒体清单：screen/camera/microphone 的相对路径 ——
        var media = Obj(root, "media");
        var screenPath = media is null ? null : Text(media.Value, "screen", "relativePath");
        var cameraPath = media is null ? null : Text(media.Value, "camera", "relativePath");
        var micPath = media is null ? null : Text(media.Value, "microphone", "relativePath");

        // —— 时间线片段：RecordingSegment.sourceStart/sourceDuration/playbackRate ——
        // Windows ClipSegment 是 sourceStart/sourceEnd（源秒）+ Speed（倍速=源时长/输出时长的倒数）。
        var clips = new List<ClipSegment>();
        var asset = new MediaAsset();
        double sourceDuration = 0;
        var timeline = Obj(root, "timeline");
        var sequence = timeline is null ? null : Obj(timeline.Value, "sourceSequence");
        var segments = sequence is null ? null : Arr(sequence.Value, "segments");
        if (segments is { } segs)
        {
            foreach (var seg in segs.EnumerateArray())
            {
                var start = Num(seg, "sourceStart");
                var dur = Num(seg, "sourceDuration");
                var rate = Num(seg, "playbackRate", 1);
                sourceDuration = Math.Max(sourceDuration, start + dur);
                clips.Add(new ClipSegment
                {
                    AssetId = asset.Id,
                    SourceStart = start,
                    SourceEnd = start + dur,
                    Speed = Math.Clamp(rate, 0.5, 100),
                });
            }
        }

        if (clips.Count == 0)
        {
            // fullRecording：整段无剪辑，长度未知；media 引用存下来让调用方探测。
            dropped.Add("timeline.sourceSequence=fullRecording（无剪辑边界，导入后按整段）");
            clips.Add(new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = sourceDuration > 0 ? sourceDuration : 1 });
        }

        asset.Video = screenPath ?? "";
        asset.Camera = cameraPath;
        asset.Microphone = micPath;
        asset.CameraOffset = media is { } cm && Obj(cm, "camera") is { } cr ? Num(cr, "sourceStartTime") - Num(cr, "startOffset") : 0;
        // 摄像头分段同步点：media.camera.syncAnchors[{sourceTime, offset}]。
        if (media is { } cmr && Obj(cmr, "camera") is { } cobj && Arr(cobj, "syncAnchors") is { } anchors)
        {
            foreach (var anchor in anchors.EnumerateArray())
            {
                asset.CameraSyncAnchors.Add(new SyncAnchor
                {
                    Id = Text(anchor, "id") ?? Guid.NewGuid().ToString("N"),
                    SourceTime = Num(anchor, "sourceTime"),
                    Offset = Num(anchor, "offset"),
                });
            }
        }
        asset.MicrophoneOffset = media is { } mm && Obj(mm, "microphone") is { } mr ? Num(mr, "sourceStartTime") - Num(mr, "startOffset") : 0;
        asset.Duration = sourceDuration > 0 ? sourceDuration : 1;
        project.Assets = [asset];
        project.Clips = clips;
        if (screenPath is not null)
        {
            var pointer = media is { } manifest ? Obj(manifest, "pointerEvents") : null;
            var r = new Result { Project = project,
                FullRecording = sequence is null || Text(sequence.Value, "kind") != "edited",
                PointerPath = pointer is { } pt ? Text(pt, "relativePath") : null,
                PointerAdvance = pointer is { } pr ? Num(pr, "sourceStartTime") - Num(pr, "startOffset") : 0 };
            CollectRefs(media, r.MediaReferences, dropped);
            r.DroppedFields.AddRange(dropped);
            MapCanvas(root, project, r.DroppedFields);
            if (project.Canvas.Wallpaper is { } wallpaper) r.MediaReferences.Add(wallpaper);
            MapCamera(root, project, r.DroppedFields);
            MapAudio(root, project);
            MapZooms(timeline, project);
            MapMotionClips(timeline, project);
            MapOverlays(timeline, project, r.MediaReferences, r.DroppedFields);
            MapCursor(root, project, r.DroppedFields);
            CollectUnmappedEffects(root, r.DroppedFields);
            if (Arr(root, "recordingMarkers") is { } markers)
                foreach (var marker in markers.EnumerateArray())
                    project.Markers.Add(new SourceMarker { AssetId = asset.Id, SourceTime = Num(marker, "sourceTime"), Note = "标记 " + Num(marker, "number", 1) });
            if (Obj(root, "exportSettings") is { } export)
            {
                project.ExportResolution = Text(export, "resolution") ?? "source";
                project.ExportFps = (int)Num(export, "frameRate", 30);
            }
            project.SourceLabel = "macOS 项目导入";
            ProjectFile.Normalize(project);
            return r;
        }

        // 没有 screen 就没法当录制项目打开。
        throw new InvalidDataException("macOS 项目里没有屏幕录制文件（media.screen.relativePath 缺失）。");
    }

    static void CollectRefs(JsonElement? media, List<string> refs, List<string> dropped)
    {
        if (media is null)
        {
            return;
        }

        // 只收 screen/camera/microphone：pointerEvents 是 mac 的光标轨迹
        // （events/pointer.jsonl），Windows 端不消费，拷进 media/ 只会变成垃圾文件。
        foreach (var key in new[] { "screen", "camera", "microphone" })
        {
            var path = Text(media.Value, key, "relativePath");
            if (path is not null)
            {
                refs.Add(path);
            }
        }

        if (Text(media.Value, "pointerEvents", "relativePath") is not null)
        {
            // Pointer JSONL is read by the caller into the Windows cursor track.
        }
    }

    static void MapCanvas(JsonElement root, ProjectDocument project, List<string> dropped)
    {
        var canvas = Obj(root, "canvas");
        if (canvas is null)
        {
            return;
        }

        var c = canvas.Value;
        // 宽高比：macOS adaptive=适应→source，其余枚举值与 Windows 对齐。
        var aspect = Text(c, "aspectRatio");
        project.Canvas.Aspect = aspect switch
        {
            "适应" or "adaptive" => "source",
            "16:9" => "16:9",
            "4:3" => "4:3",
            "1:1" => "1:1",
            "9:16" => "9:16",
            "3:4" => "3:4",
            "2.39:1" => "2.39:1",
            "1:2.39" => "1:2.39",
            _ => "source",
        };
        if (aspect is "自定义" or "custom" && Obj(c, "customAspectRatio") is { } custom)
            project.Canvas.Aspect = Num(custom, "width", 16).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Num(custom, "height", 9).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Obj(c, "crop") is { } crop)
            project.Canvas.Crop = new SourceCrop { X = Num(crop, "x"), Y = Num(crop, "y"), Width = Num(crop, "width", 1), Height = Num(crop, "height", 1) };
        project.Canvas.Padding = Num(c, "padding", 100) / 1080.0 * 100; // 参考宽 1080 → 归一到 Windows 的 0..240 直觉值
        project.Canvas.Scale = Num(c, "contentScale", 1);
        var pos = Obj(c, "contentPosition");
        if (pos is { } p)
        {
            project.Canvas.CenterX = Num(p, "x", 0.5);
            project.Canvas.CenterY = Num(p, "y", 0.5);
        }

        project.Canvas.Corner = Num(c, "cornerRadius", 24);
        project.Canvas.Shadow = Num(c, "shadowStrength", 0.28) * 100;
        // 背景：图片和视频走壁纸通道；pattern/dynamicFlow 透传给程序生成背景管线。
        var bg = Obj(c, "backgroundSource");
        var kind = bg is null ? null : Text(bg.Value, "kind");
        if (kind is "projectImage" or "systemImage" or "projectVideo" or "systemVideo")
        {
            var rel = Text(bg!.Value, "relativePath") ?? Text(bg.Value, "absolutePath");
            if (rel is not null)
            {
                project.Canvas.Wallpaper = rel;
            }
        }
        else if (kind is "pattern" or "dynamicFlow")
        {
            var preset = Text(bg!.Value, kind == "pattern" ? "patternPreset" : "dynamicFlowPreset");
            var id = preset is null ? null : PatternPresetId(preset, kind == "pattern");
            if (id is not null)
            {
                project.Canvas.Wallpaper = (kind == "pattern" ? "pattern:" : "flow:") + id;
            }
            else if (preset is not null)
            {
                dropped.Add("canvas.backgroundSource." + (kind == "pattern" ? "patternPreset" : "dynamicFlowPreset") + "=" + preset);
            }
        }
        else if (kind is not null)
        {
            dropped.Add("canvas.backgroundSource.kind=" + kind + "（Windows 只支持纯色 / 图片 / 视频 / 程序生成壁纸）");
        }

        // Mac 顶层的 patternScale/patternOpacity 与程序生成背景配套。
        project.Canvas.PatternScale = Num(c, "patternScale", 1);
        project.Canvas.PatternOpacity = Num(c, "patternOpacity", 1);

        // 屏幕外框：风格名直接透传（ChromeGeometry 识别 Mac 原名），标题 / 地址 / 缩放同取。
        if (Text(c, "screenFrame") is { } frame)
        {
            project.Canvas.ScreenFrame = frame;
        }
        project.Canvas.ScreenFrameScale = Num(c, "screenFrameScale", 1);
        project.Canvas.ScreenFrameToolbarScale = Num(c, "screenFrameToolbarScale", 1);
        project.Canvas.ScreenFrameTitle = Text(c, "screenFrameTitle") ?? "";
        project.Canvas.ScreenFrameAddress = Text(c, "screenFrameBrowserAddress") ?? "";

        // Windows 无对应概念的字段。
        foreach (var key in new[] { "borderWidth", "borderHex",
                     "backgroundBlur", "insetOpacity" })
        {
            if (c.TryGetProperty(key, out _))
            {
                dropped.Add("canvas." + key);
            }
        }
    }

    static void MapCamera(JsonElement root, ProjectDocument project, List<string> dropped)
    {
        var camera = Obj(root, "camera");
        if (camera is null)
        {
            return;
        }

        var c = camera.Value;
        var pos = Obj(c, "position");
        if (pos is { } p)
        {
            project.Camera.CenterX = Num(p, "x", 0.84);
            project.Camera.CenterY = Num(p, "y", 0.78);
        }

        project.Camera.Size = Num(c, "size", 0.29);
        // Preserve the macOS mask ratio and independent content framing.
        var shape = Text(c, "shape");
        project.Camera.Shape = shape switch { "圆形" or "circle" => "circle", "横向" or "horizontal" or "圆角矩形" => "horizontal",
            "纵向" or "vertical" => "vertical", "原始比例" or "original" => "original", "方形" or "square" or "连续圆角" => "square", _ => "none" };
        project.Camera.ContentScale = Num(c, "contentScale", 1);
        if (Obj(c, "contentPosition") is { } content)
        { project.Camera.ContentX = Num(content, "x", .5); project.Camera.ContentY = Num(content, "y", .5); }
        project.Camera.Hidden = c.TryGetProperty("isHidden", out var h) && h.ValueKind == JsonValueKind.True;
        project.Camera.Mirrored = c.TryGetProperty("isMirrored", out var mirrored) && mirrored.ValueKind == JsonValueKind.True;
        project.Camera.Corner = .5 * Num(c, "roundness", .36);

        foreach (var key in new[] { "borderWidth", "scaleDuringZoom",
                     "shadowStrength" })
        {
            if (c.TryGetProperty(key, out _))
            {
                dropped.Add("camera." + key);
            }
        }
    }

    static void MapAudio(JsonElement root, ProjectDocument project)
    {
        var audio = Obj(root, "audio");
        if (audio is null)
        {
            return;
        }

        var a = audio.Value;
        var sysMuted = a.TryGetProperty("isSystemMuted", out var sm) && sm.GetBoolean();
        var micMuted = a.TryGetProperty("isMicrophoneMuted", out var mm) && mm.GetBoolean();
        project.SystemGain = sysMuted ? 0 : Num(a, "systemVolume", 0.82);
        project.MicrophoneGain = micMuted ? 0 : Num(a, "microphoneVolume", 0.9);
    }

    static void MapZooms(JsonElement? timeline, ProjectDocument project)
    {
        var zooms = timeline is null ? null : Arr(timeline.Value, "zoomClips");
        if (zooms is null)
        {
            return;
        }

        foreach (var clip in zooms.Value.EnumerateArray())
        {
            var start = Num(clip, "startTime");
            var end = Num(clip, "endTime");
            var focus = Obj(clip, "focus");
            var span = new ZoomSpan
            {
                Start = start,
                Duration = Math.Max(0.12, end - start),
                Scale = Num(clip, "scale", 1.45),
                FocusX = focus is { } f ? Num(f, "x", 0.5) : 0.5,
                FocusY = focus is { } f2 ? Num(f2, "y", 0.5) : 0.5,
            };
            span.Easing = Text(clip, "easing") switch
            {
                "quintic" or "五次平滑" => "smooth",
                "线性" => "linear",
                "二次方缓动" => "quadratic",
                "三次方缓动" => "cubic",
                "弹簧" => "spring",
                "自定义曲线" => "custom",
                "quadratic" or "cubic" or "custom" or "spring" or "linear" => Text(clip, "easing")!,
                _ => span.Easing,
            };
            if (Obj(clip, "customCurve") is { } curve)
            {
                span.CurveX1 = Num(curve, "x1", 0.65);
                span.CurveY1 = Num(curve, "y1", 0);
                span.CurveX2 = Num(curve, "x2", 0.35);
                span.CurveY2 = Num(curve, "y2", 1);
            }
            if (clip.TryGetProperty("enterDuration", out var enter)) span.EnterDuration = Num(clip, "enterDuration");
            if (clip.TryGetProperty("exitDuration", out var exit)) span.ExitDuration = Num(clip, "exitDuration");
            project.Zooms.Add(span);
        }
    }

    static TransitionTiming MapTiming(JsonElement clip)
    {
        var t = Obj(clip, "timing");
        if (t is null) return new TransitionTiming();
        var v = t.Value;
        var easing = Text(v, "easing") switch
        {
            "线性" or "linear" => "linear",
            "二次方缓动" or "quadratic" => "quadratic",
            "三次方缓动" or "cubic" => "cubic",
            "五次平滑" or "quintic" => "quintic",
            "弹簧" or "spring" => "spring",
            "自定义曲线" or "custom" => "custom",
            _ => "quintic",
        };
        double[]? custom = null;
        static double NumOf(JsonElement e) =>
            e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var n) ? n : 0;
        if (Obj(v, "customCurve") is { } curve && Arr(curve, "points") is { } pts)
            custom = pts.EnumerateArray().Select(NumOf).Take(4).ToArray();
        else if (Arr(v, "customCurve") is { } flat)
            custom = flat.EnumerateArray().Select(NumOf).Take(4).ToArray();
        return new TransitionTiming
        {
            StartTime = Num(v, "startTime"),
            Duration = Num(v, "duration", .7),
            LeadInDuration = Num(v, "leadInDuration"),
            LeadInProgressOffset = Num(v, "leadInProgressOffset"),
            Easing = easing,
            CustomCurve = custom is { Length: 4 } ? custom : null,
            ReturnDuration = Num(v, "returnDuration"),
            ReturnProgressOffset = Num(v, "returnProgressOffset"),
            PreferredLeadInDuration = v.TryGetProperty("preferredLeadInDuration", out var pl) && pl.ValueKind == JsonValueKind.Number ? pl.GetDouble() : null,
            PreferredReturnDuration = v.TryGetProperty("preferredReturnDuration", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetDouble() : null,
        };
    }

    /// <summary>屏幕/摄像头独立运动轨道：timeline.screenMotionClips / cameraMotionClips。</summary>
    static void MapMotionClips(JsonElement? timeline, ProjectDocument project)
    {
        if (timeline is not { } track) return;
        if (Arr(track, "screenMotionClips") is { } screens)
            foreach (var clip in screens.EnumerateArray())
            {
                var target = Obj(clip, "target");
                var pos = target is { } t2 ? Obj(t2, "position") : null;
                project.ScreenMotions.Add(new ScreenMotionClip
                {
                    Id = Text(clip, "id") ?? Guid.NewGuid().ToString("N"),
                    Timing = MapTiming(clip),
                    Target = new ScreenMotionState
                    {
                        X = pos is { } px ? Num(px, "x", .5) : .5,
                        Y = pos is { } py ? Num(py, "y", .5) : .5,
                        Scale = target is { } ts ? Num(ts, "scale", 1) : 1,
                        RotationX = target is { } tx ? Num(tx, "rotationX") : 0,
                        RotationY = target is { } ty ? Num(ty, "rotationY") : 0,
                        RotationZ = target is { } tz ? Num(tz, "rotationZ") : 0,
                        Perspective = target is { } tp ? Num(tp, "perspective", .65) : .65,
                    },
                });
            }
        if (Arr(track, "cameraMotionClips") is { } cameras)
            foreach (var clip in cameras.EnumerateArray())
            {
                var target = Obj(clip, "target");
                var pos = target is { } t2 ? Obj(t2, "position") : null;
                var layout = target is { } lt && Obj(lt, "layout") is { } lo
                    ? (Text(lo, "kind") == "fullscreen" ? "fullscreen" : Text(lo, "shape") ?? "square")
                    : "square";
                project.CameraMotions.Add(new CameraMotionClip
                {
                    Id = Text(clip, "id") ?? Guid.NewGuid().ToString("N"),
                    Timing = MapTiming(clip),
                    Target = new CameraMotionState
                    {
                        Layout = layout,
                        X = pos is { } px ? Num(px, "x", .86) : .86,
                        Y = pos is { } py ? Num(py, "y", .82) : .82,
                        Size = target is { } ts ? Num(ts, "size", .35) : .35,
                        Roundness = target is { } tr ? Num(tr, "roundness", .5) : .5,
                        Opacity = target is { } to ? Num(to, "opacity", 1) : 1,
                    },
                });
            }
    }

    static void MapCursor(JsonElement root, ProjectDocument project, List<string> dropped)
    {
        var cursor = Obj(root, "cursorStyle");
        if (cursor is null)
        {
            return;
        }

        var c = cursor.Value;
        var assetId = Text(c, "assetID") ?? "";
        if (CursorCatalog.Find(assetId) is { } known)
        {
            project.Canvas.Cursor.Asset = known.Id;
        }
        project.Canvas.Cursor.Mode = (project.Canvas.Cursor.Asset ?? assetId) switch
        {
            "system.hidden" => "hidden",
            "system.touch-dot" => "dot",
            _ => "pointer",
        };
        project.Canvas.Cursor.IdleHide = c.TryGetProperty("hideWhenIdle", out var idle) && idle.GetBoolean();
        project.Canvas.Cursor.Size = Num(c, "size", 1);
        project.Canvas.Cursor.IdleDelay = Num(c, "idleDelay", 1.6);
        var clickStyle = Text(c, "clickEffectStyle");
        if (clickStyle is not null && CursorCatalog.ClickStyles.Any(s => s.Id == clickStyle))
        {
            project.Canvas.Cursor.ClickStyle = clickStyle;
        }
        project.Canvas.Cursor.ClickRipple = clickStyle is null or not "none";
        project.Canvas.Cursor.Motion = Text(c, "motionSmoothing") switch
        {
            "平滑" or "smooth" => "smooth",
            "适中" or "medium" => "medium",
            "快速" or "rapid" => "rapid",
            "无" or "none" => "none",
            _ => project.Canvas.Cursor.Motion,
        };
        project.Canvas.Cursor.MotionTilt = Num(c, "motionTiltStrength", 2);
        project.Canvas.Cursor.ClickColor = Text(c, "clickColor") ?? "#292929";
        project.Canvas.Cursor.ClickOpacity = Num(c, "clickOpacity", 0.8);
        project.Canvas.Cursor.ClickScale = Num(c, "clickScale", 1);
        if (assetId.Length > 0 && !assetId.StartsWith("system."))
        {
            dropped.Add("cursorStyle.assetID");
        }
    }

    static void MapOverlays(JsonElement? timeline, ProjectDocument project, List<string> references, List<string> dropped)
    {
        if (timeline is not { } track) return;
        EffectWindow Timing(JsonElement clip, bool region)
        {
            var time = Obj(clip, "timing");
            var enterAnimation = Text(clip, "animation") ?? "pop";
            var exitAnimation = Text(clip, "exitAnimation") ?? enterAnimation;
            return new()
            {
                Start = time is { } t ? Num(t, "startTime") : 0,
                Duration = time is { } d ? Num(d, "duration", 3) : 3,
                Transition = region ? Text(clip, "transitionStyle") ?? "none" : enterAnimation == "none" && exitAnimation == "none" ? "none" : Text(clip, "animationCurve") == "linear" ? "linear" : "smooth",
                FadeIn = !region && enterAnimation == "none" ? 0 : Num(clip, region ? "transitionInDuration" : "enterDuration", region ? .3 : .7),
                FadeOut = !region && exitAnimation == "none" ? 0 : Num(clip, region ? "transitionOutDuration" : "exitDuration", region ? .3 : .22),
            };
        }
        if (Arr(track, "mosaicClips") is { } mosaics)
            foreach (var clip in mosaics.EnumerateArray())
            {
                var rect = Obj(clip, "sourceRect");
                project.Canvas.SoftRegions.Add(new()
                {
                    Id = Text(clip, "id") ?? Guid.NewGuid().ToString("N"), Enabled = true, CoordinateSpace = "source",
                    Style = Text(clip, "style") ?? "blur", Timing = Timing(clip, true),
                    X = rect is { } x ? Num(x, "x", .25) : .25, Y = rect is { } y ? Num(y, "y", .25) : .25,
                    Width = rect is { } w ? Num(w, "width", .5) : .5, Height = rect is { } h ? Num(h, "height", .5) : .5,
                    Corner = Num(clip, "cornerRadius", .08), Strength = Num(clip, "intensity", .55), Dimming = Num(clip, "spotlightDimming", .22),
                });
            }
        if (Arr(track, "stickerClips") is { } stickers)
            foreach (var clip in stickers.EnumerateArray().OrderBy(x => Num(x, "layerIndex")))
            {
                var path = Text(clip, "relativePath");
                if (string.IsNullOrWhiteSpace(path)) continue;
                var position = Obj(clip, "position");
                project.Canvas.Stickers.Add(new()
                {
                    Id = Text(clip, "id") ?? Guid.NewGuid().ToString("N"), Path = path, Name = Path.GetFileName(path), Timing = Timing(clip, false),
                    X = position is { } x ? Num(x, "x", .5) : .5, Y = position is { } y ? Num(y, "y", .5) : .5,
                    Width = Num(clip, "width", .38), Opacity = Num(clip, "opacity", 1), Rotation = Num(clip, "rotationDegrees"),
                    CornerRadius = Num(clip, "cornerRadius", 18), BorderWidth = Num(clip, "borderWidth"),
                    BorderColor = Text(clip, "borderColor") ?? "#FFFFFF",
                    ShadowOpacity = Num(clip, "shadowOpacity", .28), ShadowRadius = Num(clip, "shadowRadius", 24),
                    ShadowOffsetX = Num(clip, "shadowOffsetX"), ShadowOffsetY = Num(clip, "shadowOffsetY", 10),
                    Animation = Text(clip, "animation") ?? "pop",
                    AnimationCurve = Text(clip, "animationCurve") ?? "swift",
                    ExitAnimation = Text(clip, "exitAnimation"),
                    EnterDuration = Num(clip, "enterDuration", .7), ExitDuration = Num(clip, "exitDuration", .22),
                    BackdropBlur = Num(clip, "backdropBlur", 20),
                    BackdropBlurIncludesCamera = clip.TryGetProperty("backdropBlurIncludesCamera", out var bic) && bic.ValueKind == JsonValueKind.True,
                    HidesScreen = clip.TryGetProperty("hidesScreen", out var hs) && hs.ValueKind == JsonValueKind.True,
                    HidesCamera = clip.TryGetProperty("hidesCamera", out var hc) && hc.ValueKind == JsonValueKind.True,
                });
                references.Add(path);
            }
    }

    static void CollectUnmappedEffects(JsonElement root, List<string> dropped)
    {
        foreach (var key in new[] { "openingSequence", "motion" })
        {
            if (root.TryGetProperty(key, out _))
            {
                dropped.Add(key);
            }
        }

    }

    public static List<CursorPoint> ImportPointerEvents(IEnumerable<string> lines, double advance)
    {
        var points = new List<CursorPoint>();
        CursorPoint? seed = null;
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            using var json = JsonDocument.Parse(line);
            var entry = json.RootElement;
            if (Obj(entry, "location") is not { } location) continue;
            var point = new CursorPoint { Time = Num(entry, "time") - advance,
                X = Num(location, "x"), Y = Num(location, "y"),
                Click = Text(entry, "kind") is "leftClick" or "rightClick",
                Cursor = Text(entry, "cursorAssetID") is { } id && id.StartsWith("system.") ? id : null };
            if (point.Time < 0) { seed = point; continue; }
            if (points.Count == 0 && seed is not null && point.Time > 0)
                points.Add(new CursorPoint { Time = 0, X = seed.X, Y = seed.Y, Cursor = seed.Cursor });
            points.Add(point);
        }
        if (points.Count == 0 && seed is not null) points.Add(new CursorPoint { X = seed.X, Y = seed.Y });
        return points.OrderBy(point => point.Time).ToList();
    }

    /// <summary>Mac 持久化的预设 rawValue 是中文名；映射回枚举 case id 供 PatternLibrary 使用。</summary>
    static string? PatternPresetId(string rawValue, bool pattern) => (pattern, rawValue) switch
    {
        (true, "黑曜石网格") => "obsidianGrid",
        (true, "工程白网格") => "engineeringWhiteGrid",
        (true, "暗夜星阵") => "midnightDots",
        (true, "建筑极简点") => "architecturalDots",
        (true, "立体等角网") => "isometricMesh",
        (false, "赛博微流网格") or (false, "旋转十字星阵") => "cyberDriftGrid",
        (false, "星辉漫步点阵") => "starfieldDots",
        (false, "极光流体星云") => "auroraFluid",
        _ => null,
    };

    static JsonElement? Obj(JsonElement e, params string[] path)
    {
        var current = e;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
            {
                return null;
            }
        }

        return current;
    }

    static JsonElement? Arr(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v : null;

    static string? Text(JsonElement e, params string[] path)
    {
        var node = Obj(e, path);
        return node?.ValueKind == JsonValueKind.String ? node.Value.GetString() : null;
    }

    static double Num(JsonElement e, string key, double fallback = 0)
    {
        if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n))
        {
            return n;
        }

        return fallback;
    }
}
