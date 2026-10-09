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
        // 背景：pattern/dynamicFlow/视频都映不动，只有纯色近似与图片可取。
        var bg = Obj(c, "backgroundSource");
        var kind = bg is null ? null : Text(bg.Value, "kind");
        if (kind is "projectImage" or "systemImage")
        {
            var rel = Text(bg!.Value, "relativePath") ?? Text(bg.Value, "absolutePath");
            if (rel is not null)
            {
                project.Canvas.Wallpaper = rel;
            }
        }
        else if (kind is not null)
        {
            dropped.Add("canvas.backgroundSource.kind=" + kind + "（Windows 只支持纯色或图片壁纸）");
        }

        // Windows 无对应概念的字段。
        foreach (var key in new[] { "borderWidth", "borderHex", "screenFrame", "screenFrameScale",
                     "screenFrameToolbarScale", "screenFrameTitle", "screenFrameBrowserAddress",
                     "patternScale", "patternOpacity", "backgroundBlur", "insetOpacity" })
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
            project.Zooms.Add(new ZoomSpan
            {
                Start = start,
                Duration = Math.Max(0.12, end - start),
                Scale = Num(clip, "scale", 1.45),
                FocusX = focus is { } f ? Num(f, "x", 0.5) : 0.5,
                FocusY = focus is { } f2 ? Num(f2, "y", 0.5) : 0.5,
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
        project.Canvas.Cursor.Mode = assetId switch
        {
            "system.hidden" => "hidden",
            "system.touch-dot" => "dot",
            _ => "pointer",
        };
        project.Canvas.Cursor.IdleHide = c.TryGetProperty("hideWhenIdle", out var idle) && idle.GetBoolean();
        project.Canvas.Cursor.Size = Num(c, "size", 1);
        project.Canvas.Cursor.IdleDelay = Num(c, "idleDelay", 1.6);
        var clickStyle = Text(c, "clickEffectStyle");
        project.Canvas.Cursor.ClickRipple = clickStyle is null or not "none";
        project.Canvas.Cursor.ClickColor = Text(c, "clickColor") ?? "#292929";
        project.Canvas.Cursor.ClickOpacity = Num(c, "clickOpacity", 0.8);
        project.Canvas.Cursor.ClickScale = Num(c, "clickScale", 1);
        foreach (var key in new[] { "motionTiltStrength", "motionSmoothing", "assetID" })
        {
            if (c.TryGetProperty(key, out _))
            {
                if (key != "assetID" || !assetId.StartsWith("system.")) dropped.Add("cursorStyle." + key);
            }
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
                });
                references.Add(path);
                var unsupported = new List<string>();
                if (Num(clip, "cornerRadius") > 0 || Num(clip, "borderWidth") > 0 || Num(clip, "shadowOpacity") > 0) unsupported.Add("圆角、边框或阴影");
                if (Num(clip, "backdropBlur") > 0) unsupported.Add("背景虚化");
                if (clip.TryGetProperty("hidesScreen", out var hideScreen) && hideScreen.ValueKind == JsonValueKind.True
                    || clip.TryGetProperty("hidesCamera", out var hideCamera) && hideCamera.ValueKind == JsonValueKind.True) unsupported.Add("隐藏画面/摄像头");
                if (Text(clip, "animation") is { } animation && animation is not "none" and not "fade"
                    || Text(clip, "exitAnimation") is { } exit && exit is not "none" and not "fade") unsupported.Add("位移/弹出动画（改为平滑淡入淡出）");
                if (Text(clip, "animation") == "fade" && Text(clip, "animationCurve") != "linear") unsupported.Add("动画曲线（采用 Windows 平滑过渡）");
                if (unsupported.Count > 0) dropped.Add("贴图 " + Path.GetFileName(path) + "：" + string.Join("、", unsupported));
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
        if (Obj(root, "timeline") is { } timeline)
            foreach (var key in new[] { "screenMotionClips", "cameraMotionClips" })
                if (timeline.TryGetProperty(key, out var clips) && clips.ValueKind == JsonValueKind.Array && clips.GetArrayLength() > 0)
                    dropped.Add("timeline." + key);
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
                Click = Text(entry, "kind") is "leftClick" or "rightClick" };
            if (point.Time < 0) { seed = point; continue; }
            if (points.Count == 0 && seed is not null && point.Time > 0)
                points.Add(new CursorPoint { Time = 0, X = seed.X, Y = seed.Y });
            points.Add(point);
        }
        if (points.Count == 0 && seed is not null) points.Add(new CursorPoint { X = seed.X, Y = seed.Y });
        return points.OrderBy(point => point.Time).ToList();
    }

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
