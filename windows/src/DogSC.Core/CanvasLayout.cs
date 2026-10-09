namespace DogSC.Core;

public readonly record struct FramePlacement(
    int X,
    int Y,
    int Width,
    int Height,
    int Radius,
    int ShadowBlur,
    int ShadowOffset,
    double ShadowAlpha);

public readonly record struct BubblePlacement(int X, int Y, int Size);

public static class CanvasLayout
{
    public static int Even(int value) => Math.Max(2, value & ~1);

    public static (int Width, int Height) OutputSize(int sourceWidth, int sourceHeight, string? aspect, string? resolution)
    {
        sourceWidth = Math.Max(2, sourceWidth);
        sourceHeight = Math.Max(2, sourceHeight);
        var sourceAspect = string.IsNullOrWhiteSpace(aspect) || aspect is "source" or "适应";
        double aspectWidth = sourceWidth, aspectHeight = sourceHeight;
        var ratio = aspect?.Split(':');
        if (ratio is { Length: 2 }
            && double.TryParse(ratio[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
            && double.TryParse(ratio[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)
            && w > 0 && h > 0)
        {
            aspectWidth = w;
            aspectHeight = h;
        }

        var shortTarget = resolution switch
        {
            "1080" or "1080p" => 1080,
            "1440" or "1440p" => 1440,
            "2160" or "4k" or "4K" => 2160,
            _ => 0,
        };

        int width;
        int height;
        if (shortTarget > 0)
        {
            if (aspectWidth >= aspectHeight)
            {
                height = shortTarget;
                width = (int)Math.Round(shortTarget * (aspectWidth / (double)aspectHeight));
            }
            else
            {
                width = shortTarget;
                height = (int)Math.Round(shortTarget * (aspectHeight / (double)aspectWidth));
            }
        }
        else if (sourceAspect)
        {
            width = sourceWidth;
            height = sourceHeight;
        }
        else if (aspectWidth >= aspectHeight)
        {
            width = Math.Max(sourceWidth, sourceHeight);
            height = (int)Math.Round(width * (aspectHeight / (double)aspectWidth));
        }
        else
        {
            height = Math.Max(sourceWidth, sourceHeight);
            width = (int)Math.Round(height * (aspectWidth / (double)aspectHeight));
        }

        return (Even(width), Even(height));
    }

    public static FramePlacement Place(int canvasWidth, int canvasHeight, int videoWidth, int videoHeight, CanvasSettings canvas)
    {
        var cropped = Crop(videoWidth, videoHeight, canvas.Crop);
        videoWidth = cropped.Width;
        videoHeight = cropped.Height;
        videoWidth = Math.Max(2, videoWidth);
        videoHeight = Math.Max(2, videoHeight);
        var pad = canvas.Padding / 1080.0 * canvasHeight;
        var availableWidth = Math.Max(2, canvasWidth - pad * 2);
        var availableHeight = Math.Max(2, canvasHeight - pad * 2);
        var fit = Math.Min(availableWidth / videoWidth, availableHeight / videoHeight);
        var scale = fit * Math.Clamp(canvas.Scale, 0.25, 2.5);
        var frameWidth = Even((int)Math.Round(videoWidth * scale));
        var frameHeight = Even((int)Math.Round(videoHeight * scale));
        var x = (int)Math.Round(Math.Clamp(canvas.CenterX, 0, 1) * canvasWidth - frameWidth / 2.0);
        var y = (int)Math.Round(Math.Clamp(canvas.CenterY, 0, 1) * canvasHeight - frameHeight / 2.0);
        var radius = (int)Math.Round(Math.Clamp(canvas.Corner, 0, 240) / 1080.0 * canvasHeight);
        radius = Math.Clamp(radius, 0, Math.Min(frameWidth, frameHeight) / 2);
        var shadowT = Math.Clamp(canvas.Shadow / 100.0, 0, 1);
        return new FramePlacement(
            x,
            y,
            frameWidth,
            frameHeight,
            radius,
            (int)Math.Round(shadowT * 28),
            (int)Math.Round(shadowT * 22),
            shadowT * 0.5);
    }

    public static (int X, int Y, int Width, int Height) Crop(int width, int height, SourceCrop crop)
    {
        var w = Even((int)Math.Round(width * crop.Width));
        var h = Even((int)Math.Round(height * crop.Height));
        return (Math.Min(width - w, (int)Math.Round(width * crop.X)) & ~1, Math.Min(height - h, (int)Math.Round(height * crop.Y)) & ~1, w, h);
    }

    public static BubblePlacement Bubble(int canvasWidth, int canvasHeight, CameraBubble camera)
    {
        var size = Even((int)Math.Round(canvasWidth * Math.Clamp(camera.Size, 0.08, 0.46)));
        var x = (int)Math.Round(Math.Clamp(camera.CenterX, 0, 1) * canvasWidth - size / 2.0);
        var y = (int)Math.Round(Math.Clamp(camera.CenterY, 0, 1) * canvasHeight - size / 2.0);
        return new BubblePlacement(x, y, size);
    }

    /// <summary>开场动画时长（秒）。</summary>
    public const double IntroDuration = 0.6;

    /// <summary>开场进度 0→1；none 或无动画时返回 1（已完成）。</summary>
    public static double IntroProgress(CanvasSettings canvas, double time) =>
        canvas.Intro is "none" || string.IsNullOrWhiteSpace(canvas.Intro)
            ? 1
            : Math.Clamp(time / IntroDuration, 0, 1);

    /// <summary>
    /// 缩放进出的平滑系数：进入段 0→1、退出段 1→0，其余时间 1。
    /// 预览与导出共用同一个 ramp，保证两端一致。
    /// </summary>
    public static double ZoomRamp(ZoomSpan zoom, double time)
    {
        if (zoom.Duration <= 0.001)
        {
            return 0;
        }

        var ease = Math.Min(0.4, zoom.Duration / 3);
        var into = Math.Clamp((time - zoom.Start) / ease, 0, 1);
        var outOf = Math.Clamp((zoom.Start + zoom.Duration - time) / ease, 0, 1);
        return into * outOf;
    }

    /// <summary>某时刻的实际缩放倍数（含过渡）。</summary>
    public static double ZoomScaleAt(ZoomSpan zoom, double time) =>
        1 + (zoom.Scale - 1) * ZoomRamp(zoom, time);

    /// <summary>某时刻的插值焦点：ramp=0 时回到画面中心。</summary>
    public static (double X, double Y) ZoomFocusAt(ZoomSpan zoom, double time)
    {
        var ramp = ZoomRamp(zoom, time);
        return (0.5 + (zoom.FocusX - 0.5) * ramp, 0.5 + (zoom.FocusY - 0.5) * ramp);
    }

    public static (int Width, int Height, int X, int Y) ZoomCrop(int videoWidth, int videoHeight, double scale, double focusX, double focusY)
    {
        scale = Math.Clamp(scale, 1, 4);
        var cropWidth = Even(Math.Clamp((int)Math.Floor(videoWidth / scale), 2, videoWidth));
        var cropHeight = Even(Math.Clamp((int)Math.Floor(videoHeight / scale), 2, videoHeight));
        var maxX = Math.Max(0, videoWidth - cropWidth);
        var maxY = Math.Max(0, videoHeight - cropHeight);
        var x = Math.Clamp((int)Math.Round(maxX * Math.Clamp(focusX, 0, 1)), 0, maxX);
        var y = Math.Clamp((int)Math.Round(maxY * Math.Clamp(focusY, 0, 1)), 0, maxY);
        x &= ~1;
        y &= ~1;
        return (cropWidth, cropHeight, x, y);
    }

    public static long EstimateBytes(int width, int height, int fps, double duration, bool audioOnly)
    {
        if (audioOnly)
        {
            return (long)(160_000 / 8.0 * Math.Max(duration, 0.1));
        }

        var bitsPerPixel = 0.12;
        var bitrate = width * height * Math.Max(fps, 1) * bitsPerPixel + 160_000;
        return (long)(bitrate / 8.0 * Math.Max(duration, 0.1));
    }
}
