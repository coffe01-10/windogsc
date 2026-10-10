using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class SourceExportTests : IDisposable
{
    private readonly List<string> directories = [];
    string NewDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        directories.Add(path);
        Directory.CreateDirectory(path);
        return path;
    }
    public void Dispose()
    {
        foreach (var path in directories) Directory.Delete(path, recursive: true);
    }
    private string MakeProjectWithMedia()
    {
        var dir = NewDirectory("srcex");
        Directory.CreateDirectory(Path.Combine(dir, "media"));
        File.WriteAllText(Path.Combine(dir, "media", "screen.mp4"), "screen");
        File.WriteAllText(Path.Combine(dir, "media", "cam.mp4"), "cam");
        File.WriteAllText(Path.Combine(dir, "media", "mic.wav"), "mic");
        File.WriteAllText(Path.Combine(dir, "media", "system.wav"), "system-audio");
        return dir;
    }

    [Fact]
    public void Export_CopiesFourSources_IntoNamedFolder()
    {
        var dir = MakeProjectWithMedia();
        var project = new ProjectDocument { Name = "演示/录制" };
        var a = new MediaAsset { Video = "media/screen.mp4", Camera = "media/cam.mp4", Microphone = "media/mic.wav", SystemAudio = "media/system.wav" };
        project.Assets.Add(a);
        var dest = NewDirectory("srcex-d");
        Directory.CreateDirectory(dest);

        var folder = SourceExport.Export(project, dir, dest);

        Assert.Equal(Path.Combine(dest, "演示-录制-源文件"), folder);
        Assert.True(File.Exists(Path.Combine(folder, "屏幕.mp4")));
        Assert.True(File.Exists(Path.Combine(folder, "摄像头.mp4")));
        Assert.True(File.Exists(Path.Combine(folder, "麦克风.wav")));
        Assert.Equal("system-audio", File.ReadAllText(Path.Combine(folder, "系统声音.wav")));
    }

    [Fact]
    public void Export_ExistingFolder_GetsNumericSuffix()
    {
        var dir = MakeProjectWithMedia();
        var project = new ProjectDocument { Name = "t" };
        project.Assets.Add(new MediaAsset { Video = "media/screen.mp4" });
        var dest = NewDirectory("srcex-d");
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
        var dest = NewDirectory("srcex-d");
        Directory.CreateDirectory(dest);

        var folder = SourceExport.Export(project, dir, dest);

        Assert.True(File.Exists(Path.Combine(folder, "屏幕-1.mp4")));
        Assert.True(File.Exists(Path.Combine(folder, "屏幕-2.mp4")));
        Assert.True(File.Exists(Path.Combine(folder, "摄像头-1.mp4")));
    }

    [Fact]
    public void Export_NoMedia_ThrowsAndLeavesNoFolder()
    {
        var dir = NewDirectory("srcex");
        Directory.CreateDirectory(dir);
        var project = new ProjectDocument { Name = "t" };
        project.Assets.Add(new MediaAsset { Video = "media/missing.mp4" });
        var dest = NewDirectory("srcex-d");
        Directory.CreateDirectory(dest);

        Assert.Throws<InvalidDataException>(() => SourceExport.Export(project, dir, dest));
        Assert.Empty(Directory.GetDirectories(dest));
    }
}
