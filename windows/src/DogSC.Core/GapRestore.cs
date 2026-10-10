namespace DogSC.Core;

/// <summary>
/// Mac 剪切缺口恢复：被裁掉的源素材仍躺在源文件里，在两个保留片段的交界处
/// 可整段还原。还原后所有时间轴定位的效果整体后移；跨越插入点的保持区间被拉长。
/// </summary>
public static class GapRestore
{
    private const double Eps = 1e-6;

    /// <summary>两个相邻片段之间可还原的源素材长度（秒，源时间）；无缺口返回 0。</summary>
    public static double RestorableGap(ProjectDocument project, string previousClipId)
    {
        var clips = project.Clips;
        var index = clips.FindIndex(c => c.Id == previousClipId);
        if (index < 0 || index + 1 >= clips.Count)
        {
            return 0;
        }

        var previous = clips[index];
        var next = clips[index + 1];
        return next.AssetId == previous.AssetId
            ? Math.Max(0, next.SourceStart - previous.SourceEnd)
            : 0;
    }

    /// <summary>在 previous/next 交界处插回被裁掉的源素材，并把后续效果整体移位。</summary>
    public static bool RestoreGap(ProjectDocument project, string previousClipId, string nextClipId)
    {
        var clips = project.Clips;
        var index = clips.FindIndex(c => c.Id == previousClipId);
        if (index < 0 || index + 1 >= clips.Count)
        {
            return false;
        }

        var previous = clips[index];
        var next = clips[index + 1];
        if (next.Id != nextClipId || next.AssetId != previous.AssetId)
        {
            return false;
        }

        var gap = next.SourceStart - previous.SourceEnd;
        if (gap <= Eps)
        {
            return false;
        }

        var inserted = new ClipSegment
        {
            AssetId = previous.AssetId,
            SourceStart = previous.SourceEnd,
            SourceEnd = next.SourceStart,
            Speed = 1,
        };
        clips.Insert(index + 1, inserted);
        // 插入点 = 前段的输出结束位置（插入后 its placed slot covers that point）
        var insertionTime = TimelineMath.Place(project)
            .First(p => p.Clip.Id == inserted.Id).Start;
        Shift(project, insertionTime, gap);
        return true;
    }

    /// <summary>还原片头裁掉的素材：首段 SourceStart &gt; 0 时插回 [0, SourceStart)。</summary>
    public static bool RestoreLeading(ProjectDocument project)
    {
        if (project.Clips.Count == 0)
        {
            return false;
        }

        var first = project.Clips[0];
        if (first.SourceStart <= Eps)
        {
            return false;
        }

        project.Clips.Insert(0, new ClipSegment
        {
            AssetId = first.AssetId,
            SourceStart = 0,
            SourceEnd = first.SourceStart,
            Speed = 1,
        });
        Shift(project, 0, first.SourceStart);
        return true;
    }

    /// <summary>还原片尾裁掉的素材：末段 SourceEnd &lt; 源时长时追加到结尾，效果不移位。</summary>
    public static bool RestoreTrailing(ProjectDocument project, double sourceDuration)
    {
        if (project.Clips.Count == 0)
        {
            return false;
        }

        var last = project.Clips[^1];
        if (sourceDuration - last.SourceEnd <= Eps)
        {
            return false;
        }

        project.Clips.Add(new ClipSegment
        {
            AssetId = last.AssetId,
            SourceStart = last.SourceEnd,
            SourceEnd = sourceDuration,
            Speed = 1,
        });
        return true;
    }

    private static void Shift(ProjectDocument project, double insertionTime, double duration)
    {
        foreach (var zoom in project.Zooms)
        {
            if (zoom.Start >= insertionTime - Eps)
            {
                zoom.Start += duration;
            }
            else if (zoom.Start + zoom.Duration > insertionTime + Eps)
            {
                zoom.Duration += duration;
            }
        }

        MotionTimingEdits.InsertGap(project, insertionTime, duration);

        foreach (var sticker in project.Canvas.Stickers)
        {
            if (sticker.Timing is { } timing)
            {
                ShiftWindow(timing, insertionTime, duration);
            }
        }

        foreach (var soft in project.Canvas.SoftRegions)
        {
            if (soft.Timing is { } timing)
            {
                ShiftWindow(timing, insertionTime, duration);
            }
        }
        // SourceMarker 与 Cursor 采样记的是源素材时间，不随时间轴移位。
    }

    private static void ShiftWindow(EffectWindow window, double insertionTime, double duration)
    {
        if (window.Start >= insertionTime - Eps)
        {
            window.Start += duration;
        }
        else if (window.Duration > 0 && window.Start + window.Duration > insertionTime + Eps)
        {
            window.Duration += duration;
        }
    }
}
