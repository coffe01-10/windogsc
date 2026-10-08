using System.Globalization;
using System.Text;

namespace DogSC.Core;

/// <summary>
/// 光标轨迹回放：把 MediaAsset.Cursor（source 秒 + 画面归一化坐标）
/// 映射到时间线时刻，预览和导出共用同一套位置解算。
/// </summary>
public static class CursorTrack
{
    /// <summary>点击反馈圈的持续时长（秒）。</summary>
    public const double ClickDuration = 0.45;

    /// <summary>静止隐藏的空闲阈值（秒）——轨迹只在移动时记录。</summary>
    public const double IdleSeconds = 1.6;

    /// <summary>
    /// 时间线时刻的光标位置（画面内 0..1 归一化）。不可见返回 null：
    /// 无轨迹、隐藏模式、或开启静止隐藏且光标已停够久。
    /// </summary>
    public static (double X, double Y)? PositionAt(ProjectDocument project, double time)
    {
        var fx = project.Canvas.Cursor;
        if (fx.Mode == "hidden")
        {
            return null;
        }

        foreach (var clip in TimelineMath.Place(project))
        {
            if (time < clip.Start - 0.0001 || time > clip.End + 0.0001 || clip.Asset.Cursor.Count == 0)
            {
                continue;
            }

            var source = clip.Clip.SourceStart + Math.Clamp(time - clip.Start, 0, clip.Duration) * clip.Clip.Speed;
            var points = clip.Asset.Cursor;
            if (source <= points[0].Time)
            {
                return IdleOr(points[0].X, points[0].Y, fx, source - points[0].Time, points[0].Time - source);
            }

            CursorPoint? previous = null;
            foreach (var point in points)
            {
                if (point.Time > source)
                {
                    var span = point.Time - previous!.Time;
                    var ratio = span < 0.001 ? 1 : (source - previous.Time) / span;
                    var x = previous.X + (point.X - previous.X) * Math.Clamp(ratio, 0, 1);
                    var y = previous.Y + (point.Y - previous.Y) * Math.Clamp(ratio, 0, 1);
                    // 静止隐藏成立的条件：本段是空档且已停够 IdleSeconds。
                    return IdleOr(x, y, fx, source - previous.Time, point.Time - previous.Time);
                }

                previous = point;
            }

            var last = points[^1];
            // 末点之后：空档长度就是已静止的时长（再动就会产生新轨迹点）。
            return IdleOr(last.X, last.Y, fx, source - last.Time, source - last.Time);
        }

        return null;

        (double, double)? IdleOr(double x, double y, CursorFx fx, double idleFor, double gap)
        {
            if (fx.IdleHide && gap > IdleSeconds && idleFor > IdleSeconds)
            {
                return null;
            }

            return (x, y);
        }
    }

    /// <summary>
    /// 点击反馈：当前时刻落在某次点击后的 ClickDuration 内时，返回
    /// 0→1 的进度（0 刚按下，1 结束）。否则 null。
    /// </summary>
    public static double? ClickProgress(ProjectDocument project, double time)
    {
        if (!project.Canvas.Cursor.ClickRipple)
        {
            return null;
        }

        foreach (var clip in TimelineMath.Place(project))
        {
            foreach (var point in clip.Asset.Cursor)
            {
                if (!point.Click || point.Time < clip.Clip.SourceStart || point.Time > clip.Clip.SourceEnd)
                {
                    continue;
                }

                var at = clip.Start + (point.Time - clip.Clip.SourceStart) / clip.Clip.Speed;
                var progress = (time - at) / ClickDuration;
                if (progress >= 0 && progress < 1)
                {
                    return progress;
                }
            }
        }

        return null;
    }

    /// <summary>当前时刻最近一次点击的画面位置（反馈圈中心）。</summary>
    public static (double X, double Y)? LastClickAt(ProjectDocument project, double time)
    {
        foreach (var clip in TimelineMath.Place(project))
        {
            foreach (var point in clip.Asset.Cursor)
            {
                if (!point.Click || point.Time < clip.Clip.SourceStart || point.Time > clip.Clip.SourceEnd)
                {
                    continue;
                }

                var at = clip.Start + (point.Time - clip.Clip.SourceStart) / clip.Clip.Speed;
                if (time >= at && time < at + ClickDuration)
                {
                    return (Math.Clamp(point.X, 0, 1), Math.Clamp(point.Y, 0, 1));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 导出端 overlay 用的逐帧表达式：把轨迹抽稀成折线，生成
    /// if(lt(t,T1),L0, if(lt(t,T2),L1, ...)) 形式的嵌套表达式。
    /// 坐标是像素表达式（x/y 以帧宽/高乘归一化位置）。
    /// </summary>
    public static (string X, string Y)? OverlayExpression(ProjectDocument project, int maxPoints = 48)
    {
        var segments = new List<(double Time, double X, double Y)>();
        foreach (var clip in TimelineMath.Place(project))
        {
            var points = clip.Asset.Cursor;
            if (points.Count == 0)
            {
                continue;
            }

            var step = Math.Max(1, points.Count / maxPoints);
            for (var i = 0; i < points.Count; i += step)
            {
                var p = points[i];
                if (p.Time < clip.Clip.SourceStart || p.Time > clip.Clip.SourceEnd)
                {
                    continue;
                }

                var t = clip.Start + (p.Time - clip.Clip.SourceStart) / clip.Clip.Speed;
                segments.Add((t, Math.Clamp(p.X, 0, 1), Math.Clamp(p.Y, 0, 1)));
            }
        }

        if (segments.Count == 0)
        {
            return null;
        }

        // 逐段阶梯表达式：if(lt(t,T_i),X_i, ...)，最后兜底末段值。
        // 滤镜参数里的逗号要转义成 \,。
        string Nest(Func<(double Time, double X, double Y), string> axis)
        {
            var build = new StringBuilder();
            for (var i = 0; i < segments.Count; i++)
            {
                build.Append("if(lt(t,").Append(F(segments[i].Time)).Append(")\\,")
                    .Append(axis(segments[i])).Append("\\,");
            }

            build.Append(axis(segments[^1]));
            for (var i = 0; i < segments.Count; i++)
            {
                build.Append(')');
            }

            return build.ToString();
        }

        return (Nest(p => F(p.X)), Nest(p => F(p.Y)));
    }

    /// <summary>
    /// 光标 overlay 的 enable 表达式：hidden 直接不显示；
    /// 静止隐藏在轨迹空档 &gt; IdleSeconds 的区间返回 0。
    /// 返回 "0"/"1" 或一段 between 组合表达式。
    /// </summary>
    public static string EnableExpression(ProjectDocument project)
    {
        if (project.Canvas.Cursor.Mode == "hidden")
        {
            return "0";
        }

        if (!project.Canvas.Cursor.IdleHide)
        {
            return "1";
        }

        var gaps = IdleGaps(project).ToList();
        if (gaps.Count == 0)
        {
            return "1";
        }

        // 对每个静止空档乘一个 1-between(...) 项。
        var expr = new StringBuilder("1");
        foreach (var (start, end) in gaps)
        {
            expr.Append("*(1-between(t\\,").Append(F(start)).Append("\\,").Append(F(end)).Append("))");
        }

        return expr.ToString();
    }

    /// <summary>光标静止超过 IdleSeconds 的时间区间（时间线秒）。</summary>
    public static IEnumerable<(double Start, double End)> IdleGaps(ProjectDocument project)
    {
        foreach (var clip in TimelineMath.Place(project))
        {
            var points = clip.Asset.Cursor;
            for (var i = 1; i < points.Count; i++)
            {
                var gap = points[i].Time - points[i - 1].Time;
                if (gap <= IdleSeconds)
                {
                    continue;
                }

                var a = clip.Start + (points[i - 1].Time - clip.Clip.SourceStart) / clip.Clip.Speed;
                var b = clip.Start + (points[i].Time - clip.Clip.SourceStart) / clip.Clip.Speed;
                if (b - a > IdleSeconds)
                {
                    yield return (a + IdleSeconds, b);
                }
            }
        }
    }

    static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
