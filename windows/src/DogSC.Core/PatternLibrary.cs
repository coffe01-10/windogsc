namespace DogSC.Core;

/// <summary>
/// Mac 程序生成背景：pattern 静态平铺（网格/点阵/等角线）与 dynamicFlow 逐帧动态层。
/// 参数全部取自 SharedFrameRenderer.swift：调色板、短边比例瓦片、漂移与相位。
/// 烘焙/叠加：底层烘成 PNG 当壁纸输入，层烘成带透明通道 PNG 用 overlay eval=frame 逐帧位移。
/// </summary>
public static class PatternLibrary
{
    public static bool IsProcedural(string? wallpaper) =>
        wallpaper is not null
            && (wallpaper.StartsWith("pattern:", StringComparison.Ordinal)
                || wallpaper.StartsWith("flow:", StringComparison.Ordinal));

    /// <summary>单层规格：PngW/H 是烘出来的 PNG 尺寸（可大于画布、负偏移），X/Y 是 overlay 逐帧表达式。</summary>
    public sealed record WallpaperLayer(
        int PngW,
        int PngH,
        string Kind,
        uint ColorA,
        uint ColorB,
        double P1,
        double P2,
        string X,
        string Y);

    /// <summary>整组烘焙单：Base 为壁纸输入 PNG（solid 或 linear 渐变），Layers 依序叠加。</summary>
    public sealed record WallpaperBake(
        string BaseKind,
        uint BaseColorA,
        uint BaseColorB,
        List<WallpaperLayer> Layers)
    {
        public bool Animated => Layers.Count > 0;
    }

    /// <summary>id 取自 Mac 枚举 case 名（importer 负责把中文 rawValue 映射过来）。</summary>
    public static WallpaperBake? Resolve(string? wallpaper, int canvasW, int canvasH, double scale, double opacity)
    {
        if (!IsProcedural(wallpaper))
        {
            return null;
        }

        var spec = wallpaper!;
        var id = spec[(spec.IndexOf(':') + 1)..];
        scale = Math.Clamp(scale, 0.4, 6);
        opacity = Math.Clamp(opacity, 0, 1);
        var shortEdge = Math.Max(Math.Min(canvasW, canvasH), 2);
        if (spec.StartsWith("pattern:", StringComparison.Ordinal))
        {
            return ResolvePattern(id, canvasW, canvasH, shortEdge, scale, opacity);
        }

        return ResolveFlow(id, canvasW, canvasH, shortEdge, scale, opacity);
    }

    private static double Tile(string id, int shortEdge, double scale) => id switch
    {
        "obsidianGrid" => Math.Max(shortEdge * 0.0425 * scale, 9),
        "engineeringWhiteGrid" => Math.Max(shortEdge * 0.040 * scale, 9),
        "midnightDots" => Math.Max(shortEdge * 0.032 * scale, 9),
        "architecturalDots" => Math.Max(shortEdge * 0.030 * scale, 9),
        "isometricMesh" => Math.Max(shortEdge * 0.050 * scale, 9),
        _ => Math.Max(shortEdge * 0.04 * scale, 9),
    };

    private static WallpaperBake? ResolvePattern(
        string id, int canvasW, int canvasH, int shortEdge, double scale, double opacity)
    {
        var (bg, stroke, dot, diagonal, kind) = id switch
        {
            "obsidianGrid" => (0xFF0F0F0Fu, (uint?)0xFF3D3D3Du, (uint?)null, false, "grid"),
            "engineeringWhiteGrid" => (0xFFF7F7F7u, (uint?)0xFFC9C9C9u, (uint?)null, false, "grid"),
            "midnightDots" => (0xFF111111u, (uint?)null, (uint?)0xE6C2AD8Au, false, "dots"),
            "architecturalDots" => (0xFFF9F9F9u, (uint?)null, (uint?)0xBF949494u, false, "dots"),
            "isometricMesh" => (0xFF131313u, (uint?)0xFF4F4A42u, (uint?)null, true, "grid"),
            _ => ((uint)0, null, null, false, ""),
        };
        if (kind.Length == 0)
        {
            return null;
        }

        var tile = Tile(id, shortEdge, scale);
        // 层 PNG 就是画布大小、内部按 tile 平铺；stroke 走 ColorA、dot 走 ColorB，P1=tile P2=opacity。
        var layer = new WallpaperLayer(canvasW, canvasH, diagonal ? "diaggrid" : kind,
            stroke ?? 0, dot ?? 0, tile, opacity, "0", "0");
        return new WallpaperBake("solid", bg, bg, [layer]);
    }

    private static WallpaperBake? ResolveFlow(
        string id, int canvasW, int canvasH, int shortEdge, double scale, double opacity)
    {
        switch (id)
        {
            case "cyberDriftGrid":
            {
                // 暖色径向辉光垫底下（±8% 摆动），obsidian 网格在上按 0.10/0.065 tile/s 斜漂。
                var tile = Tile("obsidianGrid", shortEdge, scale);
                var t = F(tile);
                var gw = (int)Math.Ceiling(canvasW * 1.16);
                var gh = (int)Math.Ceiling(canvasH * 1.16);
                var layers = new List<WallpaperLayer>
                {
                    new(gw, gh, "glow",
                        Alpha(0.85 * opacity, 0x4F381C), 0xFF0C0B0A,
                        Math.Max(canvasW, canvasH) * 0.05, Math.Max(canvasW, canvasH) * 0.70,
                        "(sin(t*0.4)-1)*" + F(canvasW * 0.08),
                        "(cos(t*0.4)-1)*" + F(canvasH * 0.08)),
                    new(canvasW + 2 * (int)Math.Ceiling(tile), canvasH + 2 * (int)Math.Ceiling(tile),
                        "grid", 0xFF3D3D3D, 0, tile, opacity,
                        "mod(t*" + F(tile * 0.10) + "," + t + ")-" + t,
                        "mod(t*" + F(tile * 0.065) + "," + t + ")-" + t),
                };
                return new WallpaperBake("solid", 0xFF0F0F0F, 0xFF0F0F0F, layers);
            }
            case "starfieldDots":
            {
                var tile = Tile("midnightDots", shortEdge, scale);
                var t = F(tile);
                return new WallpaperBake("solid", 0xFF111111, 0xFF111111,
                [
                    new(canvasW + 2 * (int)Math.Ceiling(tile), canvasH + 2 * (int)Math.Ceiling(tile),
                        "dots", 0, 0xE6C2AD8A, tile, opacity,
                        "mod(t*" + F(tile * 0.07) + "," + t + ")-" + t,
                        "mod(t*" + F(tile * 0.04) + "," + t + ")-" + t),
                ]);
            }
            case "auroraFluid":
            {
                // 蓝底线性渐变 + 三块高斯光斑按相位漂移（±0.36W / ±0.34H）
                var layers = new List<WallpaperLayer>();
                var phases = new (double Fx, double Fy, double R, uint Color)[]
                {
                    (0.19, 0.27, 0.56, Alpha(0.82 * opacity, 0x146BF5)),
                    (0.14, 0.22, 0.42, Alpha(0.62 * opacity, 0x14D4F5)),
                    (0.11, 0.17, 0.32, Alpha(0.70 * opacity, 0xE6F7FF)),
                };
                for (var i = 0; i < phases.Length; i++)
                {
                    var (fx, fy, r, color) = phases[i];
                    var radius = shortEdge * r * scale;
                    var offset = i * 2.18;
                    // PNG 要覆盖画布加上平移半径：宽加 2*0.36W，高加 2*0.34H
                    var pw = (int)Math.Ceiling(canvasW * 1.72);
                    var ph = (int)Math.Ceiling(canvasH * 1.68);
                    layers.Add(new WallpaperLayer(pw, ph, "blob", color, 0,
                        radius, offset,
                        "(cos(t*" + F(fx) + "+" + F(offset) + ")-1)*" + F(canvasW * 0.36),
                        "(sin(t*" + F(fy) + "+" + F(offset * 0.73) + ")-1)*" + F(canvasH * 0.34)));
                }

                return new WallpaperBake("linear", 0xFF061633, 0xFF040713, layers);
            }
            default:
                return null;
        }
    }

    private static uint Alpha(double a, uint rgb) => ((uint)Math.Clamp(Math.Round(a * 255), 0, 255) << 24) | (rgb & 0xFFFFFF);

    private static string F(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
