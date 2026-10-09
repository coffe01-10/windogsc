namespace DogSC.Core;

public static class MaskImage
{
    public static byte[] CreateProjectionMask(int width, int height, ProjectionCorner[] corners)
    {
        var pixels = new byte[width * height * 4];
        var order = new[] { 0, 1, 3, 2 };
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var distance = double.MaxValue;
            for (var edge = 0; edge < 4; edge++)
            {
                var a = corners[order[edge]];
                var b = corners[order[(edge + 1) % 4]];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                distance = Math.Min(distance, (dx * (y + .5 - a.Y) - dy * (x + .5 - a.X)) / Math.Sqrt(dx * dx + dy * dy));
            }
            var value = (byte)Math.Round(255 * Math.Clamp(distance + .5, 0, 1));
            var i = (y * width + x) * 4;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }
        return pixels;
    }
    public static byte[] CreateClickFrame(int size, double? progress, CursorFx fx, double zoom = 1)
    {
        var pixels = new byte[size * size * 4];
        if (progress is not { } p) return pixels;
        var color = Convert.ToInt32(ProjectFile.NormalizeColor(fx.ClickColor)[1..], 16);
        var radius = (14 + p * 30) * fx.ClickScale * zoom / 2;
        var center = size / 2d;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var distance = Math.Sqrt(Math.Pow(x + 0.5 - center, 2) + Math.Pow(y + 0.5 - center, 2));
            var coverage = Math.Clamp(1.5 * fx.ClickScale * zoom - Math.Abs(distance - radius), 0, 1);
            var i = (y * size + x) * 4;
            pixels[i] = (byte)(color & 255);
            pixels[i + 1] = (byte)((color >> 8) & 255);
            pixels[i + 2] = (byte)((color >> 16) & 255);
            pixels[i + 3] = (byte)Math.Round(255 * coverage * (1 - p * 0.85) * fx.ClickOpacity);
        }
        return pixels;
    }

    public static byte[] CreateBgra(int width, int height, float radius, bool circle)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var bytes = new byte[width * height * 4];
        var centerX = (width - 1) / 2f;
        var centerY = (height - 1) / 2f;
        var limit = circle ? MathF.Min(width, height) / 2f : radius;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float alpha;
                if (circle)
                {
                    var distance = MathF.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                    alpha = Math.Clamp(limit - distance + 0.75f, 0, 1);
                }
                else if (radius <= 0.5f)
                {
                    alpha = 1;
                }
                else
                {
                    var dx = MathF.Max(MathF.Abs(x - centerX) - (centerX - radius), 0);
                    var dy = MathF.Max(MathF.Abs(y - centerY) - (centerY - radius), 0);
                    var distance = MathF.Sqrt(dx * dx + dy * dy);
                    alpha = Math.Clamp(radius - distance + 0.75f, 0, 1);
                }

                var index = (y * width + x) * 4;
                bytes[index] = 255;
                bytes[index + 1] = 255;
                bytes[index + 2] = 255;
                bytes[index + 3] = (byte)Math.Round(alpha * 255);
            }
        }

        return bytes;
    }

    /// <summary>
    /// 径向渐变灰度图（Bgra 四通道同值）：中心 0、边缘 255。
    /// maskedmerge 用它把边缘混合成模糊层，实现移轴虚化。
    /// </summary>
    public static byte[] CreateRadial(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var bytes = new byte[width * height * 4];
        var centerX = (width - 1) / 2f;
        var centerY = (height - 1) / 2f;
        var maxDistance = MathF.Sqrt(centerX * centerX + centerY * centerY);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var distance = MathF.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY)) / maxDistance;
                // 中心 60% 半径内保持清晰，之后平滑过渡到全糊。
                var level = Math.Clamp((distance - 0.55f) / 0.45f, 0, 1);
                var value = (byte)Math.Round(level * level * 255);
                var index = (y * width + x) * 4;
                bytes[index] = value;
                bytes[index + 1] = value;
                bytes[index + 2] = value;
                bytes[index + 3] = 255;
            }
        }

        return bytes;
    }

    /// <summary>
    /// 导出用的光标贴图：pointer=箭头，dot=圆点。Bgra 白身黑边。
    /// </summary>
    public static byte[] CreateCursor(int size, bool dot)
    {
        size = Math.Max(8, size);
        var height = dot ? size : size * 3 / 2;
        var bytes = new byte[size * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var index = (y * size + x) * 4;
                bool inside;
                bool edge;
                if (dot)
                {
                    var dx = x - size / 2f;
                    var dy = y - size / 2f;
                    var distance = MathF.Sqrt(dx * dx + dy * dy);
                    inside = distance <= size / 2f;
                    edge = distance > size / 2f - 1.6f && inside;
                }
                else
                {
                    // 简单箭头：左竖边 + 斜边 + 凹口。
                    inside = x <= y * 0.62 && x <= size - 1 - (y > size ? (y - size) * 1.4f : 0);
                    edge = inside && (x <= 1.4f || x >= y * 0.62 - 1.4f || y <= 1.4f);
                }

                if (!inside)
                {
                    continue;
                }

                var value = (byte)(edge ? 0 : 255);
                bytes[index] = value;
                bytes[index + 1] = value;
                bytes[index + 2] = value;
                bytes[index + 3] = 255;
            }
        }

        return bytes;
    }

    /// <summary>
    /// 局部柔化遮罩（灰度 Bgra）：矩形内 255（取模糊层），边缘 feather 像素
    /// 线性过渡，矩形外 0（保留清晰层）。尺寸 = 画布尺寸，rect = 像素矩形。
    /// </summary>
    public static byte[] CreateRegionMask(int width, int height, SoftRegion region)
    {
        var bytes = new byte[width * height * 4];
        var rw = region.Width * width;
        var rh = region.Height * height;
        var radius = Math.Min(rw, rh) * region.Corner;
        var cx = (region.X + region.Width / 2) * width;
        var cy = (region.Y + region.Height / 2) * height;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var dx = Math.Abs(x + .5 - cx) - rw / 2 + radius;
            var dy = Math.Abs(y + .5 - cy) - rh / 2 + radius;
            var distance = Math.Sqrt(Math.Pow(Math.Max(dx, 0), 2) + Math.Pow(Math.Max(dy, 0), 2))
                + Math.Min(Math.Max(dx, dy), 0) - radius;
            var level = (byte)Math.Round(Math.Clamp(.5 - distance, 0, 1) * 255);
            var index = (y * width + x) * 4;
            bytes[index] = bytes[index + 1] = bytes[index + 2] = level;
            bytes[index + 3] = 255;
        }
        return bytes;
    }

    public static byte[] CreateRectMask(int width, int height, int rx, int ry, int rw, int rh, int feather)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        feather = Math.Max(2, feather);
        var bytes = new byte[width * height * 4];
        var x0 = Math.Clamp(rx, 0, width);
        var y0 = Math.Clamp(ry, 0, height);
        var x1 = Math.Clamp(rx + rw, 0, width);
        var y1 = Math.Clamp(ry + rh, 0, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // 到矩形边界的向内距离；负值 = 在矩形外。
                var dx = Math.Min(x - x0, x1 - 1 - x);
                var dy = Math.Min(y - y0, y1 - 1 - y);
                var inside = Math.Min(dx, dy);
                var level = inside >= feather ? 255
                    : inside <= 0 ? 0
                    : (int)Math.Round(inside / (double)feather * 255);
                var index = (y * width + x) * 4;
                bytes[index] = (byte)level;
                bytes[index + 1] = (byte)level;
                bytes[index + 2] = (byte)level;
                bytes[index + 3] = 255;
            }
        }

        return bytes;
    }

    /// <summary>点击反馈圈：细圆环。</summary>
    public static byte[] CreateRing(int size)
    {
        size = Math.Max(8, size);
        var bytes = new byte[size * size * 4];
        var center = (size - 1) / 2f;
        var outer = size / 2f;
        var inner = outer - Math.Max(2, size * 0.14f);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance < inner || distance > outer)
                {
                    continue;
                }

                var index = (y * size + x) * 4;
                bytes[index] = 255;
                bytes[index + 1] = 255;
                bytes[index + 2] = 255;
                bytes[index + 3] = 220;
            }
        }

        return bytes;
    }

    public static void WriteBmp(string path, byte[] pixels, int width, int height) =>
        WriteBmpPixels(path, pixels, width, height);

    public static void WriteRadialBmp(string path, int width, int height) =>
        WriteBmpPixels(path, CreateRadial(width, height), width, height);

    public static void WriteBmp(string path, int width, int height, float radius, bool circle)
    {
        var pixels = CreateBgra(width, height, radius, circle);
        WriteBmpPixels(path, pixels, width, height);
    }

    static void WriteBmpPixels(string path, byte[] pixels, int width, int height)
    {
        var stride = width * 4;
        var pixelBytes = stride * height;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(54 + pixelBytes);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        for (var y = height - 1; y >= 0; y--)
        {
            writer.Write(pixels, y * stride, stride);
        }
    }
}
