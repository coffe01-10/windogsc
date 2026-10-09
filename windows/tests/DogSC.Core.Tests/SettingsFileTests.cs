using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class SettingsFileTests
{
    [Fact]
    public void Save_WithNonFiniteCoordinates_WritesDefaultsOnReload()
    {
        var path = TempFile();
        var settings = new AppSettings
        {
            Theme = "dark",
            RecordMicrophone = true,
            MemoText = "记得剪掉口误",
            BarLeft = double.NaN,
            BarTop = double.PositiveInfinity,
            MemoLeft = double.NegativeInfinity,
            MemoTop = double.NaN,
            MemoOpacity = double.NaN,
        };

        SettingsFile.Save(path, settings);

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("NaN", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", json, StringComparison.Ordinal);

        var loaded = SettingsFile.Load(path);
        Assert.Equal("dark", loaded.Theme);
        Assert.True(loaded.RecordMicrophone);
        Assert.Equal("记得剪掉口误", loaded.MemoText);
        Assert.Equal(double.NaN, loaded.BarLeft);
        Assert.Equal(double.NaN, loaded.BarTop);
        Assert.Equal(double.NaN, loaded.MemoLeft);
        Assert.Equal(double.NaN, loaded.MemoTop);
        Assert.Equal(0.94, loaded.MemoOpacity, 2);

        var restored = new AppSettings { BarLeft = 120.5, BarTop = 40, MemoText = "x", BarScale = 0.75, BarCollapsed = true, BarDockEdge = BarDockEdge.Right, BarAnchorRight = true, BarAnchorBottom = true };
        SettingsFile.Save(path, restored);
        var pinned = SettingsFile.Load(path);
        Assert.Equal(120.5, pinned.BarLeft, 3);
        Assert.Equal(40, pinned.BarTop, 3);
        Assert.Equal(0.75, pinned.BarScale, 3);
        Assert.True(pinned.BarCollapsed);
        Assert.Equal(BarDockEdge.Right, pinned.BarDockEdge);
        Assert.True(pinned.BarAnchorRight);
        Assert.True(pinned.BarAnchorBottom);

        File.Delete(path);
    }

    [Fact]
    public void Load_MissingOrCorruptFile_FallsBackToDefaults()
    {
        var missing = TempFile();
        var defaults = SettingsFile.Load(missing);
        Assert.Equal("light", defaults.Theme);
        Assert.Equal(30, defaults.FrameRate);
        Assert.True(defaults.RecordSystemAudio);
        Assert.False(defaults.RecordMicrophone);
        Assert.Equal(18, defaults.MemoSize, 2);
        Assert.True(double.IsNaN(defaults.BarLeft));
        Assert.Equal(0.85, defaults.BarScale, 3);
        Assert.False(defaults.BarCollapsed);

        File.WriteAllText(missing, "{ \"theme\": \"dark\", broken");
        var corrupt = SettingsFile.Load(missing);
        Assert.Equal("light", corrupt.Theme);
        Assert.True(double.IsNaN(corrupt.BarLeft));

        File.WriteAllText(missing, "{ \"theme\": \"dark\", \"barLeft\": NaN, \"memoSize\": 30 }");
        var nanFile = SettingsFile.Load(missing);
        Assert.Equal("light", nanFile.Theme);
        Assert.Equal(18, nanFile.MemoSize, 2);
        Assert.True(double.IsNaN(nanFile.BarLeft));

        File.Delete(missing);
    }

    [Fact]
    public void Save_UsesATemporaryFileAndOverwrites()
    {
        var path = TempFile();
        var settings = new AppSettings { Theme = "dark" };
        SettingsFile.Save(path, settings);
        settings.Theme = "light";
        SettingsFile.Save(path, settings);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal("light", SettingsFile.Load(path).Theme);
        File.Delete(path);
    }

    static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "dogsc-settings-test-" + Guid.NewGuid().ToString("N") + ".json");
}
