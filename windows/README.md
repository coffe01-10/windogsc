# DogSC Windows

Windows 桌面版。录制屏幕、剪一段、导出 MP4。界面和流程对齐 macOS 版 DogSC，实现是 .NET 8 / WPF，不编译 `Sources/` 里的 Swift。

## 能做什么

- 录显示器、窗口、自选区域，或把摄像头 / 采集卡当作主画面
- 同时录麦克风、系统声音，以及一颗摄像头画中画
- 录制条、倒计时和备忘录不会进画面
- 录制条左侧把手与备忘录标题栏支持拖动；编辑器可拖动分隔条调整属性栏宽度、时间线高度，打开窗口时适配当前屏幕工作区
- 录制条默认缩小到 85%；右侧箭头收起 / 展开，菜单「工具条大小」可调 70%–110%，也可按住 Ctrl 滚动鼠标滚轮；大小和收起状态会自动记住，收起后仍可暂停、继续和停止录制
- 录制中按 `Ctrl+Alt+M` 打标记，`Ctrl+Alt+S` 停止
- 暂停会另起一段，时间线上接在一起
- 编辑器里分割、裁短、重排、变速、标记、手动缩放，支持缩略图、波形和边缘吸附
- 按录制点击生成自动缩放，调整光标样式、点击反馈和静止隐藏
- 画面留白、位置、缩放、圆角、阴影、倾斜、边缘虚化与淡入 / 上移 / 放大开场
- 壁纸、贴图和局部柔化，以及摄像头画中画的形状、位置与进出动画
- 复制缩放片段或素材效果，粘贴完整副本或只粘属性，支持撤销 / 重做
- 导出 MP4（原始 / 1080p / 1440p / 4K，30 或 60 帧）或 M4A

项目保存在「视频 / DogSC」下的 `.dogscproject` 文件夹，使用 Windows 项目格式。录制条的「导入 macOS 项目…」可把 macOS 项目包转换成新的 Windows 项目；迁移屏幕、摄像头、麦克风和部分剪辑 / 缩放 / 布局设置，未支持的效果会被略过。MOV 等素材会尝试用 ffmpeg 转为 H.264，以便在 Windows 预览。

还没有完整移植：macOS 屏幕 3D 透视与独立运镜轨道、连续光标跟随、按时间段编辑贴图 / 柔化、iOS 设备镜像。Windows 当前的倾斜与按点击自动缩放属于简化实现。

## 效果复用与快捷键

选中缩放段后，`Ctrl+C` 复制其倍数、焦点和时长，`Ctrl+V` 创建独立副本。鼠标在时间线上时优先粘到鼠标位置，离开时间线时使用播放头；靠近其他缩放的边缘会吸附。空白不足或位置已被占用时会显示提示，原有缩放保持不变。缩放副本可跨编辑窗口粘贴。

`Ctrl+Shift+V` 只向所选缩放粘贴倍数和焦点，保留目标的位置和时长。选中素材时，同一组按钮 / 快捷键复用该素材的效果包；画布、画中画和音量是项目级设置，粘贴会影响整个项目，装饰图片的效果包限当前项目使用。

- `Space`：播放 / 暂停
- `S`：在播放头分割；`Delete`：删除所选片段；`M`：添加标记
- `Ctrl+Z`：撤销；`Ctrl+Y` 或 `Ctrl+Shift+Z`：重做

在名称等文本输入框内，保留正常的文字编辑快捷键。

录制条菜单支持上下方向键切换、Enter 选择、Escape 关闭。素材选中后显示裁剪把手，拖动过程中按 Escape 可取消。连续调整滑块合并为一次撤销，编辑期间合并保存，松开滑块、关闭编辑器和导出前写入项目。

录制条收起、展开及录制状态切换使用宽度缓动；高度随内容直接调整，避免下方文字被裁切。菜单与编辑器属性面板切换使用淡入；关闭 Windows 的界面动画后，这些过渡也会关闭。Windows 版不检查 macOS 发布版本，也不显示相关更新提示。

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
