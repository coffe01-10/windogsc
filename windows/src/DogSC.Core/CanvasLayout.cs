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

public readonly record struct BubblePlacement(int X, int Y, int Size)
{
    public int Height { get; init; } = Size;
    public int Width => Size;
}

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
        var placement = new FramePlacement(
            x,
            y,
            frameWidth,
            frameHeight,
            radius,
            (int)Math.Round(shadowT * 28),
            (int)Math.Round(shadowT * 22),
            shadowT * 0.5);
        if (ChromeGeometry.Resolve(canvas, placement, canvasWidth, canvasHeight) is { } chrome)
            placement = placement with { Radius = (int)Math.Round(chrome.ContentRadius) };
        return placement;
    }

    public static (int X, int Y, int Width, int Height) Crop(int width, int height, SourceCrop crop)
    {
        var w = Even((int)Math.Round(width * crop.Width));
        var h = Even((int)Math.Round(height * crop.Height));
        return (Math.Min(width - w, (int)Math.Round(width * crop.X)) & ~1, Math.Min(height - h, (int)Math.Round(height * crop.Y)) & ~1, w, h);
    }

    public static BubblePlacement Bubble(int canvasWidth, int canvasHeight, CameraBubble camera, double sourceAspect = 16d / 9)
    {
        var aspect = camera.Shape switch { "horizontal" => 1.5, "vertical" => .72, "original" => sourceAspect, _ => 1 };
        if (camera.Docked)
        {
            var sample = CameraMotionTrack.Resolve(new() { Layout = camera.Shape == "none" ? "square" : camera.Shape,
                X = camera.CenterX, Y = camera.CenterY, Size = camera.Size }, sourceAspect, canvasWidth / (double)canvasHeight);
            var rect = MotionExport.CameraPixels(sample, canvasWidth, canvasHeight, camera);
            return new((int)Math.Round(rect.X), (int)Math.Round(rect.Y), Even((int)Math.Round(rect.W)))
                { Height = Even((int)Math.Round(rect.H)) };
        }
        var width = Even((int)Math.Round(canvasWidth * Math.Clamp(camera.Size, .08, .46)));
        var height = Even((int)Math.Round(width / aspect));
        var fit = Math.Min(1, Math.Min(canvasWidth / (double)width, canvasHeight / (double)height));
        width = Even((int)(width * fit)); height = Even((int)(height * fit));
        var x = (int)Math.Round(Math.Clamp(camera.CenterX * canvasWidth - width / 2d, 0, canvasWidth - width));
        var y = (int)Math.Round(Math.Clamp(camera.CenterY * canvasHeight - height / 2d, 0, canvasHeight - height));
        return new BubblePlacement(x, y, width) { Height = height };
    }

    public static OverlayBox FitCamera(OverlayBox box, double width, double height, CameraBubble camera)
    {
        if (!camera.Docked) return box.Fit(width, height);
        // Docking also works for an oversized aperture: its two edges can
        // stop at either canvas edge without shrinking the authored camera.
        return box with { X = Math.Clamp(box.X, Math.Min(box.Width / 2, width - box.Width / 2), Math.Max(box.Width / 2, width - box.Width / 2)),
            Y = Math.Clamp(box.Y, Math.Min(box.Height / 2, height - box.Height / 2), Math.Max(box.Height / 2, height - box.Height / 2)) };
    }

    public static void WriteCameraBox(CameraBubble camera, OverlayBox box, double width, double height,
        double screenZoomScale, bool resize)
    {
        if (camera.Docked)
        {
            // A full-width/full-height aperture has no recoverable docking
            // coordinate; retain it so it returns when the camera shrinks.
            if (Math.Abs(width - box.Width) > .0001) camera.CenterX = Math.Clamp((box.X - box.Width / 2) / (width - box.Width), 0, 1);
            if (Math.Abs(height - box.Height) > .0001) camera.CenterY = Math.Clamp((box.Y - box.Height / 2) / (height - box.Height), 0, 1);
            var zoomScale = 1 + (camera.ScaleDuringZoom - 1) * Math.Clamp((screenZoomScale - 1) / .565, 0, 1);
            if (resize) camera.Size = box.Height / Math.Min(width, height) / zoomScale;
        }
        else
        {
            camera.CenterX = box.X / width; camera.CenterY = box.Y / height;
            if (resize) camera.Size = box.Width / width;
        }
    }

    public static OverlayBox Sticker(int canvasWidth, int canvasHeight, StickerImage sticker, double aspect)
    {
        var w = Math.Max(4, (int)Math.Round(sticker.Width * canvasWidth)) & ~1;
        var h = Math.Max(4, (int)Math.Round(w / Math.Max(.1, aspect))) & ~1;
        return new OverlayBox(sticker.X * canvasWidth, sticker.Y * canvasHeight, w, h, sticker.Rotation).Fit(canvasWidth, canvasHeight);
    }

    /// <summary>开场动画时长（秒）。</summary>
    public const double IntroDuration = 0.6;

    /// <summary>开场进度 0→1；none 或无动画时返回 1（已完成）。</summary>
    public static double IntroProgress(CanvasSettings canvas, double time) =>
        canvas.Intro is "none" || string.IsNullOrWhiteSpace(canvas.Intro)
            ? 1
            : Math.Clamp(time / IntroDuration, 0, 1);

    /// <summary>Global Opening 某个元素的进场进度 0..1：元素延迟 = index*OpeningInterval，
    /// 动画占时 = OpeningDuration-delay；smooth/clear/gentle 三套缓动（对齐 Mac Motion Timing）。</summary>
    public static double OpeningProgress(CanvasSettings canvas, double time, int elementIndex)
    {
        if (canvas.Opening == "none" || time < 0) return 1;
        var delay = elementIndex * canvas.OpeningInterval;
        var anim = Math.Max(0.2, canvas.OpeningDuration - delay);
        var t = Math.Clamp((time - delay) / anim, 0, 1);
        return canvas.OpeningTiming switch
        {
            "clear" => 1 - (1 - t) * (1 - t),
            "gentle" => 1 - (1 - t) * (1 - t) * (1 - t),
            _ => t * t * (3 - 2 * t),
        };
    }

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

        var (enter, exit) = ZoomMotion.Windows(zoom);
        var local = time - zoom.Start + zoom.AnimationOffset;
        var duration = zoom.AnimationDuration ?? zoom.Duration;
        var into = enter <= .000001 ? (local >= 0 ? 1 : 0) : Math.Clamp(local / enter, 0, 1);
        var outOf = exit <= .000001 ? (local <= duration ? 1 : 0) : Math.Clamp((duration - local) / exit, 0, 1);
        into = ZoomMotion.Progress(zoom, into);
        outOf = 1 - ZoomMotion.Progress(zoom, 1 - outOf);
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
        scale = Math.Clamp(scale, 1, 6);
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
