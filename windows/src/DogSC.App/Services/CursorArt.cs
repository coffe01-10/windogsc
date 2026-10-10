using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DogSC.Core;

namespace DogSC;

/// <summary>光标贴图：BGRA 画布 + 热点（导出与预览共用，需在 UI 线程调用）。</summary>
public sealed record CursorSprite(byte[] Bgra, int Width, int Height, double HotspotX, double HotspotY);

public static class CursorArt
{
    static readonly Dictionary<(string, int), CursorSprite> Cache = new();

    /// <summary>样式 id + 基准像素高度 → 光标贴图（未旋转）。unknown/hidden 返回 null。</summary>
    public static CursorSprite? Render(string assetId, int referenceHeight)
    {
        var key = (assetId, referenceHeight);
        if (Cache.TryGetValue(key, out var sprite)) return sprite;
        sprite = RenderUncached(assetId, Math.Max(8, referenceHeight));
        if (sprite is not null) Cache[key] = sprite;
        return sprite;
    }

    /// <summary>按弧度绕热点旋转贴图，输出足以容纳旋转后包围盒的画布。</summary>
    public static CursorSprite Rotate(CursorSprite sprite, double radians)
    {
        if (Math.Abs(radians) < 0.002) return sprite;
        var w = sprite.Width;
        var h = sprite.Height;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var corners = new (double X, double Y)[] { (0, 0), (w, 0), (0, h), (w, h) }
            .Select(c => ((c.X - sprite.HotspotX) * cos - (c.Y - sprite.HotspotY) * sin,
                          (c.X - sprite.HotspotX) * sin + (c.Y - sprite.HotspotY) * cos)).ToArray();
        var minX = corners.Min(c => c.Item1);
        var minY = corners.Min(c => c.Item2);
        var outW = Math.Max(1, (int)Math.Ceiling(corners.Max(c => c.Item1) - minX));
        var outH = Math.Max(1, (int)Math.Ceiling(corners.Max(c => c.Item2) - minY));
        var outHotX = -minX;
        var outHotY = -minY;
        var output = new byte[outW * outH * 4];
        for (var y = 0; y < outH; y++)
        for (var x = 0; x < outW; x++)
        {
            // 逆映射回源图（双线性）。
            var dx = x - outHotX;
            var dy = y - outHotY;
            var sx = dx * cos + dy * sin + sprite.HotspotX;
            var sy = -dx * sin + dy * cos + sprite.HotspotY;
            var x0 = (int)Math.Floor(sx);
            var y0 = (int)Math.Floor(sy);
            var fx = sx - x0;
            var fy = sy - y0;
            var di = (y * outW + x) * 4;
            for (var ch = 0; ch < 4; ch++)
            {
                double v = 0;
                for (var j = 0; j <= 1; j++)
                for (var i = 0; i <= 1; i++)
                {
                    var xx = x0 + i;
                    var yy = y0 + j;
                    if (xx < 0 || xx >= w || yy < 0 || yy >= h) continue;
                    var weight = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
                    v += sprite.Bgra[(yy * w + xx) * 4 + ch] * weight;
                }
                output[di + ch] = (byte)Math.Round(v);
            }
        }
        return new CursorSprite(output, outW, outH, outHotX, outHotY);
    }

    static CursorSprite? RenderUncached(string assetId, int h)
    {
        switch (assetId)
        {
            case "system.touch-dot":
                return RenderTouchDot(h);
            case "system.arrow":
            case "system.automatic":
                return RenderArrow(h);
            case "system.hidden":
                return null;
        }
        // Windows 标准光标（手势/十字/I 型/禁止/移动）用系统贴图，得到原生清晰度。
        var cursor = SystemCursorHandle(assetId);
        if (cursor != IntPtr.Zero)
        {
            var native = RenderSystemCursor(cursor, h);
            if (native is not null && native.Bgra.Where((_, i) => i % 4 == 3).Any(a => a > 0)) return native;
            // A shared system cursor can be replaced by a blank bitmap (for
            // example while another app hides it). Keep the selected style visible.
            if (assetId == "system.pointing-hand") return RenderPointingHand(h);
        }
        return RenderArrow(h);
    }

    static IntPtr SystemCursorHandle(string assetId) => assetId switch
    {
        "system.pointing-hand" => LoadCursor(IntPtr.Zero, 32649), // IDC_HAND
        "system.crosshair" => LoadCursor(IntPtr.Zero, 32515),     // IDC_CROSS
        "system.ibeam" => LoadCursor(IntPtr.Zero, 32513),         // IDC_IBEAM
        "system.not-allowed" => LoadCursor(IntPtr.Zero, 32648),   // IDC_NO
        "system.open-hand" or "system.closed-hand" => LoadCursor(IntPtr.Zero, 32646), // IDC_SIZEALL
        _ => IntPtr.Zero,
    };

    static CursorSprite? RenderSystemCursor(IntPtr hCursor, int referenceHeight)
    {
        var icon = CopyIcon(hCursor);
        if (icon == IntPtr.Zero) return null;
        try
        {
            GetIconInfo(icon, out var info);
            var hotX = info.xHotspot;
            var hotY = info.yHotspot;
            try
            {
                if (info.hbmColor == IntPtr.Zero)
                {
                    // 单色光标（I 型 / 十字）：hbmColor 为 NULL，CreateBitmapSourceFromHIcon
                    // 产出全透明；改为从 hbmMask 上半 AND / 下半 XOR 重建 BGRA。
                    return RenderMonoCursor(info, referenceHeight, hotX, hotY);
                }
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                var scale = referenceHeight / (double)bgra.PixelHeight;
                var w = Math.Max(1, (int)Math.Round(bgra.PixelWidth * scale));
                var pixels = new byte[w * referenceHeight * 4];
                var resized = new TransformedBitmap(bgra, new ScaleTransform(scale, scale));
                resized.CopyPixels(pixels, w * 4, 0);
                return new CursorSprite(pixels, w, referenceHeight, hotX * scale, hotY * scale);
            }
            finally
            {
                if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
                if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    /// <summary>单色光标：mask 高 = 2×图标高，上半 AND（0=覆盖）、下半 XOR（1=白）。</summary>
    static CursorSprite? RenderMonoCursor(IconInfo info, int referenceHeight, int hotX, int hotY)
    {
        if (info.hbmMask == IntPtr.Zero) return null;
        if (GetObjectW(info.hbmMask, Marshal.SizeOf<Bitmap>(), out var bm) == 0) return null;
        var iconH = bm.bmHeight / 2;
        if (bm.bmBitsPixel != 1 || bm.bmWidth <= 0 || iconH <= 0) return null;
        var bits = new byte[bm.bmWidthBytes * bm.bmHeight];
        if (GetBitmapBits(info.hbmMask, bits.Length, bits) == 0) return null;
        bool Bit(int x, int y) => (bits[y * bm.bmWidthBytes + x / 8] >> (7 - x % 8) & 1) == 1;
        var src = new byte[bm.bmWidth * iconH * 4];
        for (var y = 0; y < iconH; y++)
        for (var x = 0; x < bm.bmWidth; x++)
        {
            if (Bit(x, y)) continue;                      // AND=1 → 透明（含反相区）
            var white = Bit(x, y + iconH);                // XOR=1 → 白身，否则黑身
            var i = (y * bm.bmWidth + x) * 4;
            src[i] = src[i + 1] = src[i + 2] = (byte)(white ? 255 : 0);
            src[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(bm.bmWidth, iconH, 96, 96, PixelFormats.Bgra32, null, src, bm.bmWidth * 4);
        var scale = referenceHeight / (double)iconH;
        var w = Math.Max(1, (int)Math.Round(bm.bmWidth * scale));
        var pixels = new byte[w * referenceHeight * 4];
        new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)).CopyPixels(pixels, w * 4, 0);
        return new CursorSprite(pixels, w, referenceHeight, hotX * scale, hotY * scale);
    }

    /// <summary>Mac 风格箭头：白身黑边的多边形，按 24×24 设计网格光栅化（4× 超采样）。</summary>
    static CursorSprite RenderArrow(int referenceHeight)
    {
        var design = new[]
        {
            new[] // 箭头轮廓（经典 mac 箭头近似）
            {
                (5.0, 2.0), (5.0, 18.6), (8.6, 15.1), (11.3, 22.3), (14.2, 20.9),
                (11.5, 13.7), (17.5, 13.7),
            },
        };
        return RenderPolygon(design, referenceHeight, 1.1, 5.0, 2.0);
    }

    static CursorSprite RenderPointingHand(int referenceHeight) => RenderPolygon(
        [ [(8, 22), (7, 18), (3, 12), (3.5, 10.5), (5, 10.8), (8, 14),
           (8, 3), (9, 2), (10, 3), (10, 10), (12, 9), (14, 10),
           (16, 10), (18, 12), (18, 18), (16, 22)] ], referenceHeight, 1.1, 9, 2);

    /// <summary>触控点：灰色半透明圆 + 白描边 + 轻微投影（Mac touchDot 规格）。</summary>
    static CursorSprite RenderTouchDot(int referenceHeight)
    {
        var size = referenceHeight;
        var pixels = new byte[size * size * 4];
        var c = (size - 1) / 2d;
        var r = size / 2d - 1.5;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = x + 0.5 - c;
            var dy = y + 0.5 - c;
            var d = Math.Sqrt(dx * dx + dy * dy);
            var i = (y * size + x) * 4;
            // 投影：圆内偏移下移 1px 的暗色衰减。
            var shadowD = Math.Sqrt(dx * dx + (dy - 1) * (dy - 1));
            if (shadowD <= r && d > r)
            {
                var a = Math.Clamp(r - shadowD + 1.5, 0, 1) * 0.35;
                pixels[i + 3] = (byte)Math.Round(a * 255);
            }
            if (d > r + 0.8) continue;
            double alpha;
            byte red, green, blue;
            var stroke = Math.Max(r * 0.09, 1.0);
            if (d > r - stroke)
            {
                red = 255; green = 255; blue = 255;
                alpha = 0.75;
            }
            else
            {
                red = 184; green = 184; blue = 184;
                alpha = 0.88;
            }
            var cov = Math.Clamp(r + 0.8 - d, 0, 1);
            var over = pixels[i + 3] / 255d;
            var blend = alpha * cov + over * (1 - alpha * cov);
            for (var ch = 0; ch < 3; ch++)
            {
                var s = ch == 0 ? blue : ch == 1 ? green : red;
                pixels[i + ch] = (byte)Math.Round(blend <= 0 ? 0
                    : (s * alpha * cov + pixels[i + ch] * over * (1 - alpha * cov)) / blend);
            }
            pixels[i + 3] = (byte)Math.Round(blend * 255);
        }
        return new CursorSprite(pixels, size, size, size / 2d, size / 2d);
    }

    /// <summary>多边形光栅化：白填充 + 黑描边，SDF 抗锯齿。</summary>
    static CursorSprite RenderPolygon((double X, double Y)[][] polys, int referenceHeight,
        double scaleToHeight, double hotspotX, double hotspotY)
    {
        // 设计网格 24；输出高度 = referenceHeight * scaleToHeight。
        var unit = referenceHeight * scaleToHeight / 24d;
        var all = polys.SelectMany(p => p).ToArray();
        var minX = all.Min(p => p.X) - 1.2;
        var minY = all.Min(p => p.Y) - 1.2;
        var w = Math.Max(1, (int)Math.Ceiling((all.Max(p => p.X) - minX + 1.2) * unit));
        var h = Math.Max(1, (int)Math.Ceiling((all.Max(p => p.Y) - minY + 1.2) * unit));
        var pixels = new byte[w * h * 4];
        var stroke = Math.Max(1.1, referenceHeight * 0.045) / unit;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var px = x / unit + minX;
            var py = y / unit + minY;
            var inside = false;
            var minDist = double.MaxValue;
            foreach (var poly in polys)
            {
                var odd = false;
                for (var i = 0; i < poly.Length; i++)
                {
                    var a = poly[i];
                    var b = poly[(i + 1) % poly.Length];
                    minDist = Math.Min(minDist, SegmentDistance(px, py, a.X, a.Y, b.X, b.Y));
                    if (a.Y > py != b.Y > py && px < (b.X - a.X) * (py - a.Y) / (b.Y - a.Y) + a.X) odd = !odd;
                }
                if (odd) inside = true;
            }
            var i4 = (y * w + x) * 4;
            if (inside)
            {
                var t = Math.Clamp(minDist - stroke + 0.5, 0, 1);
                var v = (byte)Math.Round(t * 255);
                pixels[i4] = pixels[i4 + 1] = pixels[i4 + 2] = v;
                pixels[i4 + 3] = 255;
            }
            else if (minDist <= stroke)
            {
                var a = Math.Clamp(stroke - minDist + 0.5, 0, 1);
                pixels[i4 + 3] = (byte)Math.Round(a * 255);
            }
        }
        return new CursorSprite(pixels, w, h, (hotspotX - minX) * unit, (hotspotY - minY) * unit);
    }

    static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var t = dx * dx + dy * dy < 1e-12 ? 0
            : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1);
        return Math.Sqrt(Math.Pow(px - ax - dx * t, 2) + Math.Pow(py - ay - dy * t, 2));
    }

    [DllImport("user32.dll")]
    static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
    [DllImport("user32.dll")]
    static extern IntPtr CopyIcon(IntPtr hIcon);
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")]
    static extern bool GetIconInfo(IntPtr hIcon, out IconInfo pIconInfo);
    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    static extern int GetObjectW(IntPtr h, int c, out Bitmap lpvObject);
    [DllImport("gdi32.dll")]
    static extern int GetBitmapBits(IntPtr hbit, int cb, byte[] lpvBits);

    [StructLayout(LayoutKind.Sequential)]
    public struct Bitmap
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }
}
