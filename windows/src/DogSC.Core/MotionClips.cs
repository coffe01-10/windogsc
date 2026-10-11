using System.Text.Json.Serialization;

namespace DogSC.Core;

/// <summary>对齐 Mac TransitionTiming：整段时长 = 进入过渡 + 保持；回落把画面带回基础状态。</summary>
public sealed class TransitionTiming
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double StartTime { get; set; }
    /// <summary>整段时长（进入过渡 + 保持）。</summary>
    public double Duration { get; set; } = 0.7;
    /// <summary>进入过渡时长；0 = 直接跳变，超 duration 截断。</summary>
    public double LeadInDuration { get; set; }
    /// <summary>保留进入相位；0 = 从头开始，1 = 进入已完成。</summary>
    public double LeadInProgressOffset { get; set; }
    /// <summary>linear/quadratic/cubic/quintic/spring/custom。</summary>
    public string Easing { get; set; } = "cubic";
    /// <summary>自定义贝塞尔控制点（Easing=custom 时使用）。</summary>
    public double[]? CustomCurve { get; set; }
    /// <summary>结尾回到基础状态的过渡时长；0 = 保持目标。</summary>
    public double ReturnDuration { get; set; }
    /// <summary>剪切跨过回落起点时保留的回落相位。</summary>
    public double ReturnProgressOffset { get; set; }
    public double? PreferredLeadInDuration { get; set; }
    public double? PreferredReturnDuration { get; set; }

    [JsonIgnore] public double EndTime => StartTime + Duration;
    [JsonIgnore] public double EffectEndTime => EndTime + ReturnDuration;
}

/// <summary>归一化点（画布 0..1）。</summary>
public readonly record struct NormalizedPoint(double X, double Y)
{
    public NormalizedPoint Constrained(double safeInset)
    {
        var inset = Math.Clamp(safeInset, 0, 0.5);
        return new(Math.Clamp(X, inset, 1 - inset), Math.Clamp(Y, inset, 1 - inset));
    }
}

/// <summary>画布像素点（中心原点）。</summary>
public readonly record struct CompositionPoint(double X, double Y);

/// <summary>屏幕卡片的空间运动目标（裁切/边框/阴影属基础外观）。</summary>
public sealed class ScreenMotionState
{
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.5;
    public double Scale { get; set; } = 1;
    public double RotationX { get; set; }
    public double RotationY { get; set; }
    public double RotationZ { get; set; }
    public double Perspective { get; set; } = 0.65;
    /// <summary>投影锚点（Mac Focus/Center Anchors，默认 50%/50%）。</summary>
    public double AnchorX { get; set; } = 0.5;
    public double AnchorY { get; set; } = 0.5;
}

public sealed class ScreenMotionClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public TransitionTiming Timing { get; set; } = new();
    public ScreenMotionState Target { get; set; } = new();
    /// <summary>线性柔化：清晰带外逐渐混合模糊层，随运动的进入与退出过渡。</summary>
    public bool LinearBlur { get; set; }
    public double LinearBlurSize { get; set; } = .32;
    public double LinearBlurSoftness { get; set; } = .5;
    public double LinearBlurStrength { get; set; } = .35;
    public double LinearBlurAngle { get; set; }
    public FocusEffect? FocusEffect { get; set; }
    /// <summary>组合布局预设插入的跨轨道配对 ID。</summary>
    public string? GroupId { get; set; }
}

/// <summary>摄像头运动目标；layout 同时承载形状与全屏两态，离散形状在采样前先解析为连续参数。</summary>
public sealed class CameraMotionState
{
    /// <summary>square/horizontal/vertical/original/circle/fullscreen。</summary>
    public string Layout { get; set; } = "square";
    public double X { get; set; } = 0.86;
    public double Y { get; set; } = 0.82;
    public double Size { get; set; } = 0.35;
    public double Roundness { get; set; } = 0.5;
    public double Opacity { get; set; } = 1;
}

public sealed class CameraMotionClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public TransitionTiming Timing { get; set; } = new();
    public CameraMotionState Target { get; set; } = new();
    public string? GroupId { get; set; }
}

/// <summary>锚点空间：rect 线性过渡需要的画布与适配尺寸（对齐 Mac ScreenAnchorViewport）。</summary>
public readonly record struct ScreenAnchorViewport(
    double CanvasWidth,
    double CanvasHeight,
    double FittedWidth,
    double FittedHeight,
    double BorderWidthAtScaleOne = 0,
    double DecorationTopAtScaleOne = 0,
    double DecorationRightAtScaleOne = 0,
    double DecorationBottomAtScaleOne = 0,
    double DecorationLeftAtScaleOne = 0);

/// <summary>评估结果 + 非奇异的像素位移；manualOffset 供渲染用。</summary>
public readonly record struct ScreenMotionSample(
    ScreenMotionState State,
    CompositionPoint? ManualOffset,
    NormalizedPoint ProjectionAnchor);

/// <summary>摄像头连续采样：离散布局已解析为宽高比/圆角/全屏进度。</summary>
public readonly record struct CameraMotionSample(
    NormalizedPoint Position,
    double Size,
    double AspectRatio,
    double CornerFraction,
    double FullscreenProgress,
    double Opacity);
