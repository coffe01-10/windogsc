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
                Duration = Math.Min(zoom.Start + zoom.Duration, placed.End) - Math.Max(zoom.Start, placed.Start),
                Scale = zoom.Scale,
                FocusEffect = zoom.FocusEffect is null ? null : ScenePreset.Clone(zoom.FocusEffect),
                FocusX = zoom.FocusX,
                FocusY = zoom.FocusY,
                Origin = zoom.Origin, Easing = zoom.Easing, EnterDuration = zoom.EnterDuration, ExitDuration = zoom.ExitDuration,
                SpringMass = zoom.SpringMass, SpringStiffness = zoom.SpringStiffness, SpringDamping = zoom.SpringDamping,
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
            placed.Clip.Speed = Math.Clamp(bundle.Speed, 0.5, 100);
            var length = Math.Max(0.2, bundle.SourceLength);
            placed.Clip.SourceEnd = Math.Clamp(placed.Clip.SourceStart + length, 0, placed.Asset.Duration);
            if (placed.Clip.SourceEnd - placed.Clip.SourceStart < 0.2)
            {
                placed.Clip.SourceStart = Math.Max(0, placed.Asset.Duration - 0.2);
                placed.Clip.SourceEnd = Math.Min(placed.Asset.Duration, placed.Clip.SourceStart + length);
            }
        }

        // 替换片段内的效果，片段外的缩放保留；时长改变时后续效果随素材平移。
        var newDuration = TimelineMath.ClipDuration(placed.Clip);
        var delta = newDuration - placed.Duration;
        var retained = new List<ZoomSpan>();
        foreach (var zoom in project.Zooms)
        {
            var end = zoom.Start + zoom.Duration;
            if (end <= placed.Start)
            {
                retained.Add(zoom);
            }
            else if (zoom.Start >= placed.End)
            {
                zoom.Start += delta;
                retained.Add(zoom);
            }
            else
            {
                var before = placed.Start - zoom.Start;
                var after = end - placed.End;
                if (after >= 0.12)
                {
                    var tail = CopyZoom(zoom);
                    tail.Start = placed.Start + newDuration;
                    tail.Duration = after;
                    retained.Add(tail);
                }
                if (before >= 0.12)
                {
                    zoom.Duration = before;
                    retained.Add(zoom);
                }
            }
        }
        project.Zooms = retained;
        foreach (var zoom in bundle.Zooms)
        {
            var start = Math.Max(0, zoom.Start);
            var end = Math.Min(newDuration, zoom.Start + zoom.Duration);
            if (end - start < 0.12)
            {
                continue;
            }
            project.Zooms.Add(new ZoomSpan
            {
                Start = placed.Start + start,
                Duration = end - start,
                Scale = zoom.Scale,
                FocusEffect = zoom.FocusEffect is null ? null : ScenePreset.Clone(zoom.FocusEffect),
                FocusX = zoom.FocusX,
                FocusY = zoom.FocusY,
                Origin = zoom.Origin, Easing = zoom.Easing, EnterDuration = zoom.EnterDuration, ExitDuration = zoom.ExitDuration,
                SpringMass = zoom.SpringMass, SpringStiffness = zoom.SpringStiffness, SpringDamping = zoom.SpringDamping,
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

    public static ZoomSpan CopyZoom(ZoomSpan source) => new()
    {
        Start = source.Start,
        Duration = source.Duration,
        Scale = source.Scale,
        FocusEffect = source.FocusEffect is null ? null : ScenePreset.Clone(source.FocusEffect),
        FocusX = source.FocusX,
        FocusY = source.FocusY,
        Origin = source.Origin, Easing = source.Easing, EnterDuration = source.EnterDuration, ExitDuration = source.ExitDuration,
        SpringMass = source.SpringMass, SpringStiffness = source.SpringStiffness, SpringDamping = source.SpringDamping,
        CurveX1 = source.CurveX1, CurveY1 = source.CurveY1, CurveX2 = source.CurveX2, CurveY2 = source.CurveY2,
    };

    /// <summary>完整复制缩放段；只吸附同轨边缘，不覆盖已有段或截短副本。</summary>
    public static ZoomSpan? PasteZoom(ProjectDocument project, ZoomSpan source, double time,
        double snapThreshold, out string? error)
    {
        var targets = project.Zooms.SelectMany(zoom => new[]
        {
            zoom.Start, zoom.Start + zoom.Duration,
            zoom.Start - source.Duration, zoom.Start + zoom.Duration - source.Duration,
        });
        time = TimelineMath.Snap(time, targets, snapThreshold);
        error = null;
        if (time < 0 || time + source.Duration > TimelineMath.Duration(project) + 0.000001)
        {
            error = "此位置的剩余时长不足，请在更靠前的位置粘贴。";
            return null;
        }
        if (project.Zooms.Any(zoom => time < zoom.Start + zoom.Duration - 0.000001
            && time + source.Duration > zoom.Start + 0.000001))
        {
            error = "此处已有缩放，请在足够长的空白处粘贴。";
            return null;
        }
        var copy = CopyZoom(source);
        copy.Start = time;
        project.Zooms.Add(copy);
        project.Zooms.Sort((a, b) => a.Start.CompareTo(b.Start));
        return copy;
    }

    public static bool PasteZoomAttributes(ProjectDocument project, string zoomId, ZoomSpan source)
    {
        var target = project.Zooms.FirstOrDefault(zoom => zoom.Id == zoomId);
        if (target is null)
        {
            return false;
        }
        target.Scale = source.Scale;
        target.FocusEffect = source.FocusEffect is null ? null : ScenePreset.Clone(source.FocusEffect);
        target.FocusX = source.FocusX;
        target.FocusY = source.FocusY;
        target.Origin = "manual";
        target.Easing = source.Easing;
        target.EnterDuration = source.EnterDuration;
        target.ExitDuration = source.ExitDuration;
        target.SpringMass = source.SpringMass; target.SpringStiffness = source.SpringStiffness; target.SpringDamping = source.SpringDamping;
        target.CurveX1 = source.CurveX1; target.CurveY1 = source.CurveY1;
        target.CurveX2 = source.CurveX2; target.CurveY2 = source.CurveY2;
        return true;
    }

    static CanvasSettings Copy(CanvasSettings source) => ScenePreset.Clone(source);

    static CameraBubble Copy(CameraBubble source) => new()
    {
        CenterX = source.CenterX,
        CenterY = source.CenterY,
        Size = source.Size,
        Shape = source.Shape,
        Entry = source.Entry,
        Corner = source.Corner,
        Hidden = source.Hidden,
        Mirrored = source.Mirrored,
        TimingAdvance = source.TimingAdvance,
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
            MicrophoneGain = clip.MicrophoneGain,
            SystemGain = clip.SystemGain,
            MicrophoneEnabled = clip.MicrophoneEnabled,
            SystemEnabled = clip.SystemEnabled,
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

        EffectTiming.Edit(project, spans => TimelineMath.RemoveRange(spans, placed.Start, placed.End));
        MotionTimingEdits.RemoveRange(project, placed.Start, placed.End);

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
            EffectTiming.Edit(project, spans => TimelineMath.RemoveRange(spans, slot.Start, slot.Start + head / clip.Speed));
            MotionTimingEdits.RemoveRange(project, slot.Start, slot.Start + head / clip.Speed);
            TimelineMath.RemoveRange(project.Zooms, slot.Start, slot.Start + head / clip.Speed);
        }
        else if (head < -0.0001)
        {
            EffectTiming.Edit(project, spans => TimelineMath.InsertGap(spans, slot.Start, -head / clip.Speed));
            MotionTimingEdits.InsertGap(project, slot.Start, -head / clip.Speed);
            TimelineMath.InsertGap(project.Zooms, slot.Start, -head / clip.Speed);
        }

        clip.SourceStart = sourceStart;
        var refreshed = TimelineMath.Place(project).First(item => item.Clip.Id == clipId);
        var tail = clip.SourceEnd - sourceEnd;
        if (tail > 0.0001)
        {
            var removed = tail / clip.Speed;
            EffectTiming.Edit(project, spans => TimelineMath.RemoveRange(spans, refreshed.End - removed, refreshed.End));
            MotionTimingEdits.RemoveRange(project, refreshed.End - removed, refreshed.End);
            TimelineMath.RemoveRange(project.Zooms, refreshed.End - removed, refreshed.End);
        }
        else if (tail < -0.0001)
        {
            EffectTiming.Edit(project, spans => TimelineMath.InsertGap(spans, refreshed.End, -tail / clip.Speed));
            MotionTimingEdits.InsertGap(project, refreshed.End, -tail / clip.Speed);
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

        speed = Math.Clamp(speed, 0.5, 100);
        if (Math.Abs(speed - clip.Speed) < 0.001)
        {
            return false;
        }

        var slot = TimelineMath.Place(project).First(item => item.Clip.Id == clipId);
        var oldDuration = slot.Duration;
        var newDuration = (clip.SourceEnd - clip.SourceStart) / speed;
        EffectTiming.Edit(project, spans => TimelineMath.Retime(spans, slot.Start, oldDuration, newDuration));
        MotionTimingEdits.Retime(project, slot.Start, oldDuration, newDuration);
        TimelineMath.Retime(project.Zooms, slot.Start, oldDuration, newDuration);
        clip.Speed = speed;
        return true;
    }

    /// <summary>一轮连续操作生成一个镜头；附近点击保持构图，换目标由 ZoomMotion 连续衔接。</summary>
    public static int AutoZoom(ProjectDocument project, double? scale = null, double hold = .9, double lead = .126, double minGap = 1.4)
    {
        var zoomScale = scale ?? project.Motion.DefaultZoomScale ?? 1.6;
        var transition = project.Motion.DefaultZoomTransitionDuration;
        var total = TimelineMath.Duration(project);
        var events = ZoomMotion.Events(project);
        var manual = project.Zooms.Where(z => z.Origin != "automatic").OrderBy(z => z.Start).ToArray();
        var clicks = events.Where(p => p.Click && !manual.Any(z => p.Time >= z.Start && p.Time < z.Start + z.Duration)).ToArray();
        if (clicks.Length == 0) return 0;
        project.Zooms.RemoveAll(z => z.Origin == "automatic");
        var groups = new List<List<(double Time, double X, double Y, bool Click)>>();
        foreach (var click in clicks)
        {
            if (groups.Count == 0 || click.Time - groups[^1][^1].Time > minGap
                || manual.Any(z => z.Start >= groups[^1][^1].Time && z.Start < click.Time))
                groups.Add([]);
            groups[^1].Add(click);
        }
        double previousEnd = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var first = group[0]; var last = group[^1];
            var before = manual.Where(z => z.Start + z.Duration <= first.Time).Select(z => z.Start + z.Duration).DefaultIfEmpty(0).Max();
            var after = manual.Where(z => z.Start > last.Time).Select(z => z.Start).DefaultIfEmpty(total).Min();
            var start = Math.Max(Math.Max(first.Time - lead, previousEnd), before);
            var next = i + 1 < groups.Count ? groups[i + 1][0].Time - lead : total;
            var limit = Math.Min(after, next);
            var activity = last.Time;
            var prior = events.LastOrDefault(p => p.Time <= last.Time);
            foreach (var point in events.Where(p => p.Time > last.Time && p.Time < limit))
            {
                var moved = Math.Abs(point.X - prior.X) > .002 || Math.Abs(point.Y - prior.Y) > .002;
                prior = point;
                if (!point.Click && !moved) continue; // 定时采样的静止指针不能把保持段延长到全片。
                if (point.Time - activity > minGap) break;
                activity = point.Time;
            }
            var holdEnd = Math.Max(last.Time + hold, activity + .55);
            var end = Math.Min(Math.Max(holdEnd, start + transition) + transition, limit);
            if (end - start < .12) continue;
            var zoom = new ZoomSpan
            {
                Start = start, Duration = end - start, Scale = Math.Clamp(zoomScale, 1, 6), Origin = "automatic",
                Easing = project.Motion.DefaultZoomEasing, EnterDuration = transition,
                ExitDuration = end >= total - .001 ? 0 : Math.Min(transition, Math.Max(0, end - holdEnd)),
                SpringMass = project.Motion.ScreenSpringMass, SpringStiffness = project.Motion.ScreenSpringStiffness,
                SpringDamping = project.Motion.ScreenSpringDamping,
                FocusX = ZoomMotion.Focus((first.X - project.Canvas.Crop.X) / project.Canvas.Crop.Width, zoomScale),
                FocusY = ZoomMotion.Focus((first.Y - project.Canvas.Crop.Y) / project.Canvas.Crop.Height, zoomScale),
            };
            project.Zooms.Add(zoom);
            previousEnd = end;
        }
        project.Zooms.Sort((a, b) => a.Start.CompareTo(b.Start));
        return project.Zooms.Count(z => z.Origin == "automatic");
    }

    // Existing clips are selected rather than covered by a second, neutral 3D target.
    // A new clip is returned detached so the editor can capture undo before inserting it.
    public static ScreenMotionClip? PrepareScreenMotion(ProjectDocument project, double time, double? transition = null,
        ScreenMotionCreationStyle? fallbackStyle = null)
    {
        var existing = project.ScreenMotions.LastOrDefault(c => time >= c.Timing.StartTime && time < c.Timing.EndTime);
        if (existing is not null) return existing;
        time = Math.Max(0, time);
        var style = project.Motion.DefaultScreenMotion ?? fallbackStyle;
        var enter = style?.LeadInDuration ?? transition ?? project.Motion.DefaultZoomTransitionDuration;
        var exit = style?.ReturnDuration ?? transition ?? project.Motion.DefaultZoomTransitionDuration;
        var limit = TimelineMath.Duration(project);
        foreach (var next in project.ScreenMotions.Where(c => c.Timing.StartTime > time))
            limit = Math.Min(limit, next.Timing.StartTime);
        var end = Math.Min(time + 1, limit);
        if (end - time < .12) return null;
        var timing = new TransitionTiming { StartTime = time, Duration = end - time,
            LeadInDuration = Math.Min(enter, end - time), ReturnDuration = Math.Min(exit, limit - end),
            PreferredLeadInDuration = enter, PreferredReturnDuration = exit, Easing = project.Motion.DefaultZoomEasing };
        if (style is not null) return style.Create(timing);
        return new ScreenMotionClip
        {
            Timing = timing,
            Target = ScenePreset.Clone(new ScreenMotionTrack(project.ScreenMotions).SampleLayout(time, springMass: project.Motion.ScreenSpringMass,
                springStiffness: project.Motion.ScreenSpringStiffness, springDamping: project.Motion.ScreenSpringDamping).State),
        };
    }

    public static ZoomSpan? AddZoom(ProjectDocument project, double time, double fallbackScale = 1.6)
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

        var motion = project.Motion;
        var zoom = new ZoomSpan { Start = time, Duration = length, Scale = motion.DefaultZoomScale ?? fallbackScale,
            Easing = motion.DefaultZoomEasing, EnterDuration = motion.DefaultZoomTransitionDuration, ExitDuration = motion.DefaultZoomTransitionDuration,
            SpringMass = motion.ScreenSpringMass, SpringStiffness = motion.ScreenSpringStiffness, SpringDamping = motion.ScreenSpringDamping };
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
