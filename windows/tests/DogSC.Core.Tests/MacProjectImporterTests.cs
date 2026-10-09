using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class MacProjectImporterTests
{
    const string Sample = """
        {
          "version": 27,
          "title": "演示录制",
          "media": {
            "screen":      { "relativePath": "media/screen.mp4", "startOffset": 0, "sourceStartTime": 0 },
            "camera":      { "relativePath": "media/camera.mp4", "startOffset": 0 },
            "microphone":  { "relativePath": "media/mic.wav",    "startOffset": 0 },
            "pointerEvents": { "relativePath": "media/pointer.json" }
          },
          "canvas": {
            "aspectRatio": "16:9",
            "padding": 80,
            "contentScale": 1.2,
            "contentPosition": { "x": 0.6, "y": 0.4 },
            "cornerRadius": 40,
            "shadowStrength": 0.5,
            "backgroundSource": { "kind": "projectImage", "relativePath": "media/wall.png" },
            "screenFrame": "devicePhone",
            "crop": { "x": 0, "y": 0, "width": 1, "height": 1 }
          },
          "camera": {
            "shape": "圆形",
            "position": { "x": 0.9, "y": 0.8 },
            "size": 0.3,
            "borderWidth": 3
          },
          "audio": { "systemVolume": 0.7, "microphoneVolume": 0.9, "isSystemMuted": false, "isMicrophoneMuted": true },
          "cursorStyle": { "assetID": "system.touch-dot", "hideWhenIdle": true, "clickEffectStyle": "ripple", "size": 2 },
          "openingSequence": { "isEnabled": true, "preset": "light3D" },
          "timeline": {
            "sourceSequence": {
              "kind": "edited",
              "segments": [
                { "id": "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA", "sourceStart": 0,   "sourceDuration": 4, "playbackRate": 1 },
                { "id": "BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB", "sourceStart": 6,   "sourceDuration": 2, "playbackRate": 2 }
              ]
            },
            "zoomClips": [
              { "id": "CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC", "startTime": 1, "endTime": 3, "scale": 1.8,
                "focus": { "x": 0.3, "y": 0.7 }, "easing": "cubic" }
            ]
          },
          "motion": {},
          "exportSettings": {},
          "recordingMarkers": []
        }
        """;

    [Fact]
    public void Import_MapsClipsZoomsCanvasCameraAudio()
    {
        var result = MacProjectImporter.Import(Sample);
        var project = result.Project;

        // 片段：sourceStart/Duration→sourceStart/End；playbackRate→Speed。
        Assert.Equal(2, project.Clips.Count);
        Assert.Equal(0, project.Clips[0].SourceStart);
        Assert.Equal(4, project.Clips[0].SourceEnd);
        Assert.Equal(1, project.Clips[0].Speed);
        Assert.Equal(6, project.Clips[1].SourceStart);
        Assert.Equal(8, project.Clips[1].SourceEnd);
        Assert.Equal(2, project.Clips[1].Speed);

        // 缩放：startTime/endTime→Start/Duration，focus→FocusX/Y。
        var zoom = Assert.Single(project.Zooms);
        Assert.Equal(1, zoom.Start);
        Assert.Equal(2, zoom.Duration);
        Assert.Equal(1.8, zoom.Scale);
        Assert.Equal(0.3, zoom.FocusX);
        Assert.Equal(0.7, zoom.FocusY);

        // 画布。
        Assert.Equal("16:9", project.Canvas.Aspect);
        Assert.Equal(1.2, project.Canvas.Scale);
        Assert.Equal(0.6, project.Canvas.CenterX);
        Assert.Equal(40, project.Canvas.Corner);
        Assert.Equal(50, project.Canvas.Shadow);
        Assert.Equal("media/wall.png", project.Canvas.Wallpaper);

        // 摄像头：圆形 + 位置 + 大小。
        Assert.Equal("circle", project.Camera.Shape);
        Assert.Equal(0.9, project.Camera.CenterX);
        Assert.Equal(0.3, project.Camera.Size);

        // 音频：mic 静音→增益 0，系统 0.7。
        Assert.Equal(0, project.MicrophoneGain);
        Assert.Equal(0.7, project.SystemGain);

        // 光标：touch-dot→dot，idle 隐藏开。
        Assert.Equal("dot", project.Canvas.Cursor.Mode);
        Assert.True(project.Canvas.Cursor.IdleHide);
        Assert.True(project.Canvas.Cursor.ClickRipple);

        // 丢弃清单：screenFrame/crop/width 等 macOS 独有字段被记下。
        Assert.Contains(result.DroppedFields, f => f.StartsWith("canvas.screenFrame"));
        Assert.DoesNotContain(result.DroppedFields, f => f.StartsWith("canvas.crop"));
        Assert.Equal(1, project.Canvas.Crop.Width);
        Assert.Equal(2, project.Canvas.Cursor.Size);
        Assert.Contains(result.DroppedFields, f => f == "openingSequence");
        Assert.Contains(result.DroppedFields, f => f == "motion");
        Assert.Contains(result.DroppedFields, f => f.StartsWith("camera.borderWidth"));

        // 媒体引用清单供调用方拷文件。
        Assert.Contains("media/screen.mp4", result.MediaReferences);
        Assert.Contains("media/camera.mp4", result.MediaReferences);
        // pointerEvents 是 mac 的光标轨迹（events/pointer.jsonl），不该被拷进 media/。
        Assert.DoesNotContain("media/pointer.json", result.MediaReferences);
        Assert.DoesNotContain(result.MediaReferences, r => r.Contains("pointer"));
        Assert.Equal("media/pointer.json", result.PointerPath);
        Assert.DoesNotContain(result.DroppedFields, f => f.StartsWith("media.pointerEvents"));
    }

    [Fact]
    public void Import_NoScreen_Throws()
    {
        var json = """{ "version": 27, "title": "空", "media": {} }""";
        Assert.Throws<InvalidDataException>(() => MacProjectImporter.Import(json));
    }

    [Fact]
    public void Import_FullRecording_FallsBackToSingleClip()
    {
        var json = """
            {
              "version": 27, "title": "整段",
              "media": { "screen": { "relativePath": "media/screen.mp4" } },
              "timeline": { "sourceSequence": { "kind": "fullRecording" } }
            }
            """;
        var result = MacProjectImporter.Import(json);
        var clip = Assert.Single(result.Project.Clips);
        Assert.Equal(0, clip.SourceStart);
        // 时长未知由调用方探测；这里保底为 1 段占位。
        Assert.True(clip.SourceEnd >= 1);
        Assert.Contains(result.DroppedFields, f => f.Contains("fullRecording"));
    }
}
