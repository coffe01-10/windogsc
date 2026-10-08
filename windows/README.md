# DogSC Windows

Windows 桌面版。录制屏幕、剪一段、导出 MP4。界面和流程对齐 macOS 版 DogSC，实现是 .NET 8 / WPF，不编译 `Sources/` 里的 Swift。

## 能做什么

- 录显示器、窗口、自选区域，或把摄像头 / 采集卡当作主画面
- 同时录麦克风、系统声音，以及一颗摄像头画中画
- 录制条、倒计时和备忘录不会进画面
- 录制中按 `Ctrl+Alt+M` 打标记，`Ctrl+Alt+S` 停止
- 暂停会另起一段，时间线上接在一起
- 编辑器里分割、裁短、变速、标记、手动缩放
- 画面留白、位置、缩放、圆角、阴影、背景和几个场景
- 导出 MP4（原始 / 1080p / 1440p / 4K，30 或 60 帧）或 M4A

项目保存在「视频 / DogSC」下的 `.dogscproject` 文件夹。这是 Windows 自己的项目格式，打不开 macOS 版的项目包。

还没有移植：屏幕 3D、自动跟随运镜、开场动画、效果复制粘贴、光标样式、iOS 设备镜像。

## 运行

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 和 [ffmpeg](https://www.gyan.dev/ffmpeg/builds/)（含 ffprobe）。ffmpeg 要在 PATH 里，或在应用设置中指定 `ffmpeg.exe`；把 `ffmpeg.exe` / `ffprobe.exe` 放到应用目录下也能被自动找到（`FfmpegTools.Candidates` 会查 `AppContext.BaseDirectory` 与 `DOGSC_FFMPEG` 环境变量）。

```powershell
dotnet run --project windows/src/DogSC.App
```

自检会生成一段测试画面、导出，并抓 1 秒屏幕：

```powershell
dotnet run --project windows/src/DogSC.App -- --self-test
```

核心时间线和导出图的测试：

```powershell
dotnet test windows/tests/DogSC.Core.Tests
```

## 发布

推 `v*` tag 会触发 `.github/workflows/windows.yml`：在 `windows-latest` 上 restore → build → test → `dotnet publish`（win-x64、依赖框架），跑发布产物的 `--self-test`，最后把 zip 挂到 release draft。CI 里只借一份 ffmpeg essentials 跑自检，zip 不分发 ffmpeg——发布说明里引导用户自备。本地复现构建：

```powershell
dotnet publish windows/src/DogSC.App/DogSC.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish
# 验证产物
& artifacts/publish/DogSC.exe --self-test
```

## 权限

Windows 桌面程序不需要单独的“屏幕录制”授权。第一次使用麦克风或摄像头时，系统可能会弹出隐私提示。系统声音走的是当前播放设备的回环，没有在播放的声音就是静音。
