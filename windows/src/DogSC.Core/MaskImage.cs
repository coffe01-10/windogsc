namespace DogSC.Core;

public static class MaskImage
{
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

    public static void WriteBmp(string path, int width, int height, float radius, bool circle)
    {
        var pixels = CreateBgra(width, height, radius, circle);
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
