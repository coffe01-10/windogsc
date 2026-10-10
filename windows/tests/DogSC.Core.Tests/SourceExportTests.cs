using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class SourceExportTests
{
    private static string MakeProjectWithMedia()
    {
        var dir = Path.Combine(Path.GetTempPath(), "srcex" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "media"));
        File.WriteAllText(Path.Combine(dir, "media", "screen.mp4"), "screen");
        File.WriteAllText(Path.Combine(dir, "media", "cam.mp4"), "cam");
        File.WriteAllText(Path.Combine(dir, "media", "mic.wav"), "mic");
        return dir;
    }

    [Fact]
    public void Export_CopiesThreeSources_IntoNamedFolder()
    {
        var dir = MakeProjectWithMedia();
        var project = new ProjectDocument { Name = "演示/录制" };
        var a = new MediaAsset { Video = "media/screen.mp4", Camera = "media/cam.mp4", Microphone = "media/mic.wav" };
        project.Assets.Add(a);
        var dest = Path.Combine(Path.GetTempPath(), "srcex-d" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);

        var folder = SourceExport.Export(project, dir, dest);

        Assert.Equal(Path.Combine(dest, "演示-录制-源文件"), folder);
        Assert.True(File.Exists(Path.Combine(folder, "屏幕.mp4")));
        Assert.True(File.Exists(Path.Combine(folder, "摄像头.mp4")));
        Assert.True(File.Exists(Path.Combine(folder, "麦克风.wav")));
    }

    [Fact]
    public void Export_ExistingFolder_GetsNumericSuffix()
    {
        var dir = MakeProjectWithMedia();
        var project = new ProjectDocument { Name = "t" };
        project.Assets.Add(new MediaAsset { Video = "media/screen.mp4" });
        var dest = Path.Combine(Path.GetTempPath(), "srcex-d" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dest, "t-源文件"));

        var folder = SourceExport.Export(project, dir, dest);

        Assert.Equal(Path.Combine(dest, "t-源文件-2"), folder);
    }

    [Fact]
    public void Export_MultipleAssets_GetNumberedNames()
    {
        var dir = MakeProjectWithMedia();
        var project = new ProjectDocument { Name = "t" };
        project.Assets.Add(new MediaAsset { Video = "media/screen.mp4", Camera = "media/cam.mp4" });
        File.WriteAllText(Path.Combine(dir, "media", "screen2.mp4"), "s2");
        project.Assets.Add(new MediaAsset { Video = "media/screen2.mp4" });
        var dest = Path.Combine(Path.GetTempPath(), "srcex-d" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);

        var folder = SourceExport.Export(project, dir, dest);

        Assert.True(File.Exists(Path.Combine(folder, "屏幕-1.mp4")));
        Assert.True(File.Exists(Path.Combine(folder, "屏幕-2.mp4")));
        Assert.True(File.Exists(Path.Combine(folder, "摄像头-1.mp4")));
    }

    [Fact]
    public void Export_NoMedia_ThrowsAndLeavesNoFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "srcex" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var project = new ProjectDocument { Name = "t" };
        project.Assets.Add(new MediaAsset { Video = "media/missing.mp4" });
        var dest = Path.Combine(Path.GetTempPath(), "srcex-d" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);

        Assert.Throws<InvalidDataException>(() => SourceExport.Export(project, dir, dest));
        Assert.Empty(Directory.GetDirectories(dest));
    }
}
