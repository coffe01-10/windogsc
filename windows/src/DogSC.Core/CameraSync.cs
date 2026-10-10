namespace DogSC.Core;

/// <summary>
/// 摄像头分段同步点（Mac MediaSyncAnchor）：在某个源时间点固定一个额外偏移，
/// 两点之间线性重映射——先在错位前固定当前，再到错位后调整。
/// </summary>
public sealed class SyncAnchor
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>主时间轴源时间（秒）。</summary>
    public double SourceTime { get; set; }
    /// <summary>该点的额外偏移（秒），UI 端限制 ±10。</summary>
    public double Offset { get; set; }
}

/// <summary>
/// 分段同步曲线（Mac MediaSyncAnchorCurve）：按源时间的分段线性校正。
/// 首个锚点前从零偏移线性爬升；末个锚点后保持；空曲线恒零。
/// </summary>
public static class SyncAnchorCurve
{
    public static double Offset(IReadOnlyList<SyncAnchor> anchors, double sourceTime)
    {
        if (anchors.Count == 0 || !double.IsFinite(sourceTime))
        {
            return 0;
        }

        var t = Math.Max(sourceTime, 0);
        var sorted = anchors.OrderBy(anchor => anchor.SourceTime).ToList();
        var first = sorted[0];

        // 首锚前：从隐含原点 (0,0) 线性爬到第一锚。
        if (first.SourceTime > 0.000001 && t <= first.SourceTime)
        {
            var progress = Math.Clamp(t / first.SourceTime, 0, 1);
            return first.Offset * progress;
        }
        if (t <= first.SourceTime)
        {
            return first.Offset;
        }

        // 二分找右端点。
        var lower = 1;
        var upper = sorted.Count;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (sorted[middle].SourceTime < t)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }
        if (lower >= sorted.Count)
        {
            return sorted[^1].Offset;
        }

        var left = sorted[lower - 1];
        var right = sorted[lower];
        var span = right.SourceTime - left.SourceTime;
        if (span <= 0.000001)
        {
            return right.Offset;
        }

        var lerp = Math.Clamp((t - left.SourceTime) / span, 0, 1);
        return left.Offset + (right.Offset - left.Offset) * lerp;
    }

    /// <summary>找出距给定源时间 tolerance 秒内的锚点索引；-1 表示没有。</summary>
    public static int IndexNear(IReadOnlyList<SyncAnchor> anchors, double sourceTime, double tolerance)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < anchors.Count; i++)
        {
            var distance = Math.Abs(anchors[i].SourceTime - sourceTime);
            if (distance <= tolerance && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }
}
