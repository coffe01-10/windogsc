using System.Text.Json;
using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class MotionDefaultsTests
{
    static ProjectDocument Project() => new()
    {
        Width = 960, Height = 720, Assets = [new() { Id = "a", Duration = 10, Video = "screen.mp4" }],
        Clips = [new() { AssetId = "a", SourceEnd = 10 }],
    };

    [Fact]
    public void SceneDefaultsCreateNewZoomAnd3DWithoutCopyingExistingTimedEdits()
    {
        var source = Project(); source.Motion.DefaultZoomScale = 5.5; source.Motion.DefaultZoomEasing = "cubic";
        source.Motion.DefaultZoomTransitionDuration = 1.1;
        var authored = new ScreenMotionClip { Target = new() { RotationX = 20, RotationY = -25, Scale = 1.4 }, LinearBlur = true,
            Timing = new() { StartTime = 1, Duration = .4, LeadInDuration = .4, PreferredLeadInDuration = 1.3,
                ReturnDuration = .2, PreferredReturnDuration = .9, Easing = "custom", CustomCurve = [.2, .1, .8, .9], LeadInProgressOffset = .5 },
            GroupId = "paired-camera", LinearBlurAngle = 35 };
        source.Motion.DefaultScreenMotion = ScreenMotionCreationStyle.Capture(authored);
        var target = Project(); var existing = new ZoomSpan { Scale = 2, Start = 0, Duration = 1 }; target.Zooms.Add(existing);
        var scene = ScenePreset.Capture(source, "运镜场景"); scene.Apply(target);
        var zoom = TimelineEdits.AddZoom(target, 2)!; var screen = TimelineEdits.PrepareScreenMotion(target, 6)!;
        Assert.Same(existing, target.Zooms[0]); Assert.Equal(2, existing.Scale);
        Assert.Equal(5.5, zoom.Scale); Assert.Equal("cubic", zoom.Easing); Assert.Equal(1.1, zoom.EnterDuration);
        Assert.NotEqual(authored.Id, screen.Id); Assert.Null(screen.GroupId); Assert.Equal(6, screen.Timing.StartTime);
        Assert.Equal(0, screen.Timing.LeadInProgressOffset); Assert.Equal(1, screen.Timing.LeadInDuration);
        Assert.Equal(1.3, screen.Timing.PreferredLeadInDuration); Assert.Equal(.9, screen.Timing.ReturnDuration);
        Assert.Equal(20, screen.Target.RotationX); Assert.Equal(-25, screen.Target.RotationY); Assert.True(screen.LinearBlur); Assert.Equal(35, screen.LinearBlurAngle);
        screen.Target.RotationY = 30; screen.Timing.CustomCurve![0] = .6;
        Assert.Equal(-25, source.Motion.DefaultScreenMotion.Target.RotationY); Assert.Equal(.2, target.Motion.DefaultScreenMotion!.CustomCurve![0]);
        ProjectFile.Normalize(target); Assert.Equal(5.5, zoom.Scale);
        Assert.Contains(",1,6)", new ZoomMotion(target).Expressions(0, 10, "t").Scale);
        Assert.Equal(174, CanvasLayout.ZoomCrop(960, 720, 5.5, .5, .5).Width);
    }

    [Fact]
    public void DefaultsAndLegacySceneCompatibilitySurviveSnapshots()
    {
        var project = Project(); project.Motion.DefaultZoomScale = 2.8;
        var before = JsonSerializer.Serialize(project, ProjectSchema.Json);
        project.Motion.DefaultZoomScale = 4.5; project.Motion.DefaultScreenMotion = new() { Target = new() { RotationY = 25 } };
        var restored = JsonSerializer.Deserialize<ProjectDocument>(before, ProjectSchema.Json)!;
        Assert.Equal(2.8, TimelineEdits.AddZoom(restored, 0)!.Scale); Assert.Null(restored.Motion.DefaultScreenMotion);
        new ScenePreset { Motion = null }.Apply(project); Assert.Equal(4.5, project.Motion.DefaultZoomScale);
        var legacy = new ScenePreset { Motion = new() { DefaultZoomScale = null } }; legacy.Apply(project);
        Assert.Equal(4.5, project.Motion.DefaultZoomScale);
        var saved = ScenePreset.Capture(restored, "默认"); Assert.True(saved.MatchesConfiguration(restored));
        restored.Motion.DefaultZoomTransitionDuration = 1.2; Assert.False(saved.MatchesConfiguration(restored));
    }

    [Fact]
    public void FallbackMagnificationIsCapturedAndProjectDefaultWins()
    {
        var project = Project();
        var preset = ScenePreset.Capture(project, "记住的倍率", zoomCreationScale: 3.6);
        Assert.True(preset.MatchesConfiguration(project, zoomCreationScale: 3.6));
        Assert.False(preset.MatchesConfiguration(project, zoomCreationScale: 2.4));
        Assert.Equal(3.6, TimelineEdits.AddZoom(project, 0, 3.6)!.Scale);
        project.Motion.DefaultZoomScale = 2.2; Assert.Equal(2.2, TimelineEdits.AddZoom(project, 4, 3.6)!.Scale);
        var fallback = new ScreenMotionCreationStyle { Target = new() { RotationY = 15 } };
        Assert.Equal(15, TimelineEdits.PrepareScreenMotion(project, 0, fallbackStyle: fallback)!.Target.RotationY);
        project.Motion.DefaultScreenMotion = new() { Target = new() { RotationY = -10 } };
        Assert.Equal(-10, TimelineEdits.PrepareScreenMotion(project, 0, fallbackStyle: fallback)!.Target.RotationY);
    }

    [Fact]
    public void MacImportRetainsCreationStyleAndObjectBezierCurve()
    {
        var result = MacProjectImporter.Import("""
        { "title":"默认样式", "media":{"screen":{"relativePath":"media/screen.mp4"}},
          "timeline":{"sourceSequence":{"kind":"edited","segments":[{"sourceStart":0,"sourceDuration":10}]},
            "screenMotionClips":[{"timing":{"startTime":0,"duration":1,"easing":"custom","customCurve":{"x1":0.2,"y1":0.1,"x2":0.8,"y2":0.9}}}]},
          "motion":{"defaultZoomScale":5.5,"defaultZoomEasing":"三次方缓动","defaultZoomTransitionDuration":1.1,
            "screenSpringMass":3,"screenSpringStiffness":250,"screenSpringDamping":50,"screen":"focused",
            "defaultScreenMotion":{"target":{"position":{"x":0.7,"y":0.4},"scale":1.4,"rotationY":25},
              "easing":"自定义曲线","customCurve":{"x1":0.2,"y1":0.1,"x2":0.8,"y2":0.9},"leadInDuration":1.3,"returnDuration":0.9,
              "focusEffect":{"shape":"linear","size":0.3,"blur":0.6,"softness":0.4,"angleDegrees":35}}}}
        """);
        var project = result.Project; var zoom = TimelineEdits.AddZoom(project, 2)!; var motion = TimelineEdits.PrepareScreenMotion(project, 6)!;
        Assert.Equal(5.5, zoom.Scale); Assert.Equal("cubic", zoom.Easing); Assert.Equal(3, zoom.SpringMass);
        Assert.Equal(25, motion.Target.RotationY); Assert.Equal(.7, motion.Target.X); Assert.Equal("custom", motion.Timing.Easing);
        Assert.Equal([.2, .1, .8, .9], motion.Timing.CustomCurve!); Assert.Equal([.2, .1, .8, .9], project.ScreenMotions[0].Timing.CustomCurve!);
        Assert.True(motion.LinearBlur); Assert.Equal(.6, motion.LinearBlurStrength); Assert.Equal(35, motion.LinearBlurAngle);
        Assert.DoesNotContain("motion", result.DroppedFields); Assert.Contains("motion.screen", result.DroppedFields);
    }
}
