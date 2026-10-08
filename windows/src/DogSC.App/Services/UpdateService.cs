using System.Net.Http;
using System.Reflection;
using DogSC.Core;

namespace DogSC;

/// <summary>
/// 启动时异步查 GitHub releases 最新版本，有新版就在录制条角落提示，
/// 点击跳浏览器到 release 页。只检测不替换自身——下载与安装交给用户。
/// </summary>
public static class UpdateService
{
    const string ReleasesUrl = "https://api.github.com/repos/laogou717/dogsc/releases";
    static readonly HttpClient _http = CreateClient();

    /// <summary>当前程序集版本（csproj 的 Version）。</summary>
    public static string CurrentVersion
    {
        get
        {
            var info = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                // CI 里 Version 可能带 +sha 后缀。
                var plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    /// <summary>
    /// 后台查一次；有新版回调 onUpdate（UI 线程），失败只记日志不打扰。
    /// </summary>
    public static void CheckInBackground(Action<UpdateInfo> onUpdate)
    {
        _ = Task.Run(async () =>
        {
            var info = await FetchAsync();
            if (info.HasUpdate)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => onUpdate(info));
            }
        });
    }

    /// <summary>设置页手动检查：返回完整结果（含"已是最新"和失败原因）。</summary>
    public static Task<UpdateInfo> CheckAsync() => FetchAsync();

    static async Task<UpdateInfo> FetchAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(ReleasesUrl);
            return UpdateChecker.ParseReleases(json, CurrentVersion);
        }
        catch (Exception ex)
        {
            AppPaths.Log("更新检测失败：" + ex.Message);
            return new UpdateInfo { Error = "连不上 GitHub 或解析失败。" };
        }
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // GitHub API 要求 User-Agent。
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DogSC-Windows/" + CurrentVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
