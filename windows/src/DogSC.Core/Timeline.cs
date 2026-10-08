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

public static class TimelineEdits
{
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
