using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class VideoReferenceTests
{
    static ProjectDocument Project() => new()
    {
        Width = 320, Height = 180,
        Assets = [new() { Id = "a", Video = "video.mp4", SystemAudio = "system.wav", Width = 320, Height = 180, Duration = 2 }],
        Clips = [new() { Id = "c", AssetId = "a", SourceEnd = 2 }],
    };

    [Fact]
    public void AddingScreenMotionInsideExistingClipPreservesIts3DAngles()
    {
        var project = Project();
        var original = TimelineEdits.PrepareScreenMotion(project, 0)!;
        original.Target.RotationX = 20; original.Target.RotationY = -25;
        project.ScreenMotions.Add(original);
        var selected = TimelineEdits.PrepareScreenMotion(project, .5);
        Assert.Same(original, selected); Assert.Single(project.ScreenMotions);
        var pose = new ScreenMotionTrack(project.ScreenMotions).SampleLayout(.8).State;
        Assert.Equal(20, pose.RotationX, 6); Assert.Equal(-25, pose.RotationY, 6);
    }

    [Fact]
    public void NewScreenMotionFitsBeforeNextClipAndDoesNotShiftAtProjectEnd()
    {
        var project = Project();
        var next = TimelineEdits.PrepareScreenMotion(project, .6)!;
        project.ScreenMotions.Add(next);
        var inserted = TimelineEdits.PrepareScreenMotion(project, .2)!;
        Assert.Equal(.6, inserted.Timing.EndTime, 6);
        Assert.Equal(.4, inserted.Timing.LeadInDuration, 6);
        Assert.Null(TimelineEdits.PrepareScreenMotion(project, 2));
    }

    [Fact]
    public void SplitKeepsPerClipAudioAndOverridesCanRestoreMutedGlobalAudio()
    {
        var project = Project(); project.SystemGain = 0;
        project.Clips[0].SystemGain = .82; project.Clips[0].MicrophoneGain = .4;
        project.Clips[0].SystemEnabled = true;
        Assert.True(TimelineEdits.TrySplit(project, 1));
        Assert.Equal(.82, project.Clips[1].SystemGain); Assert.Equal(.4, project.Clips[1].MicrophoneGain);
        Assert.True(project.Clips[1].SystemEnabled);
        project.Clips[0].SystemEnabled = false;
        Assert.Equal(.82, project.Clips[0].SystemGain);
        var plan = ExportGraph.Build(project, new() { AudioOnly = true }, new()
        {
            Videos = new Dictionary<string, string> { ["a"] = "video.mp4" },
            Systems = new Dictionary<string, string> { ["a"] = "system.wav" },
            Microphones = new Dictionary<string, string>(), Cameras = new Dictionary<string, string>(),
        });
        Assert.NotNull(plan.AudioLabel);
        Assert.Contains("anullsrc", plan.FilterScript);
        Assert.Equal(2, plan.Duration, 6);
    }

    [Fact]
    public void SelectedPresetGroupsPreserveUnselectedSceneAndTimedEdits()
    {
        var source = Project(); source.Canvas.Padding = 100; source.Canvas.Cursor.Size = 1.25;
        source.Canvas.Wallpaper = "pattern:obsidianGrid"; source.Canvas.Opening = "sideSlide";
        var preset = ScenePreset.Capture(source, "局部场景");
        preset.IncludesCanvas = false; preset.IncludesCamera = false; preset.IncludesOpening = false;
        var target = Project(); target.Canvas.Padding = 30; target.Canvas.Opening = "light3D";
        target.Canvas.Stickers.Add(new() { Name = "本项目贴图" });
        target.Canvas.SoftRegions.Add(new() { Enabled = true });
        target.Canvas.Cursor.TimingAdvance = .25; target.Camera.Size = .4;
        preset.Apply(target);
        Assert.Equal(30, target.Canvas.Padding); Assert.Equal("light3D", target.Canvas.Opening);
        Assert.Equal(1.25, target.Canvas.Cursor.Size); Assert.Equal(.25, target.Canvas.Cursor.TimingAdvance);
        Assert.Equal("pattern:obsidianGrid", target.Canvas.Wallpaper);
        Assert.Equal(.4, target.Camera.Size); Assert.Single(target.Canvas.Stickers); Assert.Single(target.Canvas.SoftRegions);
        Assert.Equal(2, TimelineMath.Duration(target));
        target.Canvas.Cursor.Size = 2; Assert.Equal(1.25, preset.Canvas.Cursor.Size);
    }

    [Fact]
    public void FullScenePresetAppliesGlobalAudioButKeepsClipAudioAndTimedEdits()
    {
        var source = Project(); source.MicrophoneGain = .4; source.MicrophoneEnabled = false;
        source.SystemGain = 1.3; source.SystemEnabled = true;
        var preset = ScenePreset.Capture(source, "全套场景", "retained-id");
        var target = Project(); target.Clips[0].MicrophoneGain = .8; target.Clips[0].SystemEnabled = false;
        target.Zooms.Add(new ZoomSpan { Scale = 2, Start = .2 }); target.ScreenMotions.Add(new ScreenMotionClip());
        var clips = target.Clips; var zooms = target.Zooms; var motions = target.ScreenMotions;
        preset.Apply(target);
        Assert.Equal("retained-id", preset.Id);
        Assert.Equal(.4, target.MicrophoneGain); Assert.False(target.MicrophoneEnabled);
        Assert.Equal(1.3, target.SystemGain); Assert.True(target.SystemEnabled);
        Assert.Equal(.8, target.Clips[0].MicrophoneGain); Assert.False(target.Clips[0].SystemEnabled);
        Assert.Same(clips, target.Clips); Assert.Same(zooms, target.Zooms); Assert.Same(motions, target.ScreenMotions);
    }

    [Fact]
    public void OldSceneWithoutAudioKeepsCurrentGlobalAudio()
    {
        var target = Project(); target.SystemGain = .2; target.MicrophoneEnabled = false;
        new ScenePreset { Audio = null }.Apply(target);
        Assert.Equal(.2, target.SystemGain); Assert.False(target.MicrophoneEnabled);
    }

    [Fact]
    public void SceneModificationIndicatorTracksConfigurationAndIgnoresTimedContentAndSync()
    {
        var project = Project(); var snapshot = ScenePreset.Capture(project, "已保存");
        project.Canvas.Stickers.Add(new StickerImage()); project.Canvas.SoftRegions.Add(new SoftRegion());
        project.Canvas.Cursor.TimingAdvance = .2; project.Camera.TimingAdvance = .5;
        project.Clips[0].SystemGain = .4; project.Zooms.Add(new ZoomSpan { Scale = 3 });
        Assert.True(snapshot.MatchesConfiguration(project));
        project.SystemGain = .3; Assert.False(snapshot.MatchesConfiguration(project));
        project.SystemGain = snapshot.Audio!.SystemGain;
        project.Canvas.CenterX = .7; Assert.False(snapshot.MatchesConfiguration(project));
    }

    [Fact]
    public void LinearFocusHasAClearBandAndFollowsItsAngleAndTransition()
    {
        var clip = new ScreenMotionClip { LinearBlur = true, LinearBlurSize = .3, LinearBlurSoftness = .5,
            Timing = new() { StartTime = .2, Duration = 1, LeadInDuration = .2, ReturnDuration = .2, Easing = "linear" } };
        Assert.Equal(0, LinearFocus.Mask(160, 90, 320, 180, clip));
        Assert.Equal(1, LinearFocus.Mask(160, 0, 320, 180, clip));
        clip.LinearBlurAngle = 90;
        Assert.Equal(0, LinearFocus.Mask(160, 0, 320, 180, clip));
        Assert.Equal(1, LinearFocus.Mask(0, 90, 320, 180, clip));
        Assert.Equal(0, LinearFocus.Progress(clip, .1)); Assert.Equal(.5, LinearFocus.Progress(clip, .3), 6);
        Assert.Equal(1, LinearFocus.Progress(clip, 1)); Assert.Equal(.5, LinearFocus.Progress(clip, 1.3), 6);
        Assert.Equal(0, LinearFocus.Progress(clip, 1.5));
    }
}
