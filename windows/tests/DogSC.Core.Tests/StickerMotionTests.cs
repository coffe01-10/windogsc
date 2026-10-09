using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class StickerMotionTests
{
    static StickerImage MkSticker(double start, double duration, string animation = "pop",
        string? exit = null, double x = .5, double y = .5, double width = .38) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Timing = new EffectWindow { Start = start, Duration = duration },
            Animation = animation, ExitAnimation = exit, X = x, Y = y, Width = width,
        };

    [Fact]
    public void CurveEndpointsMatchMac()
    {
        foreach (var curve in new[] { "smooth", "swift", "gentle", "linear" })
        {
            Assert.Equal(0, StickerMotion.CurveProgress(0, curve), 6);
            Assert.Equal(1, StickerMotion.CurveProgress(1, curve), 6);
        }
        Assert.Equal(0.5, StickerMotion.CurveProgress(.5, "smooth"), 6);
        // swift = 1-(1-x)^4：中点 0.9375。
        Assert.Equal(0.9375, StickerMotion.CurveProgress(.5, "swift"), 6);
        // gentle = 1-(1-x)^3：中点 0.875。
        Assert.Equal(0.875, StickerMotion.CurveProgress(.5, "gentle"), 6);
    }

    [Fact]
    public void ComplementaryExitMatchesMac()
    {
        Assert.Equal("slideRight", StickerMotion.AutomaticExit("slideLeft"));
        Assert.Equal("slideLeft", StickerMotion.AutomaticExit("slideRight"));
        Assert.Equal("slideDown", StickerMotion.AutomaticExit("slideUp"));
        Assert.Equal("slideTopRight", StickerMotion.AutomaticExit("slideBottomLeft"));
        Assert.Equal("pop", StickerMotion.AutomaticExit("pop"));
        Assert.Equal("fade", StickerMotion.AutomaticExit("fade"));
        Assert.Equal("none", StickerMotion.AutomaticExit("none"));
        Assert.Equal("slideLeft", StickerMotion.ExitPreset(MkSticker(0, 1, "slideRight")));
        Assert.Equal("slideTopRight", StickerMotion.ExitPreset(MkSticker(0, 1, "none", "slideTopRight")));
    }

    [Fact]
    public void TransformSlideGoesOutside()
    {
        var (scale, ox, oy) = StickerMotion.Transform("slideLeft", 0, .5, .5, .38);
        Assert.True(ox <= -1.2 && ox > -2, $"offset {ox}");
        Assert.Equal(0, oy);
        Assert.Equal(1, scale);
        var end = StickerMotion.Transform("slideLeft", 1, .5, .5, .38);
        Assert.Equal(0, end.OffsetX, 6);
        var pop = StickerMotion.Transform("pop", 0, .5, .5, .38);
        Assert.Equal(.72, pop.Scale, 6);
    }

    [Fact]
    public void AdjacentStickersJoinKeepsVisible()
    {
        var a = MkSticker(0, 1, "slideLeft");          // 进场从左滑入 → 退出自动 slideRight？a 在左边
        a.ExitAnimation = "slideRight";
        var b = MkSticker(1, 1, "slideRight");         // b 的进入与 a 的退出同向 → join
        var track = new StickerMotion.Track(new List<StickerImage>{a, b});
        var (wa, we) = track.VisibleWindow(a);
        var (wb, wbe) = track.VisibleWindow(b);
        Assert.Equal(0, wa);
        // 交接：b 的可见窗口提前到 join 起点（b 在 a 退场过程中同步进入），a 延伸到 b 进场完成。
        Assert.True(we > 1 && wb < 1, $"window {we} {wb}");
        // 交接中：t=1 时刻 a 在退出、b 在进入，两者合起来覆盖画面。
        var (samples, _) = track.SampleAt(1.05);
        Assert.Equal(2, samples.Count);
    }

    [Fact]
    public void BackdropChannelsFollowFlags()
    {
        var s = MkSticker(0, 2);
        s.HidesScreen = true; s.HidesCamera = true;
        var track = new StickerMotion.Track(new List<StickerImage>{s});
        var (_, backdrop) = track.SampleAt(1);
        Assert.Equal(1, backdrop.ScreenSuppression, 3);
        Assert.Equal(1, backdrop.CameraSuppression, 3);
        // 虚化贴纸：BackdropBlur>0 → ScreenBlur；含摄像头 → CameraBlur。
        var blur = MkSticker(0, 2, "none");
        blur.BackdropBlur = 20; blur.BackdropBlurIncludesCamera = true;
        track = new StickerMotion.Track(new List<StickerImage>{blur});
        backdrop = track.SampleAt(1).Item2;
        Assert.True(backdrop.ScreenBlur > 0);
        Assert.True(backdrop.CameraBlur > 0);
        // 不含摄像头时 CameraBlur 为 0。
        blur.BackdropBlurIncludesCamera = false;
        track = new StickerMotion.Track(new List<StickerImage>{s});
        backdrop = track.SampleAt(1).Item2;
        Assert.Equal(0, backdrop.CameraBlur);
        // 未设标志的贴纸不给通道。
        var plain = MkSticker(0, 2);
        plain.BackdropBlur = 0;
        Assert.True(track == null || new StickerMotion.Track(new List<StickerImage>{plain}).SampleAt(1).Item2.IsZero);
    }

    [Fact]
    public void ExportGraphEmitsStickerFxAndBackdrop()
    {
        var asset = new MediaAsset { Video = "media/screen.mp4", Duration = 3, Width = 1920, Height = 1080 };
        var project = new ProjectDocument
        {
            Width = 1920, Height = 1080, Assets = { asset },
            Clips = { new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = 3 } },
        };
        var s = MkSticker(0.5, 1.5, "slideLeft");
        s.HidesScreen = true;
        var stickerPath = Path.Combine(Path.GetTempPath(), "dogsc-stk-" + Guid.NewGuid().ToString("N") + ".bmp");
        MaskImage.WriteBmp(stickerPath, 16, 8, 0, circle: false);
        s.Path = stickerPath;
        project.Canvas.Stickers.Add(s);
        var media = new ResolvedMedia
        {
            Videos = new Dictionary<string, string> { [asset.Id] = "v.mp4" },
            Microphones = new Dictionary<string, string>(),
            Systems = new Dictionary<string, string>(),
            Cameras = new Dictionary<string, string>(),
            Stickers = new Dictionary<string, ResolvedSticker> { [s.Id] = new(stickerPath, 1) },
        };
        var plan = ExportGraph.Build(project, new ExportOptions(), media);
        Assert.True(plan.StickerMatteIndex >= 0);
        Assert.Contains("extractplanes=r+g+b+a", plan.FilterScript);
        Assert.Contains("maskedmerge", plan.FilterScript);
        Assert.Contains("eval=frame", plan.FilterScript); // 动画贴纸走 spad 垫层
    }
}
