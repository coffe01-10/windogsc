using System.Globalization;

namespace DogSC.Core;

/// <summary>光标样式规格（对齐 macOS 版 CursorAssetLibrary）。</summary>
public sealed record CursorAsset(string Id, string Name, double HotspotX, double HotspotY,
    double Width = 1, double Height = 1.35, bool RecordedOnly = false);

/// <summary>一次点击图元：Ring=空心圆环(直径/线宽)，Fill=实心圆，Glow=径向渐变圆，Dot=四向点缀。</summary>
public readonly record struct ClickPrimitive(string Kind, double Diameter, double Alpha,
    double LineWidth = 0, double OffsetX = 0, double OffsetY = 0, double Size = 0);

public static class CursorCatalog
{
    /// <summary>样式目录：automatic 跟随录制到的真实光标，否则落到箭头。</summary>
    public static readonly CursorAsset[] Assets =
    [
        new("system.automatic", "自动", 0.25, 0.125),
        new("system.arrow", "箭头", 0.25, 0.125),
        new("system.pointing-hand", "手势", 0.36, 0.10),
        new("system.crosshair", "十字", 0.5, 0.5, 1, 1),
        new("system.ibeam", "I 型", 0.5, 0.5, 0.7, 1.35),
        new("system.open-hand", "开手", 0.5, 0.5, 1.1, 1.1),
        new("system.closed-hand", "合手", 0.5, 0.5, 1.1, 1.1),
        new("system.not-allowed", "禁止", 0.5, 0.5, 1, 1),
        new("system.touch-dot", "触控点", 0.5, 0.5, 0.55, 0.55),
        new("system.hidden", "隐藏", 0.5, 0.5),
    ];

    /// <summary>点击效果目录：键 → (名称, 时长秒)。与 Mac CursorClickEffectStyle 一致。</summary>
    public static readonly (string Id, string Name, double Duration)[] ClickStyles =
    [
        ("ripple", "涟漪扩散", 0.36),
        ("glow", "柔和光晕", 0.32),
        ("pulse", "弹性双环", 0.38),
        ("burst", "聚焦微闪", 0.22),
        ("none", "无效果", 0),
    ];

    public static readonly string[] MotionIds = ["smooth", "medium", "rapid", "none"];
    public static readonly (string Id, string Name)[] Motions =
        [("smooth", "平滑弹簧"), ("medium", "适中"), ("rapid", "快速"), ("none", "无")];

    public static double ClickDuration(string style) =>
        ClickStyles.FirstOrDefault(s => s.Id == style).Duration is { } d && d > 0 ? d : 0.36;

    public static CursorAsset? Find(string? id) => Assets.FirstOrDefault(a => a.Id == id);

    /// <summary>fx → 实际样式 id：显式 Asset 优先，否则按旧版 Mode 映射。</summary>
    public static string ResolveAsset(CursorFx fx)
    {
        if (fx.Asset is { Length: > 0 } id && Find(id) is { } known)
        {
            return known.Id;
        }
        return fx.Mode switch { "dot" => "system.touch-dot", "hidden" => "system.hidden", _ => "system.arrow" };
    }

    public static string ResolveClick(CursorFx fx) =>
        fx.ClickStyle is { Length: > 0 } s && ClickStyles.Any(c => c.Id == s) ? s
            : fx.ClickRipple ? "ripple" : "none";

    /// <summary>某个录制点的光标 id 不在目录内时回退到箭头。</summary>
    public static string NormalizeRecorded(string? id) => Find(id) is { } a && a.Id != "system.automatic" ? a.Id : "system.arrow";

    /// <summary>点击图元序列（移植 Mac clickGeometry 公式；p=0..1，H=基准直径，u=不透明度系数）。</summary>
    public static IEnumerable<ClickPrimitive> ClickGeometry(string style, double p, double H, double u)
    {
        switch (style)
        {
            case "ripple":
            {
                var e = 1 - Math.Pow(1 - p, 3);
                yield return new("ring", H * (0.45 + 1.45 * e), u * 0.95 * (1 - Math.Pow(p, 1.3)), Math.Max(H * (0.075 - 0.04 * p), 1.2));
                if (p < 0.35) yield return new("fill", H * 0.28 * (1 - p / 0.35), u * 0.95 * (1 - p / 0.35));
                break;
            }
            case "glow":
            {
                double d, a;
                if (p < 0.18)
                {
                    var q = p / 0.18;
                    d = H * (0.75 + 0.50 * (1 - (1 - q) * (1 - q)));
                    a = u * 0.78 * q;
                }
                else
                {
                    var q = (p - 0.18) / 0.82;
                    d = H * (1.25 + 0.55 * q);
                    a = u * 0.78 * (1 - Math.Pow(q, 1.4));
                }
                yield return new("glow", Math.Max(d, 1), a);
                yield return new("fill", Math.Max(d * 0.5, 1), a * 0.75);
                break;
            }
            case "pulse":
            {
                var e = 1 - Math.Pow(1 - p, 2.5);
                yield return new("ring", H * (0.50 + 1.25 * e), u * 0.90 * (1 - p), Math.Max(H * 0.065, 1.2));
                if (p <= 0.55) yield return new("fill", H * (0.25 + 0.45 * Math.Sin(Math.PI * p / 0.55)), u * 0.55);
                break;
            }
            case "burst":
            {
                yield return new("fill", Math.Max(H * (0.35 - 0.18 * p), 1), u * 0.95 * (1 - p * p));
                yield return new("ring", H * (0.45 + 0.55 * p), u * 0.55 * (1 - p), Math.Max(H * 0.035, 1));
                var offset = H * (0.28 + 0.38 * (1 - (1 - p) * (1 - p)));
                var size = Math.Max(H * 0.075 * (1 - p), 1.2);
                var alpha = u * 0.85 * (1 - p);
                foreach (var (ox, oy) in new[] { (0.0, -offset), (offset, 0.0), (0.0, offset), (-offset, 0.0) })
                    yield return new("dot", size, alpha, OffsetX: ox, OffsetY: oy, Size: size);
                break;
            }
        }
    }

    /// <summary>最大外径（决定点击画布尺寸）：各样式足迹的最大值。</summary>
    public static double ClickFootprint(string style, double H) => style switch
    {
        "ripple" => H * 1.9, "glow" => H * 1.8, "pulse" => H * 1.75, "burst" => H * 1.5, _ => H,
    };

    /// <summary>把点击层渲染成 BGRA 帧（画布正方形，元素以中心为原点）。</summary>
    public static byte[] RenderClick(int size, string style, double progress, uint color, double opacity, double H)
    {
        var pixels = new byte[size * size * 4];
        var cx = size / 2d;
        var cy = size / 2d;
        foreach (var prim in ClickGeometry(style, Math.Clamp(progress, 0, 1), H, 1))
        {
            var alphaBase = Math.Clamp(prim.Alpha * opacity, 0, 1);
            if (alphaBase <= 0) continue;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                double a;
                var dx = x + 0.5 - cx - prim.OffsetX;
                var dy = y + 0.5 - cy - prim.OffsetY;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                switch (prim.Kind)
                {
                    case "ring":
                    {
                        var d = Math.Abs(distance - prim.Diameter / 2);
                        a = Math.Clamp(prim.LineWidth / 2 + 0.5 - d, 0, 1) * alphaBase;
                        break;
                    }
                    case "glow":
                        a = Math.Clamp(1 - distance / (prim.Diameter / 2 + 0.001), 0, 1);
                        a = a * a * alphaBase;
                        break;
                    case "fill":
                    case "dot":
                        a = Math.Clamp(prim.Diameter / 2 + 0.5 - distance, 0, 1) * alphaBase;
                        break;
                    default:
                        a = 0;
                        break;
                }
                if (a <= 0) continue;
                var i = (y * size + x) * 4;
                var existing = pixels[i + 3] / 255d;
                var blend = a + existing * (1 - a);
                if (blend <= 0) continue;
                for (var ch = 0; ch < 3; ch++)
                {
                    var src = (byte)(color >> (8 * ch) & 255);
                    pixels[i + ch] = (byte)Math.Round((src * a + pixels[i + ch] * existing * (1 - a)) / blend);
                }
                pixels[i + 3] = (byte)Math.Round(blend * 255);
            }
        }
        return pixels;
    }

    /// <summary>#RRGGBB → 0x00RRGGBB（低字节为蓝色，对应 BGRA）。</summary>
    public static uint ParseColor(string hex)
    {
        var normalized = ProjectFile.NormalizeColor(hex);
        var rgb = Convert.ToUInt32(normalized[1..], 16);
        return rgb;
    }
}
