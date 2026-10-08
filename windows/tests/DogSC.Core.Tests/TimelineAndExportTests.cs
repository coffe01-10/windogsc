using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class TimelineAndExportTests
{
    [Fact]
    public void OutputSize_UsesShortEdgePresets()
    {
        Assert.Equal((1920, 1080), CanvasLayout.OutputSize(1280, 720, "16:9", "1080"));
        Assert.Equal((3840, 2160), CanvasLayout.OutputSize(1920, 1080, "16:9", "4k"));
        Assert.Equal((1080, 1920), CanvasLayout.OutputSize(1920, 1080, "9:16", "1080"));
        Assert.Equal((640, 360), CanvasLayout.OutputSize(640, 360, "source", "source"));
    }

    [Fact]
    public void Placement_CentersAPaddedFrame()
    {
        var placed = CanvasLayout.Place(1920, 1080, 1920, 1080, new CanvasSettings
        {
            Padding = 0,
            Scale = 1,
            CenterX = 0.5,
            CenterY = 0.5,
        });
        Assert.Equal(1920, placed.Width);
        Assert.Equal(1080, placed.Height);
        Assert.Equal(0, placed.X);
        Assert.Equal(0, placed.Y);
    }

    [Fact]
    public void SplitAndSpeed_KeepAContinuousTimeline()
    {
        var project = Sample(10);
        Assert.True(TimelineEdits.TrySplit(project, 4));
        Assert.Equal(2, project.Clips.Count);
        Assert.Equal(10d, TimelineMath.Duration(project), 2);
        Assert.True(TimelineEdits.TrySetSpeed(project, project.Clips[1].Id, 2));
        Assert.Equal(7d, TimelineMath.Duration(project), 2);
    }

    [Fact]
    public void DeleteClip_ShiftsLaterZooms()
    {
        var project = Sample(10);
        Assert.True(TimelineEdits.TrySplit(project, 4));
        project.Zooms.Add(new ZoomSpan { Start = 6, Duration = 2 });
        Assert.True(TimelineEdits.TryDeleteClip(project, project.Clips[0].Id));
        Assert.Equal(2d, project.Zooms[0].Start, 2);
        Assert.Equal(6d, TimelineMath.Duration(project), 2);
    }

    [Fact]
    public void Marker_StaysOnTheSourceFrameAfterAHeadTrim()
    {
        var project = Sample(10);
        var marker = TimelineEdits.AddMarker(project, 5, "口误");
        Assert.NotNull(marker);
        Assert.True(TimelineEdits.TryTrim(project, project.Clips[0].Id, 2, 10));
        Assert.Equal(3d, TimelineMath.MarkerTime(project, marker!)!.Value, 2);
    }

    [Fact]
    public void Zoom_DoesNotOverlap()
    {
        var project = Sample(8);
        var first = TimelineEdits.AddZoom(project, 1);
        Assert.NotNull(first);
        Assert.Null(TimelineEdits.AddZoom(project, 1.4));
        var second = TimelineEdits.AddZoom(project, 4);
        Assert.NotNull(second);
    }

    [Fact]
    public void Export_IncludesConcatCropAndCanvasSize()
    {
        var project = Sample(6);
        project.Width = 640;
        project.Height = 360;
        project.Canvas.Aspect = "16:9";
        project.Canvas.Padding = 40;
        project.Canvas.Corner = 24;
        project.Canvas.Shadow = 30;
        Assert.True(TimelineEdits.TrySplit(project, 3));
        TimelineEdits.AddZoom(project, 0.2);
        var plan = ExportGraph.Build(project, new ExportOptions { Resolution = "1080", Fps = 30 }, Media(project));
        Assert.Equal((1920, 1080), (plan.Width, plan.Height));
        Assert.Contains("concat=n=", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("zoompan=", plan.FilterScript, StringComparison.Ordinal);
        Assert.True(plan.FrameMaskIndex >= 0);
        Assert.Equal("vout", plan.VideoLabel);
        Assert.Null(plan.AudioLabel);
    }

    [Fact]
    public void Export_MixesMicrophoneAndSystemAudio()
    {
        var project = Sample(4);
        project.Canvas.Corner = 0;
        project.Canvas.Shadow = 0;
        project.Assets[0].Microphone = "media/mic.wav";
        project.Assets[0].SystemAudio = "media/system.wav";
        var media = new ResolvedMedia
        {
            Videos = new Dictionary<string, string> { [project.Assets[0].Id] = "C:/video.mp4" },
            Microphones = new Dictionary<string, string> { [project.Assets[0].Id] = "C:/mic.wav" },
            Systems = new Dictionary<string, string> { [project.Assets[0].Id] = "C:/system.wav" },
            Cameras = new Dictionary<string, string>(),
        };
        var plan = ExportGraph.Build(project, new ExportOptions(), media);
        Assert.Contains("amix=inputs=2", plan.FilterScript, StringComparison.Ordinal);
        Assert.Equal("a0", plan.AudioLabel);
        Assert.Equal(3, plan.Inputs.Count);
    }

    [Fact]
    public void Export_FillsSilenceAndSkipsCamera_ForClipsMissingTracks()
    {
        // 三段素材：中间一段既没有麦克风/系统声，也没有摄像头。
        var assetA = new MediaAsset { Video = "media/a.mp4", Duration = 4, Width = 640, Height = 360, Microphone = "media/a-mic.wav", Camera = "media/a-cam.mp4" };
        var assetB = new MediaAsset { Video = "media/b.mp4", Duration = 4, Width = 640, Height = 360 };
        var assetC = new MediaAsset { Video = "media/c.mp4", Duration = 4, Width = 640, Height = 360, Microphone = "media/c-mic.wav", Camera = "media/c-cam.mp4" };
        var project = new ProjectDocument
        {
            Name = "缺轨",
            Width = 640,
            Height = 360,
            Assets = [assetA, assetB, assetC],
            Clips =
            [
                new ClipSegment { AssetId = assetA.Id, SourceStart = 0, SourceEnd = 4 },
                new ClipSegment { AssetId = assetB.Id, SourceStart = 0, SourceEnd = 4 },
                new ClipSegment { AssetId = assetC.Id, SourceStart = 0, SourceEnd = 4 },
            ],
        };
        var media = new ResolvedMedia
        {
            Videos = project.Assets.ToDictionary(a => a.Id, a => "C:/" + a.Video),
            Microphones = new Dictionary<string, string>
            {
                [assetA.Id] = "C:/a-mic.wav",
                [assetC.Id] = "C:/c-mic.wav",
            },
            Systems = new Dictionary<string, string>(),
            Cameras = new Dictionary<string, string>
            {
                [assetA.Id] = "C:/a-cam.mp4",
                [assetC.Id] = "C:/c-cam.mp4",
            },
        };

        var plan = ExportGraph.Build(project, new ExportOptions(), media);

        // 整条时间线时长不变，音轨照常导出，中间片段用静音补齐。
        Assert.Equal(12d, plan.Duration, 2);
        Assert.Equal("aout", plan.AudioLabel);
        Assert.Contains("concat=n=3:v=0:a=1", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("anullsrc", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("atrim=0:4", plan.FilterScript, StringComparison.Ordinal);

        // 画中画仍然导出，但只叠加在两个有摄像头片段的时间窗里。
        Assert.Equal("vout", plan.VideoLabel);
        Assert.True(plan.CameraMaskIndex >= 0);
        Assert.Contains("enable='gte(t,0)*lt(t,4)'", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("enable='gte(t,8)*lt(t,12)'", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("alphamerge[camr0]", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("alphamerge[camr2]", plan.FilterScript, StringComparison.Ordinal);
        Assert.DoesNotContain("camr1]", plan.FilterScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_WithoutAnyCameraOrAudio_KeepsOldBehavior()
    {
        var asset = new MediaAsset { Video = "media/screen.mp4", Duration = 4, Width = 640, Height = 360 };
        var project = new ProjectDocument
        {
            Width = 640,
            Height = 360,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = 4 }],
        };
        var media = new ResolvedMedia
        {
            Videos = new Dictionary<string, string> { [asset.Id] = "C:/screen.mp4" },
            Microphones = new Dictionary<string, string>(),
            Systems = new Dictionary<string, string>(),
            Cameras = new Dictionary<string, string>(),
        };
        var plan = ExportGraph.Build(project, new ExportOptions(), media);
        Assert.Null(plan.AudioLabel);
        Assert.True(plan.CameraMaskIndex < 0);
        Assert.DoesNotContain("anullsrc", plan.FilterScript, StringComparison.Ordinal);
        Assert.DoesNotContain("alphamerge[cam", plan.FilterScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Snap_PicksTheNearestTargetWithinThreshold()
    {
        var targets = new[] { 0.0, 4.0, 8.0 };
        Assert.Equal(4.0, TimelineMath.Snap(4.05, targets, 0.1));
        Assert.Equal(0.0, TimelineMath.Snap(0.09, targets, 0.1));
        Assert.Equal(6.0, TimelineMath.Snap(6.0, targets, 0.1));
        Assert.Equal(3.9, TimelineMath.Snap(3.9, targets, 0.05));
    }

    [Fact]
    public void MoveClip_ReordersTheTimeline()
    {
        var project = Sample(10);
        TimelineEdits.TrySplit(project, 4);
        TimelineEdits.TrySplit(project, 7);
        Assert.Equal(3, project.Clips.Count);
        var first = project.Clips[0];
        var middle = project.Clips[1];
        Assert.True(TimelineEdits.TryMoveClip(project, first.Id, 2));
        Assert.Equal(middle.Id, project.Clips[0].Id);
        Assert.Equal(first.Id, project.Clips[2].Id);
        Assert.False(TimelineEdits.TryMoveClip(project, first.Id, 2));
        Assert.False(TimelineEdits.TryMoveClip(project, "不存在", 0));
    }

    [Fact]
    public void Trim_CanBeDraggedBackToRestoreTheCutPart()
    {
        var project = Sample(10);
        var clip = project.Clips[0];
        // 裁掉头部 2 秒和尾部 2 秒。
        Assert.True(TimelineEdits.TryTrim(project, clip.Id, 2, 8));
        Assert.Equal(6d, TimelineMath.Duration(project), 2);
        // 再拖回边界：裁掉的部分完整还原，不丢数据。
        Assert.True(TimelineEdits.TryTrim(project, clip.Id, 0, 10));
        Assert.Equal(10d, TimelineMath.Duration(project), 2);
        Assert.Equal(0, clip.SourceStart);
        Assert.Equal(10, clip.SourceEnd);
    }

    [Fact]
    public void ZoomRamp_EasesInAndOut()
    {
        var zoom = new ZoomSpan { Start = 2, Duration = 2, Scale = 2, FocusX = 0.8, FocusY = 0.2 };
        // 过渡时长 ease = min(0.4, 2/3) = 0.4
        Assert.Equal(0, CanvasLayout.ZoomRamp(zoom, 2));
        Assert.Equal(0.5, CanvasLayout.ZoomRamp(zoom, 2.2), 3);
        Assert.Equal(1, CanvasLayout.ZoomRamp(zoom, 3));
        Assert.Equal(0.25, CanvasLayout.ZoomRamp(zoom, 3.9), 3);
        Assert.Equal(0, CanvasLayout.ZoomRamp(zoom, 4));
        Assert.Equal(1.5, CanvasLayout.ZoomScaleAt(zoom, 2.2), 3);
        // ramp=0 时焦点回画面中心，ramp=1 时落在设定焦点。
        Assert.Equal(0.5, CanvasLayout.ZoomFocusAt(zoom, 2).X, 3);
        Assert.Equal(0.8, CanvasLayout.ZoomFocusAt(zoom, 3).X, 3);
        Assert.Equal(0.65, CanvasLayout.ZoomFocusAt(zoom, 2.2).X, 3);
    }

    [Fact]
    public void Export_ZoomUsesTimeVaryingCropExpression()
    {
        var project = Sample(6);
        project.Width = 640;
        project.Height = 360;
        var zoom = TimelineEdits.AddZoom(project, 0.5);
        Assert.NotNull(zoom);
        var plan = ExportGraph.Build(project, new ExportOptions(), Media(project));
        // 缩放段用 zoompan 按帧插值，不再是写死的 crop 常量。
        Assert.Contains("zoompan=z='1+", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("clip(", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("+time-", plan.FilterScript, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoZoom_CreatesSpansAtClickPoints()
    {
        var asset = new MediaAsset
        {
            Video = "media/screen.mp4",
            Duration = 10,
            Width = 640,
            Height = 360,
            Cursor =
            [
                new CursorPoint { Time = 1.0, X = 0.2, Y = 0.8, Click = false },
                new CursorPoint { Time = 2.0, X = 0.2, Y = 0.8, Click = true },
                new CursorPoint { Time = 2.2, X = 0.3, Y = 0.8, Click = true }, // 间隔太近被合并
                new CursorPoint { Time = 7.5, X = 0.9, Y = 0.1, Click = true },
            ],
        };
        var project = new ProjectDocument
        {
            Width = 640,
            Height = 360,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = 10 }],
        };

        var added = TimelineEdits.AutoZoom(project, scale: 1.6, lead: 0.35);
        Assert.Equal(2, added);
        Assert.Equal(2, project.Zooms.Count);
        var first = project.Zooms[0];
        Assert.Equal(2.0 - 0.35, first.Start, 2);
        Assert.Equal(1.6, first.Scale, 2);
        // 点击 (0.2, 0.8) → focus = (c - 0.5/scale) / (1 - 1/scale)，且被 clamp 在 0..1。
        Assert.Equal(0, first.FocusX, 3);
        Assert.True(first.FocusY > 0.5);
        Assert.Equal(7.5 - 0.35, project.Zooms[1].Start, 2);
    }

    [Fact]
    public void AutoZoom_IgnoresClicksOutsideClipsAndExistingZooms()
    {
        var asset = new MediaAsset
        {
            Video = "media/screen.mp4",
            Duration = 10,
            Width = 640,
            Height = 360,
            Cursor = [new CursorPoint { Time = 9.0, X = 0.5, Y = 0.5, Click = true }],
        };
        var project = new ProjectDocument
        {
            Width = 640,
            Height = 360,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = 5 }],
        };
        // 点击落在被裁掉的 source 区（5..10）→ 不生成。
        Assert.Equal(0, TimelineEdits.AutoZoom(project));
    }

    [Fact]
    public void Export_TiltAndFocusBlur_AddPerspectiveAndMaskedMerge()
    {
        var project = Sample(4);
        project.Width = 640;
        project.Height = 360;
        project.Canvas.TiltY = 18;
        project.Canvas.FocusBlur = 0.6;
        var plan = ExportGraph.Build(project, new ExportOptions(), Media(project));
        Assert.Contains("perspective=", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("gblur=sigma=", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("maskedmerge", plan.FilterScript, StringComparison.Ordinal);
        Assert.True(plan.FocusMaskIndex >= 0);

        // 关掉效果时回到原样。
        var plainProject = Sample(4);
        var plain = ExportGraph.Build(plainProject, new ExportOptions(), Media(plainProject));
        Assert.DoesNotContain("perspective=", plain.FilterScript, StringComparison.Ordinal);
        Assert.True(plain.FocusMaskIndex < 0);
    }

    [Fact]
    public void Normalize_ClampsTiltAndFocusBlur()
    {
        var project = Sample(2);
        project.Canvas.TiltX = 90;
        project.Canvas.TiltY = -90;
        project.Canvas.FocusBlur = 3;
        ProjectFile.Normalize(project);
        Assert.Equal(40, project.Canvas.TiltX);
        Assert.Equal(-40, project.Canvas.TiltY);
        Assert.Equal(1, project.Canvas.FocusBlur);
    }

    [Fact]
    public void Export_IntroPresets_AddTheMatchingFilters()
    {
        var fade = Sample(4);
        fade.Canvas.Intro = "fade";
        var fadePlan = ExportGraph.Build(fade, new ExportOptions(), Media(fade));
        Assert.Contains("fade=t=in:st=0:d=0.6:alpha=1", fadePlan.FilterScript, StringComparison.Ordinal);
        Assert.DoesNotContain("zoompan=z='0.9", fadePlan.FilterScript, StringComparison.Ordinal);

        var rise = Sample(4);
        rise.Canvas.Intro = "rise";
        var risePlan = ExportGraph.Build(rise, new ExportOptions(), Media(rise));
        Assert.Contains("fade=t=in", risePlan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("min(t/0.6\\,1)", risePlan.FilterScript, StringComparison.Ordinal);

        var zoom = Sample(4);
        zoom.Canvas.Intro = "zoom";
        var zoomPlan = ExportGraph.Build(zoom, new ExportOptions(), Media(zoom));
        Assert.Contains("zoompan=z='0.9+0.1*", zoomPlan.FilterScript, StringComparison.Ordinal);

        var none = Sample(4);
        var nonePlan = ExportGraph.Build(none, new ExportOptions(), Media(none));
        Assert.DoesNotContain("fade=t=in", nonePlan.FilterScript, StringComparison.Ordinal);
    }

    [Fact]
    public void IntroProgress_RampsThenStaysDone()
    {
        var canvas = new CanvasSettings { Intro = "fade" };
        Assert.Equal(0, CanvasLayout.IntroProgress(canvas, 0));
        Assert.Equal(0.5, CanvasLayout.IntroProgress(canvas, 0.3), 3);
        Assert.Equal(1, CanvasLayout.IntroProgress(canvas, 2));
        Assert.Equal(1, CanvasLayout.IntroProgress(new CanvasSettings { Intro = "none" }, 0));

        // 非法预设值回退 none。
        var project = Sample(1);
        project.Canvas.Intro = "bogus";
        ProjectFile.Normalize(project);
        Assert.Equal("none", project.Canvas.Intro);
    }

    [Fact]
    public void EffectCopyPaste_RoundTripsThroughJson()
    {
        var project = Sample(10);
        TimelineEdits.TrySplit(project, 5);
        var source = project.Clips[0];
        var target = project.Clips[1];
        TimelineEdits.TrySetSpeed(project, source.Id, 1.5);
        TimelineEdits.AddZoom(project, 1);
        project.Canvas.Background = "#1E1F22";
        project.Canvas.Corner = 40;
        project.MicrophoneGain = 1.8;

        var bundle = TimelineEdits.CopyEffect(project, source.Id);
        Assert.NotNull(bundle);
        Assert.Single(bundle!.Zooms);
        // JSON 序列化往返：字段不丢。
        var json = System.Text.Json.JsonSerializer.Serialize(bundle, ProjectSchema.Json);
        var restored = System.Text.Json.JsonSerializer.Deserialize<EffectBundle>(json, ProjectSchema.Json);
        Assert.NotNull(restored);
        Assert.Equal(bundle.Speed, restored!.Speed, 3);
        Assert.Equal(bundle.SourceLength, restored.SourceLength, 3);
        Assert.Equal(bundle.Zooms[0].Scale, restored.Zooms[0].Scale, 3);
        Assert.Equal("#1E1F22", restored.Canvas.Background);

        // 全量粘贴：目标片段的速度、时长与缩放段一起带走。
        Assert.True(TimelineEdits.PasteEffect(project, target.Id, restored, includeTiming: true));
        Assert.Equal(source.Speed, target.Speed, 3);
        Assert.Equal(source.SourceEnd - source.SourceStart, target.SourceEnd - target.SourceStart, 3);
        Assert.Equal(2, project.Zooms.Count);
        Assert.Equal("#1E1F22", project.Canvas.Background);
        Assert.Equal(1.8, project.MicrophoneGain, 3);
    }

    [Fact]
    public void PasteAttributes_KeepsTheTiming()
    {
        var project = Sample(10);
        TimelineEdits.TrySplit(project, 5);
        var source = project.Clips[0];
        var target = project.Clips[1];
        TimelineEdits.TrySetSpeed(project, source.Id, 2);
        project.Canvas.FocusBlur = 0.7;

        var bundle = TimelineEdits.CopyEffect(project, source.Id)!;
        var beforeDuration = TimelineMath.ClipDuration(target);
        Assert.True(TimelineEdits.PasteEffect(project, target.Id, bundle, includeTiming: false));
        // 只粘属性：速度和时长不动。
        Assert.Equal(1, target.Speed, 3);
        Assert.Equal(beforeDuration, TimelineMath.ClipDuration(target), 3);
        Assert.Equal(0.7, project.Canvas.FocusBlur, 3);
    }

    [Fact]
    public void CursorTrack_PositionClickAndIdleHide()
    {
        var asset = new MediaAsset
        {
            Video = "media/screen.mp4",
            Duration = 10,
            Width = 640,
            Height = 360,
            Cursor =
            [
                new CursorPoint { Time = 0, X = 0.1, Y = 0.1 },
                new CursorPoint { Time = 1, X = 0.5, Y = 0.5 },
                new CursorPoint { Time = 2, X = 0.5, Y = 0.5, Click = true },
                new CursorPoint { Time = 8, X = 0.9, Y = 0.9 }, // 静止 6 秒
            ],
        };
        var project = new ProjectDocument
        {
            Width = 640,
            Height = 360,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = 10 }],
        };

        // 插值位置
        var mid = CursorTrack.PositionAt(project, 0.5);
        Assert.NotNull(mid);
        Assert.Equal(0.3, mid!.Value.X, 2);
        // hidden 模式
        project.Canvas.Cursor.Mode = "hidden";
        Assert.Null(CursorTrack.PositionAt(project, 0.5));
        project.Canvas.Cursor.Mode = "pointer";
        // 静止隐藏：t=5 位于 2→8 的大空档中段
        project.Canvas.Cursor.IdleHide = true;
        Assert.Null(CursorTrack.PositionAt(project, 5.2));
        Assert.NotNull(CursorTrack.PositionAt(project, 2.5));
        // 点击反馈进度
        var progress = CursorTrack.ClickProgress(project, 2.1);
        Assert.NotNull(progress);
        Assert.InRange(progress!.Value, 0, 1);
        Assert.Null(CursorTrack.ClickProgress(project, 6));
        // 导出表达式：overlay 阶梯位置 + idle gaps 的 enable
        var expr = CursorTrack.OverlayExpression(project);
        Assert.NotNull(expr);
        Assert.Contains("if(lt(t,", expr!.Value.X, StringComparison.Ordinal);
        var enable = CursorTrack.EnableExpression(project);
        Assert.Contains("between(t", enable, StringComparison.Ordinal);
        Assert.Contains("1-between", enable, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_WithCursorTrack_OverlaysCursorAndRipple()
    {
        var asset = new MediaAsset
        {
            Video = "media/screen.mp4",
            Duration = 6,
            Width = 640,
            Height = 360,
            Cursor =
            [
                new CursorPoint { Time = 0, X = 0.1, Y = 0.1 },
                new CursorPoint { Time = 3, X = 0.5, Y = 0.5, Click = true },
                new CursorPoint { Time = 5.5, X = 0.8, Y = 0.8 },
            ],
        };
        var project = new ProjectDocument
        {
            Width = 640,
            Height = 360,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = 6 }],
        };
        var plan = ExportGraph.Build(project, new ExportOptions(), Media(project));
        Assert.True(plan.CursorIndex >= 0);
        Assert.True(plan.RippleIndex >= 0);
        Assert.Contains("cursorimg", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("rippleimg", plan.FilterScript, StringComparison.Ordinal);

        // 隐藏模式：overlay 仍生成但 enable=0（轨迹还在）。
        project.Canvas.Cursor.Mode = "hidden";
        var hidden = ExportGraph.Build(project, new ExportOptions(), Media(project));
        Assert.Contains("enable='0'", hidden.FilterScript, StringComparison.Ordinal);

        // 没轨迹的老项目：不生成光标 overlay。
        var plainProject = Sample(4);
        var plain = ExportGraph.Build(plainProject, new ExportOptions(), Media(plainProject));
        Assert.True(plain.CursorIndex < 0);
    }

    [Fact]
    public void CaptureRegion_FollowsTheWindowInsideTheMonitor()
    {
        var monitor = new CaptureRegion.Box(0, 0, 1920, 1080);
        var second = new CaptureRegion.Box(1920, 0, 1920, 1080);
        var monitors = new[] { monitor, second };

        // 窗口在主屏 → crop 是相对主屏的偏移。
        var window = new CaptureRegion.Box(100, 60, 800, 600);
        Assert.Equal(monitor, CaptureRegion.MonitorFor(window, monitors));
        Assert.Equal(new CaptureRegion.Box(100, 60, 800, 600), CaptureRegion.Crop(window, monitor));

        // 窗口移到副屏 → 监视器跟着切换，crop 用副屏坐标。
        var moved = new CaptureRegion.Box(2000, 100, 800, 600);
        Assert.Equal(second, CaptureRegion.MonitorFor(moved, monitors));
        var crop = CaptureRegion.Crop(moved, second);
        Assert.Equal(80, crop.X);
        Assert.Equal(100, crop.Y);
        Assert.Equal(800, crop.Width);

        // 窗口跨出监视器右缘 → 收缩到边界内且保持偶数宽。
        var edge = CaptureRegion.Crop(new CaptureRegion.Box(1800, 0, 800, 600), monitor);
        Assert.Equal(1800, edge.X);
        Assert.Equal(120, edge.Width);
        Assert.True(edge.Width % 2 == 0 && edge.Height % 2 == 0);

        // 完全在监视器外 → crop 仍在监视器范围内（最小 64）。
        var outside = CaptureRegion.Crop(new CaptureRegion.Box(5000, 5000, 800, 600), monitor);
        Assert.True(outside.X + outside.Width <= monitor.Width);
        Assert.True(outside.Y + outside.Height <= monitor.Height);
        Assert.True(outside.Width >= 64 && outside.Height >= 64);
    }

    [Fact]
    public void Mask_KeepsTheCenterAndClearsTheCorner()
    {
        var pixels = MaskImage.CreateBgra(32, 32, 8, circle: false);
        Assert.Equal(255, pixels[((16 * 32) + 16) * 4 + 3]);
        Assert.Equal(0, pixels[3]);
        var circle = MaskImage.CreateBgra(32, 32, 0, circle: true);
        Assert.Equal(0, circle[3]);
        Assert.Equal(255, circle[((16 * 32) + 16) * 4 + 3]);
    }

    [Fact]
    public void Export_WithWallpaper_ReplacesColorBackground()
    {
        var wallpaper = Path.Combine(Path.GetTempPath(), "dogsc-wall-" + Guid.NewGuid().ToString("N") + ".bmp");
        MaskImage.WriteBmp(wallpaper, 8, 8, 0, circle: false);
        var project = Sample(4);
        project.Canvas.Wallpaper = wallpaper;
        var baseMedia = Media(project);
        var resolved = new ResolvedMedia
        {
            Videos = baseMedia.Videos, Microphones = baseMedia.Microphones,
            Systems = baseMedia.Systems, Cameras = baseMedia.Cameras, Wallpaper = wallpaper,
        };
        var plan = ExportGraph.Build(project, new ExportOptions(), resolved);
        Assert.True(plan.WallpaperIndex >= 0);
        // 壁纸走 cover+模糊铺底，不再是纯色 color 源。
        Assert.Contains("gblur", plan.FilterScript, StringComparison.Ordinal);
        Assert.DoesNotContain("color=c=0x", plan.FilterScript, StringComparison.Ordinal);
        // 文件丢了 → 退回纯色背景。
        File.Delete(wallpaper);
        var fallback = ExportGraph.Build(project, new ExportOptions(), resolved);
        Assert.True(fallback.WallpaperIndex < 0);
        Assert.Contains("color=c=0x", fallback.FilterScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_WithSticker_OverlaysScaledImage()
    {
        var sticker = Path.Combine(Path.GetTempPath(), "dogsc-st-" + Guid.NewGuid().ToString("N") + ".bmp");
        MaskImage.WriteBmp(sticker, 16, 8, 0, circle: false);
        var project = Sample(4);
        project.Canvas.Sticker = new StickerImage { Path = sticker, X = 0.8, Y = 0.2, Width = 0.3, Opacity = 0.5 };
        var resolved = new ResolvedMedia
        {
            Videos = Media(project).Videos, Microphones = new Dictionary<string, string>(),
            Systems = new Dictionary<string, string>(), Cameras = new Dictionary<string, string>(),
            Sticker = sticker, StickerAspect = 2,
        };
        var plan = ExportGraph.Build(project, new ExportOptions(), resolved);
        Assert.True(plan.StickerIndex >= 0);
        Assert.Contains("stickerimg", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("colorchannelmixer=aa=0.5", plan.FilterScript, StringComparison.Ordinal);
        Assert.True(plan.StickerRect.W > 0);
        File.Delete(sticker);
    }

    [Fact]
    public void Export_WithSoftRegion_BuildsMaskedBlurChain()
    {
        var project = Sample(4);
        project.Canvas.SoftRegion = new SoftRegion { Enabled = true, X = 0.1, Y = 0.1, Width = 0.5, Height = 0.5, Strength = 0.8 };
        var plan = ExportGraph.Build(project, new ExportOptions(), Media(project));
        Assert.True(plan.SoftMaskIndex >= 0);
        Assert.Contains("smask", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("maskedmerge", plan.FilterScript, StringComparison.Ordinal);
        Assert.Contains("gblur", plan.FilterScript, StringComparison.Ordinal);
        Assert.True(plan.SoftRect.W > 0);
        // 关掉开关 → 无柔化链。
        project.Canvas.SoftRegion.Enabled = false;
        var off = ExportGraph.Build(project, new ExportOptions(), Media(project));
        Assert.True(off.SoftMaskIndex < 0);
    }

    [Fact]
    public void RectMask_InsideWhiteOutsideBlack()
    {
        var mask = MaskImage.CreateRectMask(100, 80, 20, 20, 40, 30, 6);
        int At(int x, int y) => mask[(y * 100 + x) * 4];
        Assert.Equal(255, At(40, 35));   // 中心
        Assert.Equal(0, At(5, 5));       // 矩形外
        Assert.Equal(0, At(20, 20));     // 顶角边界 feather 起点
        Assert.True(At(23, 26) > 0 && At(23, 26) < 255); // feather 过渡带
    }

    [Fact]
    public void Normalize_ClampsDecorationFields()
    {
        var project = Sample(2);
        project.Canvas.Wallpaper = "  ";
        project.Canvas.WallpaperBlur = 5;
        project.Canvas.Sticker = new StickerImage { Path = "", X = 9, Width = 9 };
        project.Canvas.SoftRegion = new SoftRegion { Enabled = true, X = -1, Width = 9, Strength = 9 };
        ProjectFile.Normalize(project);
        Assert.Null(project.Canvas.Wallpaper);
        Assert.Equal(1, project.Canvas.WallpaperBlur);
        Assert.Null(project.Canvas.Sticker); // 空路径被清掉
        Assert.Equal(0, project.Canvas.SoftRegion.X);
        Assert.Equal(1, project.Canvas.SoftRegion.Width);
        Assert.Equal(1, project.Canvas.SoftRegion.Strength);
    }

    [Fact]
    public void Export_CameraShapePicksMask_AndEntryAnimates()
    {
        var asset = new MediaAsset { Video = "media/screen.mp4", Camera = "media/cam.mp4", Duration = 4, Width = 640, Height = 360 };
        var project = new ProjectDocument
        {
            Width = 640, Height = 360,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = 4 }],
        };
        ResolvedMedia Media() => new()
        {
            Videos = new Dictionary<string, string> { [asset.Id] = "C:/screen.mp4" },
            Microphones = new Dictionary<string, string>(),
            Systems = new Dictionary<string, string>(),
            Cameras = new Dictionary<string, string> { [asset.Id] = "C:/cam.mp4" },
        };

        // 方形 mask：plan 带 square 与圆角像素。
        project.Camera.Shape = "square";
        project.Camera.Corner = 0.3;
        var square = ExportGraph.Build(project, new ExportOptions(), Media());
        Assert.Equal("square", square.CameraShape);
        Assert.True(square.CameraCorner > 0);
        Assert.True(square.CameraMaskIndex >= 0);
        // 默认无进出动画：camr 直接进 overlay，没有 camf 中间帧。
        Assert.DoesNotContain("camf", square.FilterScript, StringComparison.Ordinal);

        // fade 进出：camf 链出现两次 alpha fade。
        project.Camera.Entry = "fade";
        var fade = ExportGraph.Build(project, new ExportOptions(), Media());
        Assert.Contains("fade=t=in", fade.FilterScript, StringComparison.Ordinal);
        Assert.Contains("fade=t=out", fade.FilterScript, StringComparison.Ordinal);

        // rise：overlay 的 y 变成逐帧表达式。
        project.Camera.Entry = "rise";
        var rise = ExportGraph.Build(project, new ExportOptions(), Media());
        Assert.Contains("min((t-", rise.FilterScript, StringComparison.Ordinal);

        // none：无 camf、无表达式 y。
        project.Camera.Entry = "none";
        var none = ExportGraph.Build(project, new ExportOptions(), Media());
        Assert.DoesNotContain("camf", none.FilterScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_CameraShapeAndEntryWhitelist()
    {
        var project = Sample(2);
        project.Camera.Shape = "hexagon";
        project.Camera.Entry = "spin";
        project.Camera.Corner = 9;
        ProjectFile.Normalize(project);
        Assert.Equal("circle", project.Camera.Shape);
        Assert.Equal("none", project.Camera.Entry);
        Assert.Equal(0.5, project.Camera.Corner);
    }

    [Fact]
    public void ProjectRoundTrip_RejectsMacPackages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dogsc-test-" + Guid.NewGuid().ToString("N"));
        ProjectFile.Save(directory, Sample(3));
        var loaded = ProjectFile.Load(directory);
        Assert.Equal("演示", loaded.Name);
        loaded.Platform = "macos";
        ProjectFile.Save(directory, loaded);
        var error = Assert.Throws<InvalidDataException>(() => ProjectFile.Load(directory));
        Assert.Contains("macOS", error.Message, StringComparison.Ordinal);
        Directory.Delete(directory, recursive: true);
    }

    private static ProjectDocument Sample(double seconds)
    {
        var asset = new MediaAsset
        {
            Video = "media/screen.mp4",
            Duration = seconds,
            Width = 640,
            Height = 360,
        };
        return new ProjectDocument
        {
            Name = "演示",
            Width = 640,
            Height = 360,
            Assets = [asset],
            Clips = [new ClipSegment { AssetId = asset.Id, SourceStart = 0, SourceEnd = seconds }],
        };
    }

    private static ResolvedMedia Media(ProjectDocument project) => new()
    {
        Videos = project.Assets.ToDictionary(asset => asset.Id, _ => "C:/screen.mp4"),
        Microphones = new Dictionary<string, string>(),
        Systems = new Dictionary<string, string>(),
        Cameras = new Dictionary<string, string>(),
    };
}
