namespace DogSC.Core;

/// <summary>
/// 窗口录制的画面跟随：实际采集的是窗口所在监视器，ffmpeg 端用 crop
/// 把画面裁到窗口矩形。这里的纯函数负责把「窗口物理矩形 + 监视器物理
/// 矩形」换算成 crop 参数，便于单测。
/// </summary>
public static class CaptureRegion
{
    public readonly record struct Box(int X, int Y, int Width, int Height);

    /// <summary>
    /// 选窗口当前位置所在的监视器（按窗口中心点归属），找不到时返回 null。
    /// </summary>
    public static Box? MonitorFor(Box window, IReadOnlyList<Box> monitors)
    {
        var cx = window.X + window.Width / 2;
        var cy = window.Y + window.Height / 2;
        foreach (var monitor in monitors)
        {
            if (cx >= monitor.X && cx < monitor.X + monitor.Width &&
                cy >= monitor.Y && cy < monitor.Y + monitor.Height)
            {
                return monitor;
            }
        }

        return null;
    }

    /// <summary>
    /// 把窗口矩形映射成相对监视器的 crop 矩形：clamp 到监视器内、宽高取偶，
    /// 窗口跑出监视器边缘的部分按比例收缩。
    /// </summary>
    public static Box Crop(Box window, Box monitor)
    {
        var width = window.Width;
        var height = window.Height;
        var x = window.X;
        var y = window.Y;
        if (width < 0)
        {
            x += width;
            width = -width;
        }

        if (height < 0)
        {
            y += height;
            height = -height;
        }

        var left = Math.Max(x, monitor.X);
        var top = Math.Max(y, monitor.Y);
        var right = Math.Min(x + width, monitor.X + monitor.Width);
        var bottom = Math.Min(y + height, monitor.Y + monitor.Height);
        var cropWidth = Math.Max(64, right - left) & ~1;
        var cropHeight = Math.Max(64, bottom - top) & ~1;
        // 监视器比 64 还窄/矮时，保底宽本身就越界：先收口到监视器内再 clamp 位置。
        cropWidth = Math.Max(2, Math.Min(cropWidth, monitor.Width) & ~1);
        cropHeight = Math.Max(2, Math.Min(cropHeight, monitor.Height) & ~1);
        // 宽高被 clamp 放大时不要越过监视器边界。
        left = Math.Min(left, monitor.X + Math.Max(0, monitor.Width - cropWidth));
        top = Math.Min(top, monitor.Y + Math.Max(0, monitor.Height - cropHeight));
        return new Box(left - monitor.X, top - monitor.Y, cropWidth, cropHeight);
    }
}
