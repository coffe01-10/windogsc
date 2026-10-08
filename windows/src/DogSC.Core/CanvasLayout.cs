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
        var (aspectWidth, aspectHeight) = aspect switch
        {
            "16:9" => (16, 9),
            "4:3" => (4, 3),
            "1:1" => (1, 1),
            "9:16" => (9, 16),
            "3:4" => (3, 4),
            _ => (sourceWidth, sourceHeight),
        };

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

    public static BubblePlacement Bubble(int canvasWidth, int canvasHeight, CameraBubble camera)
    {
        var size = Even((int)Math.Round(canvasWidth * Math.Clamp(camera.Size, 0.08, 0.46)));
        var x = (int)Math.Round(Math.Clamp(camera.CenterX, 0, 1) * canvasWidth - size / 2.0);
        var y = (int)Math.Round(Math.Clamp(camera.CenterY, 0, 1) * canvasHeight - size / 2.0);
        return new BubblePlacement(x, y, size);
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
