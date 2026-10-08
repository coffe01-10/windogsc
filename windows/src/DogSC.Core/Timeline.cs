namespace DogSC.Core;

public sealed record PlacedClip(ClipSegment Clip, MediaAsset Asset, double Start, double Duration)
{
    public double End => Start + Duration;
}

public readonly record struct SourceHit(string AssetId, double SourceTime, PlacedClip Clip);

public static class TimelineMath
{
    public static double ClipDuration(ClipSegment clip) =>
        Math.Max(0, (clip.SourceEnd - clip.SourceStart) / Math.Max(clip.Speed, 0.05));

    public static IReadOnlyList<PlacedClip> Place(ProjectDocument project)
    {
        var placed = new List<PlacedClip>(project.Clips.Count);
        double time = 0;
        foreach (var clip in project.Clips)
        {
            var asset = project.Asset(clip.AssetId);
            if (asset is null)
            {
                continue;
            }

            var duration = ClipDuration(clip);
            placed.Add(new PlacedClip(clip, asset, time, duration));
            time += duration;
        }

        return placed;
    }

    public static double Duration(ProjectDocument project) =>
        Place(project).LastOrDefault()?.End ?? 0;

    public static SourceHit? Hit(ProjectDocument project, double timelineTime)
    {
        foreach (var clip in Place(project))
        {
            if (timelineTime < clip.Start - 0.0001)
            {
                return null;
            }

            if (timelineTime <= clip.End + 0.0001)
            {
                var local = Math.Clamp(timelineTime - clip.Start, 0, clip.Duration);
                return new SourceHit(clip.Asset.Id, clip.Clip.SourceStart + local * clip.Clip.Speed, clip);
            }
        }

        return null;
    }

    public static double? MarkerTime(ProjectDocument project, SourceMarker marker)
    {
        foreach (var clip in Place(project))
        {
            if (clip.Asset.Id != marker.AssetId)
            {
                continue;
            }

            if (marker.SourceTime < clip.Clip.SourceStart - 0.0001 || marker.SourceTime > clip.Clip.SourceEnd + 0.0001)
            {
                continue;
            }

            return clip.Start + (marker.SourceTime - clip.Clip.SourceStart) / clip.Clip.Speed;
        }

        return null;
    }

    public static void RemoveRange(IList<ZoomSpan> zooms, double start, double end)
    {
        var span = end - start;
        if (span <= 0.0001)
        {
            return;
        }

        for (var index = zooms.Count - 1; index >= 0; index--)
        {
            var zoom = zooms[index];
            var zoomEnd = zoom.Start + zoom.Duration;
            if (zoomEnd <= start + 0.0001)
            {
                continue;
            }

            if (zoom.Start >= end - 0.0001)
            {
                zoom.Start -= span;
                continue;
            }

            if (zoom.Start >= start - 0.0001 && zoomEnd <= end + 0.0001)
            {
                zooms.RemoveAt(index);
                continue;
            }

            if (zoom.Start < start && zoomEnd > end)
            {
                zoom.Duration -= span;
            }
            else if (zoom.Start < start)
            {
                zoom.Duration = start - zoom.Start;
            }
            else
            {
                zoom.Duration = zoomEnd - end;
                zoom.Start = start;
            }

            if (zoom.Duration < 0.12)
            {
                zooms.RemoveAt(index);
            }
        }
    }

    public static void InsertGap(IList<ZoomSpan> zooms, double time, double gap)
    {
        if (gap <= 0)
        {
            return;
        }

        foreach (var zoom in zooms)
        {
            if (zoom.Start >= time - 0.0001)
            {
                zoom.Start += gap;
            }
        }
    }

    /// <summary>
    /// 拖动吸附：time 距某个候选点不超过 threshold 时返回最近的候选点，
    /// 否则原样返回。
    /// </summary>
    public static double Snap(double time, IEnumerable<double> targets, double threshold)
    {
        var best = time;
        var gap = threshold;
        foreach (var target in targets)
        {
            var distance = Math.Abs(target - time);
            if (distance <= gap)
            {
                gap = distance;
                best = target;
            }
        }

        return best;
    }

    public static void Retime(IList<ZoomSpan> zooms, double clipStart, double oldDuration, double newDuration)
    {
        if (oldDuration <= 0.0001)
        {
            return;
        }

        var oldEnd = clipStart + oldDuration;
        var ratio = newDuration / oldDuration;
        for (var index = zooms.Count - 1; index >= 0; index--)
        {
            var zoom = zooms[index];
            var zoomEnd = zoom.Start + zoom.Duration;
            if (zoom.Start >= oldEnd - 0.0001)
            {
                zoom.Start += newDuration - oldDuration;
                continue;
            }

            if (zoomEnd <= clipStart + 0.0001)
            {
                continue;
            }

            var overlapStart = Math.Max(zoom.Start, clipStart);
            var overlapEnd = Math.Min(zoomEnd, oldEnd);
            var mappedStart = clipStart + (overlapStart - clipStart) * ratio;
            var mappedEnd = clipStart + (overlapEnd - clipStart) * ratio;
            var before = zoom.Start < clipStart ? clipStart - zoom.Start : 0;
            var after = zoomEnd > oldEnd ? zoomEnd - oldEnd : 0;
            zoom.Start = zoom.Start < clipStart ? zoom.Start : mappedStart;
            zoom.Duration = before + Math.Max(0, mappedEnd - mappedStart) + after;
            if (zoom.Duration < 0.12)
            {
                zooms.RemoveAt(index);
            }
        }
    }
}

/// <summary>
/// 片段的可复制效果包：速度、source 段长度，以及时间窗内的缩放段
/// （换成相对片段起点的偏移）与项目级的画布/画中画/音量设置。
/// </summary>
public sealed class EffectBundle
{
    public double Speed { get; set; } = 1;
    public double SourceLength { get; set; }
    public List<ZoomSpan> Zooms { get; set; } = [];
    public CanvasSettings Canvas { get; set; } = new();
    public CameraBubble Camera { get; set; } = new();
    public double MicrophoneGain { get; set; } = 1;
    public double SystemGain { get; set; } = 1;
}

public static class TimelineEdits
{
    /// <summary>把选中片段的效果属性抽成可序列化的 EffectBundle。</summary>
    public static EffectBundle? CopyEffect(ProjectDocument project, string clipId)
    {
        var placed = TimelineMath.Place(project).FirstOrDefault(item => item.Clip.Id == clipId);
        if (placed is null)
        {
            return null;
        }

        var zooms = project.Zooms
            .Where(zoom => zoom.Start < placed.End - 0.02 && zoom.Start + zoom.Duration > placed.Start + 0.02)
            .Select(zoom => new ZoomSpan
            {
                Start = Math.Max(0, zoom.Start - placed.Start),
                Duration = zoom.Duration,
                Scale = zoom.Scale,
                FocusX = zoom.FocusX,
                FocusY = zoom.FocusY,
            })
            .ToList();
        return new EffectBundle
        {
            Speed = placed.Clip.Speed,
            SourceLength = placed.Clip.SourceEnd - placed.Clip.SourceStart,
            Zooms = zooms,
            Canvas = Copy(project.Canvas),
            Camera = Copy(project.Camera),
            MicrophoneGain = project.MicrophoneGain,
            SystemGain = project.SystemGain,
        };
    }

    /// <summary>
    /// 把效果包贴到另一个片段。includeTiming=false 时只粘属性
    /// （缩放/画布/音量/画中画），不动速度和 source 段长度。
    /// </summary>
    public static bool PasteEffect(ProjectDocument project, string clipId, EffectBundle bundle, bool includeTiming)
    {
        var placed = TimelineMath.Place(project).FirstOrDefault(item => item.Clip.Id == clipId);
        if (placed is null)
        {
            return false;
        }

        if (includeTiming)
        {
            placed.Clip.Speed = Math.Clamp(bundle.Speed, 0.5, 2);
            var length = Math.Max(0.2, bundle.SourceLength);
            placed.Clip.SourceEnd = Math.Clamp(placed.Clip.SourceStart + length, 0, placed.Asset.Duration);
            if (placed.Clip.SourceEnd - placed.Clip.SourceStart < 0.2)
            {
                placed.Clip.SourceStart = Math.Max(0, placed.Asset.Duration - 0.2);
                placed.Clip.SourceEnd = Math.Min(placed.Asset.Duration, placed.Clip.SourceStart + length);
            }
        }

        // 目标片段时间窗里的旧缩放清掉，按复制的相对偏移重建。
        project.Zooms.RemoveAll(zoom =>
            zoom.Start < placed.End - 0.02 && zoom.Start + zoom.Duration > placed.Start + 0.02);
        foreach (var zoom in bundle.Zooms)
        {
            project.Zooms.Add(new ZoomSpan
            {
                Start = placed.Start + zoom.Start,
                Duration = zoom.Duration,
                Scale = zoom.Scale,
                FocusX = zoom.FocusX,
                FocusY = zoom.FocusY,
            });
        }

        project.Zooms.Sort((a, b) => a.Start.CompareTo(b.Start));
        project.Canvas = Copy(bundle.Canvas);
        project.Camera = Copy(bundle.Camera);
        project.MicrophoneGain = bundle.MicrophoneGain;
        project.SystemGain = bundle.SystemGain;
        ProjectFile.Normalize(project);
        return true;
    }

    static CanvasSettings Copy(CanvasSettings source) => new()
    {
        Aspect = source.Aspect,
        Background = source.Background,
        Padding = source.Padding,
        CenterX = source.CenterX,
        CenterY = source.CenterY,
        Scale = source.Scale,
        Corner = source.Corner,
        Shadow = source.Shadow,
        TiltX = source.TiltX,
        TiltY = source.TiltY,
        FocusBlur = source.FocusBlur,
        Intro = source.Intro,
    };

    static CameraBubble Copy(CameraBubble source) => new()
    {
        CenterX = source.CenterX,
        CenterY = source.CenterY,
        Size = source.Size,
    };


    public static bool TrySplit(ProjectDocument project, double time)
    {
        var hit = TimelineMath.Hit(project, time);
        if (hit is null)
        {
            return false;
        }

        var clip = hit.Value.Clip.Clip;
        if (time <= hit.Value.Clip.Start + 0.08 || time >= hit.Value.Clip.End - 0.08)
        {
            return false;
        }

        var cut = clip.SourceStart + (time - hit.Value.Clip.Start) * clip.Speed;
        var right = new ClipSegment
        {
            AssetId = clip.AssetId,
            SourceStart = cut,
            SourceEnd = clip.SourceEnd,
            Speed = clip.Speed,
        };
        clip.SourceEnd = cut;
        var index = project.Clips.FindIndex(item => item.Id == clip.Id);
        project.Clips.Insert(index + 1, right);
        return true;
    }

    /// <summary>
    /// 把片段移到 index 位置（重排）。index 是移动前在 Clips 里的目标槽位。
    /// </summary>
    public static bool TryMoveClip(ProjectDocument project, string clipId, int index)
    {
        var from = project.Clips.FindIndex(item => item.Id == clipId);
        if (from < 0)
        {
            return false;
        }

        index = Math.Clamp(index, 0, project.Clips.Count - 1);
        if (index == from)
        {
            return false;
        }

        var clip = project.Clips[from];
        project.Clips.RemoveAt(from);
        project.Clips.Insert(index, clip);
        return true;
    }

    public static bool TryDeleteClip(ProjectDocument project, string clipId)
    {
        if (project.Clips.Count <= 1)
        {
            return false;
        }

        var placed = TimelineMath.Place(project).FirstOrDefault(item => item.Clip.Id == clipId);
        if (placed is null)
        {
            return false;
        }

        TimelineMath.RemoveRange(project.Zooms, placed.Start, placed.End);
        project.Clips.RemoveAll(item => item.Id == clipId);
        return true;
    }

    public static bool TryTrim(ProjectDocument project, string clipId, double sourceStart, double sourceEnd)
    {
        var clip = project.Clips.FirstOrDefault(item => item.Id == clipId);
        var asset = clip is null ? null : project.Asset(clip.AssetId);
        if (clip is null || asset is null)
        {
            return false;
        }

        sourceStart = Math.Clamp(sourceStart, 0, asset.Duration);
        sourceEnd = Math.Clamp(sourceEnd, 0, asset.Duration);
        if (sourceEnd - sourceStart < 0.2 * clip.Speed)
        {
            return false;
        }

        var slot = TimelineMath.Place(project).First(item => item.Clip.Id == clipId);
        var head = sourceStart - clip.SourceStart;
        if (head > 0.0001)
        {
            TimelineMath.RemoveRange(project.Zooms, slot.Start, slot.Start + head / clip.Speed);
        }
        else if (head < -0.0001)
        {
            TimelineMath.InsertGap(project.Zooms, slot.Start, -head / clip.Speed);
        }

        clip.SourceStart = sourceStart;
        var refreshed = TimelineMath.Place(project).First(item => item.Clip.Id == clipId);
        var tail = clip.SourceEnd - sourceEnd;
        if (tail > 0.0001)
        {
            var removed = tail / clip.Speed;
            TimelineMath.RemoveRange(project.Zooms, refreshed.End - removed, refreshed.End);
        }
        else if (tail < -0.0001)
        {
            TimelineMath.InsertGap(project.Zooms, refreshed.End, -tail / clip.Speed);
        }

        clip.SourceEnd = sourceEnd;
        return true;
    }

    public static bool TrySetSpeed(ProjectDocument project, string clipId, double speed)
    {
        var clip = project.Clips.FirstOrDefault(item => item.Id == clipId);
        if (clip is null)
        {
            return false;
        }

        speed = Math.Clamp(speed, 0.5, 2);
        if (Math.Abs(speed - clip.Speed) < 0.001)
        {
            return false;
        }

        var slot = TimelineMath.Place(project).First(item => item.Clip.Id == clipId);
        var oldDuration = slot.Duration;
        var newDuration = (clip.SourceEnd - clip.SourceStart) / speed;
        TimelineMath.Retime(project.Zooms, slot.Start, oldDuration, newDuration);
        clip.Speed = speed;
        return true;
    }

    /// <summary>
    /// 自动运镜：按录制时存下的点击事件生成缩放段。点击间隔小于
    /// minGap 的只保留最新一次；与已有缩放重叠的跳过。返回新加的段数。
    /// </summary>
    public static int AutoZoom(ProjectDocument project, double scale = 1.6, double hold = 1.6, double lead = 0.35, double minGap = 1.2)
    {
        var total = TimelineMath.Duration(project);
        var clicks = new List<(double Time, double X, double Y)>();
        foreach (var clip in TimelineMath.Place(project))
        {
            foreach (var point in clip.Asset.Cursor)
            {
                if (!point.Click || point.Time < clip.Clip.SourceStart || point.Time > clip.Clip.SourceEnd)
                {
                    continue;
                }

                clicks.Add((clip.Start + (point.Time - clip.Clip.SourceStart) / clip.Clip.Speed,
                    Math.Clamp(point.X, 0, 1), Math.Clamp(point.Y, 0, 1)));
            }
        }

        var added = 0;
        double last = -minGap;
        foreach (var click in clicks.OrderBy(item => item.Time))
        {
            if (click.Time - last < minGap)
            {
                continue;
            }

            var start = Math.Clamp(click.Time - lead, 0, Math.Max(0, total - 0.3));
            var length = Math.Min(hold + lead + 0.4, total - start);
            if (length < 0.3)
            {
                continue;
            }

            // 点击点在画面里的归一化位置 → 缩放窗口的 focus（与
            // EditorWindow.SetZoomFocus 的逆公式一致）。
            var divisor = 1 - 1 / scale;
            var zoom = new ZoomSpan
            {
                Start = start,
                Duration = length,
                Scale = scale,
                FocusX = divisor < 0.02 ? 0.5 : Math.Clamp((click.X - 0.5 / scale) / divisor, 0, 1),
                FocusY = divisor < 0.02 ? 0.5 : Math.Clamp((click.Y - 0.5 / scale) / divisor, 0, 1),
            };
            var overlaps = project.Zooms.Any(item =>
                zoom.Start < item.Start + item.Duration - 0.02 && zoom.Start + zoom.Duration > item.Start + 0.02);
            if (overlaps)
            {
                continue;
            }

            project.Zooms.Add(zoom);
            last = click.Time;
            added++;
        }

        project.Zooms.Sort((a, b) => a.Start.CompareTo(b.Start));
        return added;
    }

    public static ZoomSpan? AddZoom(ProjectDocument project, double time)
    {
        var duration = TimelineMath.Duration(project);
        if (duration < 0.3)
        {
            return null;
        }

        time = Math.Clamp(time, 0, Math.Max(0, duration - 0.3));
        var length = Math.Min(2, duration - time);
        foreach (var existing in project.Zooms.OrderBy(item => item.Start))
        {
            var existingEnd = existing.Start + existing.Duration;
            if (time >= existing.Start - 0.0001 && time < existingEnd - 0.0001)
            {
                return null;
            }

            if (existing.Start >= time && existing.Start < time + length)
            {
                length = existing.Start - time;
            }
        }

        if (length < 0.3)
        {
            return null;
        }

        var zoom = new ZoomSpan { Start = time, Duration = length };
        project.Zooms.Add(zoom);
        project.Zooms.Sort((left, right) => left.Start.CompareTo(right.Start));
        return zoom;
    }

    public static bool TryDeleteZoom(ProjectDocument project, string zoomId) =>
        project.Zooms.RemoveAll(item => item.Id == zoomId) > 0;

    public static bool TryMoveZoom(ProjectDocument project, string zoomId, double start, double? duration = null)
    {
        var zoom = project.Zooms.FirstOrDefault(item => item.Id == zoomId);
        if (zoom is null)
        {
            return false;
        }

        var total = TimelineMath.Duration(project);
        var length = Math.Clamp(duration ?? zoom.Duration, 0.2, Math.Max(0.2, total));
        start = Math.Clamp(start, 0, Math.Max(0, total - length));
        var end = start + length;
        foreach (var other in project.Zooms)
        {
            if (other.Id == zoom.Id)
            {
                continue;
            }

            var otherEnd = other.Start + other.Duration;
            if (start < otherEnd - 0.02 && end > other.Start + 0.02)
            {
                return false;
            }
        }

        zoom.Start = start;
        zoom.Duration = length;
        return true;
    }

    public static SourceMarker? AddMarker(ProjectDocument project, double timelineTime, string? note)
    {
        var hit = TimelineMath.Hit(project, timelineTime);
        if (hit is null)
        {
            return null;
        }

        var marker = new SourceMarker
        {
            AssetId = hit.Value.AssetId,
            SourceTime = hit.Value.SourceTime,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        project.Markers.Add(marker);
        return marker;
    }
}
