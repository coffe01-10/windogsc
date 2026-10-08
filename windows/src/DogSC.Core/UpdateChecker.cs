using System.Text.Json;
using System.Text.Json.Serialization;

namespace DogSC.Core;

/// <summary>一次更新检测的结果：latest 版本号、下载/说明页地址、是否有新版。</summary>
public sealed class UpdateInfo
{
    public string Latest { get; init; } = "";
    public string Url { get; init; } = "";
    public bool HasUpdate { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// GitHub releases 更新检测。网络部分留在 App 层，这里只做可测的
/// 版本号比较与 releases JSON 解析。
/// </summary>
public static class UpdateChecker
{
    /// <summary>
    /// 比较两个语义化版本（允许 v 前缀与 -beta.x 预发布后缀）。
    /// latest 更新返回 true；无法解析或 latest 不更新返回 false。
    /// </summary>
    public static bool IsNewer(string latest, string current)
    {
        if (!TryParse(latest, out var l) || !TryParse(current, out var c))
        {
            return false;
        }

        var core = l.Core.CompareTo(c.Core);
        if (core != 0)
        {
            return core > 0;
        }

        // 同主版本：正式版 > 预发布；都带预发布则比预发布序号。
        if (l.PreRelease.Length == 0 && c.PreRelease.Length > 0)
        {
            return true;
        }

        if (l.PreRelease.Length > 0 && c.PreRelease.Length == 0)
        {
            return false;
        }

        return string.Compare(l.PreRelease, c.PreRelease, StringComparison.OrdinalIgnoreCase) > 0;
    }

    /// <summary>
    /// 从 GitHub releases 的 JSON 响应里取最新一条非 draft 的 release。
    /// 传入的是 /releases 数组（不是 /latest 单对象），跳过 draft。
    /// </summary>
    public static UpdateInfo ParseReleases(string json, string current)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                {
                    continue;
                }

                var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                var url = release.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
                if (tag.Length == 0)
                {
                    continue;
                }

                return new UpdateInfo { Latest = tag, Url = url, HasUpdate = IsNewer(tag, current) };
            }

            return new UpdateInfo { Error = "release 列表为空。" };
        }
        catch (JsonException ex)
        {
            return new UpdateInfo { Error = "解析失败：" + ex.Message };
        }
    }

    /// <summary>解析 "v1.2.3-beta.4" / "1.2.3" 形式的版本号。</summary>
    public static bool TryParse(string tag, out (Version Core, string PreRelease) result)
    {
        result = (new Version(0, 0, 0), "");
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var dash = text.IndexOf('-');
        string pre = "";
        if (dash >= 0)
        {
            pre = text[(dash + 1)..];
            text = text[..dash];
        }

        if (!Version.TryParse(text, out var core) || core is null)
        {
            return false;
        }

        result = (core, pre);
        return true;
    }
}
