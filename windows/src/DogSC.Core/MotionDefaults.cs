using System.Text.Json;

namespace DogSC.Core;

public sealed class MotionDefaults
{
    public double? DefaultZoomScale { get; set; }
    public string DefaultZoomEasing { get; set; } = "spring";
    public double DefaultZoomTransitionDuration { get; set; } = .7;
    public double ScreenSpringMass { get; set; } = 2.4;
    public double ScreenSpringStiffness { get; set; } = 210;
    public double ScreenSpringDamping { get; set; } = 42;
    public ScreenMotionCreationStyle? DefaultScreenMotion { get; set; }
}

// Placement, identity, progress offsets and camera links belong to each clip.
public sealed class ScreenMotionCreationStyle
{
    public ScreenMotionState Target { get; set; } = new();
    public bool LinearBlur { get; set; }
    public double LinearBlurSize { get; set; } = .32;
    public double LinearBlurSoftness { get; set; } = .5;
    public double LinearBlurStrength { get; set; } = .35;
    public double LinearBlurAngle { get; set; }
    public string Easing { get; set; } = "spring";
    public double[]? CustomCurve { get; set; }
    public double LeadInDuration { get; set; } = .7;
    public double ReturnDuration { get; set; } = .7;

    public static ScreenMotionCreationStyle Capture(ScreenMotionClip clip) => new()
    {
        Target = ScenePreset.Clone(clip.Target), LinearBlur = clip.LinearBlur,
        LinearBlurSize = clip.LinearBlurSize, LinearBlurSoftness = clip.LinearBlurSoftness,
        LinearBlurStrength = clip.LinearBlurStrength, LinearBlurAngle = clip.LinearBlurAngle,
        Easing = clip.Timing.Easing, CustomCurve = clip.Timing.CustomCurve?.ToArray(),
        LeadInDuration = clip.Timing.PreferredLeadInDuration ?? clip.Timing.LeadInDuration,
        ReturnDuration = clip.Timing.PreferredReturnDuration
            ?? (clip.Timing.ReturnDuration > .0001 ? clip.Timing.ReturnDuration : .7),
    };

    public bool Matches(ScreenMotionCreationStyle other) =>
        JsonSerializer.Serialize(this, ProjectSchema.Json) == JsonSerializer.Serialize(other, ProjectSchema.Json);

    public ScreenMotionClip Create(TransitionTiming timing)
    {
        timing.Easing = Easing; timing.CustomCurve = CustomCurve?.ToArray();
        timing.PreferredLeadInDuration = LeadInDuration; timing.PreferredReturnDuration = ReturnDuration;
        timing.LeadInDuration = Math.Min(LeadInDuration, timing.Duration);
        timing.ReturnDuration = Math.Min(ReturnDuration, timing.ReturnDuration);
        timing.LeadInProgressOffset = timing.ReturnProgressOffset = 0;
        return new ScreenMotionClip
        {
            Timing = timing, Target = ScenePreset.Clone(Target), LinearBlur = LinearBlur,
            LinearBlurSize = LinearBlurSize, LinearBlurSoftness = LinearBlurSoftness,
            LinearBlurStrength = LinearBlurStrength, LinearBlurAngle = LinearBlurAngle,
        };
    }
}
