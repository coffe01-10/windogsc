using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DogSC.Core;

namespace DogSC;

/// <summary>
/// 屏幕外框 chrome 烘焙：按 ChromeSpec 在画布大小上画透明底 PNG，
/// 叠在 placed（卡片已上透明画布）之后、屏幕运动透视之前，跟着运镜一起变。
/// </summary>
public static class ChromeArt
{
    public static string Bake(ChromeSpec spec, int canvasW, int canvasH, string output)
    {
        var bitmap = Preview(spec, canvasW, canvasH);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var stream = File.Create(output); encoder.Save(stream); return output;
    }

    public static BitmapSource Preview(ChromeSpec spec, int canvasW, int canvasH)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var surface = BrushOf(spec.Surface);
            var toolbar = BrushOf(spec.Toolbar);
            var separator = BrushOf(spec.Separator);
            var glyph = BrushOf(spec.Glyph);
            var field = BrushOf(spec.Field);

            var outer = new Rect(spec.OuterX, spec.OuterY, spec.OuterW, spec.OuterH);
            var outerRR = new RectangleGeometry(outer, spec.OuterRadius, spec.OuterRadius);
            // 内容矩形：窗口/浏览器顶边与顶栏齐平、占满宽（insets 只有 top）。
            var contentTop = spec.IsDevice ? spec.OuterY + spec.ToolbarH : spec.OuterY + spec.ToolbarH;
            var content = new Rect(
                spec.IsDevice ? spec.OuterX + spec.ToolbarH : spec.OuterX,
                contentTop,
                spec.IsDevice ? spec.OuterW - spec.ToolbarH * 2 : spec.OuterW,
                spec.IsDevice ? spec.OuterH - spec.ToolbarH * 2 : spec.OuterH - spec.ToolbarH);
            var innerRR = new RectangleGeometry(content, spec.IsDevice ? spec.ContentRadius : spec.ContentRadius, spec.IsDevice ? spec.ContentRadius : spec.ContentRadius);
            // 外壳环 = 外圆角矩形 - 内容矩形 → 只剩顶栏带 / bezel 四边。
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude, outerRR, innerRR);
            dc.DrawGeometry(surface, null, ring);

            if (!spec.IsDevice)
            {
                // 顶栏带 = 外框圆角矩形 ∩ 顶部矩形，顶角圆、底边直。
                var band = new CombinedGeometry(GeometryCombineMode.Intersect, outerRR,
                    new RectangleGeometry(new Rect(spec.OuterX, spec.OuterY, spec.OuterW, spec.ToolbarH)));
                dc.DrawGeometry(toolbar, null, band);
                dc.DrawRectangle(separator, null,
                    new Rect(spec.OuterX + 1, spec.OuterY + spec.ToolbarH - 1, spec.OuterW - 2, 1));

                // macOS 红绿灯，半径≈顶栏高 13%，间距 2.6r。
                var r = Math.Max(spec.ToolbarH * 0.13, 2.5);
                var cy = spec.OuterY + spec.ToolbarH * 0.46;
                var cx = spec.OuterX + spec.ToolbarH * 0.42;
                dc.DrawEllipse(BrushOf(0xFF5F57), null, new Point(cx, cy), r, r);
                dc.DrawEllipse(BrushOf(0xFEBC2E), null, new Point(cx + r * 2.6, cy), r, r);
                dc.DrawEllipse(BrushOf(0x28C840), null, new Point(cx + r * 5.2, cy), r, r);

                if (spec.IsBrowser)
                {
                    // 地址栏药丸居中，地址文字浅灰。
                    var pillH = spec.ToolbarH * 0.56;
                    var pillW = spec.OuterW * 0.42;
                    var pill = new Rect(spec.OuterX + (spec.OuterW - pillW) / 2,
                        spec.OuterY + (spec.ToolbarH - pillH) / 2, pillW, pillH);
                    dc.DrawGeometry(field, null, new RectangleGeometry(pill, pillH / 2, pillH / 2));
                    if (spec.Address.Length > 0)
                    {
                        var t = Text(spec.Address, Math.Max(6, pillH * 0.44), glyph);
                        dc.DrawText(t, new Point(pill.X + pillH * 0.42, pill.Y + (pillH - t.Height) / 2));
                    }
                }
                else if (spec.Title.Length > 0)
                {
                    var t = Text(spec.Title, Math.Max(6, spec.ToolbarH * 0.32), glyph);
                    dc.DrawText(t, new Point(spec.OuterX + (spec.OuterW - t.Width) / 2,
                        spec.OuterY + (spec.ToolbarH - t.Height) / 2));
                }
            }
            else
            {
                // 手机 home 指示条：竖版底部居中横条，横版右侧居中竖条。
                var bezel = spec.ToolbarH;
                var bar = Math.Min(bezel * 0.38, 6);
                if (spec.Kind.EndsWith("Portrait"))
                {
                    var bw = spec.OuterW * 0.26;
                    dc.DrawGeometry(glyph, null, new RectangleGeometry(
                        new Rect(spec.OuterX + (spec.OuterW - bw) / 2,
                            spec.OuterY + spec.OuterH - (bezel + bar) / 2, bw, bar), bar / 2, bar / 2));
                }
                else
                {
                    var bh = spec.OuterH * 0.26;
                    dc.DrawGeometry(glyph, null, new RectangleGeometry(
                        new Rect(spec.OuterX + spec.OuterW - (bezel + bar) / 2,
                            spec.OuterY + (spec.OuterH - bh) / 2, bar, bh), bar / 2, bar / 2));
                }
            }
        }

        var bmp = new RenderTargetBitmap(canvasW, canvasH, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze(); return bmp;
    }

    static FormattedText Text(string s, double size, Brush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, brush, 96);

    static SolidColorBrush BrushOf(uint rgb)
    {
        var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        b.Freeze();
        return b;
    }
}
