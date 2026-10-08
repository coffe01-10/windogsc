using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v0.2.0", "0.1.0", true)]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("v0.1.0", "0.1.0", false)]      // 同版本
    [InlineData("0.1.0", "0.2.0", false)]        // 本地更新
    [InlineData("v1.0.0", "0.9.9", true)]
    // 预发布：同主版本号下正式版 > beta；beta 序号递增算更新。
    [InlineData("v0.2.0", "0.2.0-beta.3", true)]
    [InlineData("v0.2.0-beta.4", "0.2.0-beta.3", true)]
    [InlineData("v0.2.0-beta.3", "0.2.0-beta.4", false)]
    [InlineData("v0.2.0-beta.1", "0.2.0", false)]
    // 无法解析的标签不算更新。
    [InlineData("latest", "0.1.0", false)]
    [InlineData("v0.2.0", "garbage", false)]
    public void IsNewer_ComparesSemanticVersions(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(latest, current));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3", "")]
    [InlineData("0.2.0-beta.4", "0.2.0", "beta.4")]
    [InlineData("V2.0.0-rc.1", "2.0.0", "rc.1")]
    public void TryParse_StripsVAndSplitsPreRelease(string tag, string core, string pre)
    {
        Assert.True(UpdateChecker.TryParse(tag, out var result));
        Assert.Equal(Version.Parse(core), result.Core);
        Assert.Equal(pre, result.PreRelease);
    }

    [Fact]
    public void ParseReleases_SkipsDrafts_AndReportsUpdate()
    {
        var json = """
            [
              { "tag_name": "v0.3.0", "draft": true,  "html_url": "https://x/draft" },
              { "tag_name": "v0.2.0", "draft": false, "html_url": "https://x/v020" },
              { "tag_name": "v0.1.0", "draft": false, "html_url": "https://x/v010" }
            ]
            """;
        var info = UpdateChecker.ParseReleases(json, "0.1.0");
        Assert.Null(info.Error);
        Assert.Equal("v0.2.0", info.Latest);
        Assert.Equal("https://x/v020", info.Url);
        Assert.True(info.HasUpdate);
    }

    [Fact]
    public void ParseReleases_NoNewer_ReturnsFalse()
    {
        var json = """[{ "tag_name": "v0.1.0", "draft": false, "html_url": "https://x/v010" }]""";
        var info = UpdateChecker.ParseReleases(json, "0.1.0");
        Assert.False(info.HasUpdate);
    }

    [Fact]
    public void ParseReleases_PicksHighestVersion_NotFirstEntry()
    {
        // /releases 按创建时间倒序：旧线补丁 v0.1.9 晚于 v0.2.0 发布会排前面，
        // 但应取更高的 v0.2.0，否则 HasUpdate 误判。
        var json = """
            [
              { "tag_name": "v0.1.9", "draft": false, "html_url": "https://x/v019" },
              { "tag_name": "v0.2.0", "draft": false, "html_url": "https://x/v020" },
              { "tag_name": "garbage", "draft": false, "html_url": "https://x/bad" }
            ]
            """;
        var info = UpdateChecker.ParseReleases(json, "0.2.0");
        Assert.Null(info.Error);
        Assert.Equal("v0.2.0", info.Latest);
        Assert.Equal("https://x/v020", info.Url);
        Assert.False(info.HasUpdate);
    }

    [Fact]
    public void ParseReleases_EmptyOrBad_ReportsError()
    {
        Assert.NotNull(UpdateChecker.ParseReleases("[]", "0.1.0").Error);
        Assert.NotNull(UpdateChecker.ParseReleases("not json", "0.1.0").Error);
    }
}
