using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using DogSC.Core;

namespace DogSC;

/// <summary>
/// 贴图离线烘焙：圆角 / 边框 / 投影按 Mac StickerClip 规格画进一张带对称留边的
/// RGBA PNG，导出时让贴纸中心与画面中心对齐即可（留边对称，中心不偏移）。
/// </summary>
public static class StickerArt
{
    public sealed record Baked(string Path, double Aspect, double Margin);

    /// <summary>
    /// 把 source 烘焙成 contentW×contentH 的贴纸 PNG；四周留 margin，返回
    /// （路径, 内容宽高比, margin/内容宽）。
    /// </summary>
    public static Baked Bake(string sourcePath, StickerImage sticker, int contentW, int contentH, string directory)
    {
        var margin = (int)Math.Ceiling(sticker.ShadowRadius * 2.5
            + Math.Max(Math.Abs(sticker.ShadowOffsetX), Math.Abs(sticker.ShadowOffsetY))
            + Math.Max(sticker.BorderWidth, 0) + 2);
        var key = Hash(sourcePath, sticker, contentW, contentH, margin);
        var output = Path.Combine(directory, "sticker-" + key + ".png");
        var aspect = contentW / (double)Math.Max(1, contentH);
        var marginFrac = margin / (double)contentW;
        if (File.Exists(output)) return new Baked(output, aspect, marginFrac);

        var image = LoadPixels(sourcePath, contentW, contentH);
        var w = contentW + margin * 2;
        var h = contentH + margin * 2;
        var pixels = new byte[w * h * 4];

        var radius = Math.Max(0, sticker.CornerRadius);
        var shadowSigma = Math.Max(0, sticker.ShadowRadius);
        var shadowOpacity = Math.Clamp(sticker.ShadowOpacity, 0, 1);
        if (shadowSigma > 0.2 && shadowOpacity > 0.001)
        {
            // 阴影 = 内容形状的圆角矩形 → 三次盒式模糊近似高斯，再按偏移摆进画布。
            var shape = RoundedMask(contentW, contentH, radius);
            var blurred = BoxBlur(shape, contentW, contentH, shadowSigma);
            var ox = (int)Math.Round(sticker.ShadowOffsetX);
            var oy = (int)Math.Round(sticker.ShadowOffsetY);
            for (var y = 0; y < contentH; y++)
            for (var x = 0; x < contentW; x++)
            {
                var a = blurred[y * contentW + x] * shadowOpacity;
                if (a <= 0) continue;
                var px = x + margin + ox;
                var py = y + margin + oy;
                if (px < 0 || px >= w || py < 0 || py >= h) continue;
                Over(pixels, (py * w + px) * 4, 0, 0, 0, a);
            }
        }

        // 内容：圆角裁切 + 边框环。
        var borderW = Math.Max(0, sticker.BorderWidth);
        var (br, bg, bb) = ParseColor(sticker.BorderColor);
        var contentMask = RoundedMask(contentW, contentH, radius);
        var borderMask = borderW > 0.2
            ? Subtract(RoundedMask(contentW, contentH, radius),
                RoundedMask(contentW, contentH, Math.Max(0, radius - borderW), inset: borderW))
            : null;
        for (var y = 0; y < contentH; y++)
        for (var x = 0; x < contentW; x++)
        {
            var cov = contentMask[y * contentW + x];
            if (cov <= 0) continue;
            var i = (y * contentW + x) * 4;
            var dst = ((y + margin) * w + x + margin) * 4;
            Over(pixels, dst, image[i], image[i + 1], image[i + 2], cov * (image[i + 3] / 255.0));
            if (borderMask is not null)
            {
                var ring = borderMask[y * contentW + x];
                if (ring > 0) Over(pixels, dst, bb, bg, br, ring);
            }
        }

        Directory.CreateDirectory(directory);
        var frame = BitmapSource.Create(w, h, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, pixels, w * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using (var stream = File.Create(output)) encoder.Save(stream);
        return new Baked(output, aspect, marginFrac);
    }

    /// <summary>圆角矩形覆盖掩码 0..1（inset>0 表示边框内收缩）。</summary>
    static double[] RoundedMask(int w, int h, double radius, double inset = 0)
    {
        var mask = new double[w * h];
        var r = Math.Max(0.5, radius);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var px = x + 0.5 - inset;
            var py = y + 0.5 - inset;
            var iw = w - inset * 2;
            var ih = h - inset * 2;
            if (iw <= 0 || ih <= 0) continue;
            var qx = Math.Abs(px - iw / 2) - iw / 2 + r;
            var qy = Math.Abs(py - ih / 2) - ih / 2 + r;
            var d = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0))
                + Math.Min(Math.Max(qx, qy), 0) - r;
            mask[y * w + x] = Math.Clamp(0.5 - d, 0, 1);
        }
        return mask;
    }

    static double[] Subtract(double[] a, double[] b)
    {
        var r = new double[a.Length];
        for (var i = 0; i < a.Length; i++) r[i] = Math.Max(0, Math.Min(a[i], 1 - b[i]));
        return r;
    }

    /// <summary>三遍盒式模糊近似高斯。</summary>
    static double[] BoxBlur(double[] src, int w, int h, double sigma)
    {
        var radius = Math.Max(1, (int)Math.Round(sigma * 0.9));
        var current = src;
        for (var pass = 0; pass < 3; pass++) current = BoxPass(current, w, h, radius);
        return current;
    }

    static double[] BoxPass(double[] src, int w, int h, int r)
    {
        var tmp = new double[w * h];
        var dst = new double[w * h];
        for (var y = 0; y < h; y++)
        {
            double sum = 0;
            for (var x = -r; x <= r; x++) sum += src[y * w + Math.Clamp(x, 0, w - 1)];
            for (var x = 0; x < w; x++)
            {
                tmp[y * w + x] = sum / (2 * r + 1);
                sum += src[y * w + Math.Clamp(x + r + 1, 0, w - 1)] - src[y * w + Math.Clamp(x - r, 0, w - 1)];
            }
        }
        for (var x = 0; x < w; x++)
        {
            double sum = 0;
            for (var y = -r; y <= r; y++) sum += tmp[Math.Clamp(y, 0, h - 1) * w + x];
            for (var y = 0; y < h; y++)
            {
                dst[y * w + x] = sum / (2 * r + 1);
                sum += tmp[Math.Clamp(y + r + 1, 0, h - 1) * w + x] - tmp[Math.Clamp(y - r, 0, h - 1) * w + x];
            }
        }
        return dst;
    }

    static void Over(byte[] pixels, int i, byte b, byte g, byte r, double alpha)
    {
        if (alpha <= 0) return;
        var over = pixels[i + 3] / 255d;
        var blend = alpha + over * (1 - alpha);
        if (blend <= 0) return;
        pixels[i] = (byte)Math.Round((b * alpha + pixels[i] * over * (1 - alpha)) / blend);
        pixels[i + 1] = (byte)Math.Round((g * alpha + pixels[i + 1] * over * (1 - alpha)) / blend);
        pixels[i + 2] = (byte)Math.Round((r * alpha + pixels[i + 2] * over * (1 - alpha)) / blend);
        pixels[i + 3] = (byte)Math.Round(blend * 255);
    }

    static byte[] LoadPixels(string path, int w, int h)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        var frame = bitmap;
        var scaleX = w / (double)frame.PixelWidth;
        var scaleY = h / (double)frame.PixelHeight;
        var converted = new FormatConvertedBitmap(frame,
            System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var scaled = new TransformedBitmap(converted,
            new System.Windows.Media.ScaleTransform(scaleX, scaleY));
        var pixels = new byte[w * h * 4];
        scaled.CopyPixels(pixels, w * 4, 0);
        return pixels;
    }

    static (byte R, byte G, byte B) ParseColor(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length < 6) return (255, 255, 255);
        return ((byte)Convert.ToInt32(s[..2], 16),
            (byte)Convert.ToInt32(s[2..4], 16), (byte)Convert.ToInt32(s[4..6], 16));
    }

    static string Hash(string source, StickerImage s, int w, int h, int margin)
    {
        var stamp = File.Exists(source) ? File.GetLastWriteTimeUtc(source).Ticks : 0;
        var text = string.Join('|', source, stamp, w, h, margin,
            s.CornerRadius, s.BorderWidth, s.BorderColor,
            s.ShadowOpacity, s.ShadowRadius, s.ShadowOffsetX, s.ShadowOffsetY);
        return Convert.ToHexString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];
    }
}
