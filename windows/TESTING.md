# Windows 测试方法

2026-10-18 参考本机 Myrecordly 的测试实现，并用 Codex 内置浏览器操作了其真实 React 编辑器。重点借鉴真实操作和可观察结果；按改动选择相关场景，不增加每次修改都要跑的全量流程。

## Myrecordly 值得借鉴的做法

| 做法 | 参考实现 | 在 DogSC 中的用途 |
|---|---|---|
| 界面与桌面接口分开 | `tests/ui/bridge.ts` 用测试桥接替代 Electron 设备、文件和项目接口 | 界面验证使用临时项目，设备采集另做真实 Windows 验证；模拟成功不能证明录制成功 |
| 用小视频加载真实预览 | `tests/ui/fixtures/filmstrip.mp4`、`editor.spec.ts` 等待视频就绪 | 检查播放、定位、缩略图与剪辑，避免只有空界面的截图 |
| 从可访问控件执行操作 | `editor.spec.ts` 按按钮名称、角色和选中状态定位 | 优先从实际控件验证点击、焦点和键盘路由；直接调用处理函数只证明内部路径 |
| 检查焦点下的快捷键 | `playback-shortcut.spec.ts` 检查播放按钮获得焦点后的空格、长按重复和再次按下 | 防止空格触发两次、输入项目名时误剪切、滑块方向键被全局快捷键抢走 |
| 检查布局和命中范围 | `desktop-windows.spec.ts`、`timeline-interactions.spec.ts`、`timeline-gap-snapping.spec.ts` | 小窗口中的按钮与状态文字不能被裁掉；拖动手柄应命中所见边界，吸附后时间与画面一致 |
| 一次操作、一次撤销 | `editor.spec.ts` 和时间线相关用例 | 分割、联动裁短和参数修改应能一次撤销，重做及保存重载恢复同一结果 |
| 失败和取消有具体场景 | `modernVideoExporter.cancellation.test.ts`、`export-error-report.spec.ts` | 验证取消后不继续启动编码、失败后可重试、旧成片得到保留；按实际改动选择场景 |

Myrecordly 仓库使用 Vitest 做逻辑测试，Playwright 启动 renderer-only Vite 页面做 UI 自动化。本次人工交互使用内置浏览器，复用其桌面桥接与测试视频，没有运行其整套 Playwright 测试。

## 浏览器的适用范围

Myrecordly 的 Electron/React 界面可在浏览器运行；DogSC 当前是原生 WPF，没有浏览器可访问的编辑器。网页界面可直接用内置浏览器点击、输入、定位和截图；WPF 使用原生窗口及现有 `DogSC.UiChecks`。不为通过浏览器检查另外制作一套替代界面。

设备录制、系统声音和 FFmpeg 输出需要真实 Windows/媒体链路。浏览器中的测试桥接、控件选中状态或“导出成功”提示都不能替代真实文件检查。

## 按改动选择检查

### 编辑与快捷键

载入临时短视频，定位到片段中部，执行分割、一次撤销和重做；检查片段区间、播放头和预览。快捷键分别在工作区、文本框、滑块和播放按钮获得焦点时操作。联动布局检查两条轨道一起变化，左右配对独立，保存重载不丢失关系。

现有检查入口：

```powershell
dotnet test windows/tests/DogSC.Core.Tests -c Release
dotnet run --project windows/tests/DogSC.UiChecks -c Release -- --shortcuts
```

`--shortcuts` 覆盖 WPF 布局、键盘处理、输入焦点隔离、联动分割、撤销重做和保存，也执行真实范围导出及剪切成片对比。它包含直接调用键盘处理方法的检查，不能据此宣称所有真实鼠标/系统输入路径都已验证。

### 画面与导出

涉及运镜、聚焦、摄像头或时间线时，对比原片与编辑后保留区间的帧数和画面，覆盖相关进入、保持和退出位置。现有 30/60fps 合成素材检查已抓到“分割聚焦效果后逐次多出尾帧”的问题；继续保留这个回归场景。

涉及聚焦渲染时可运行：

```powershell
dotnet run --project windows/tests/DogSC.UiChecks -c Release -- --focus-parity
```

小尺寸短视频验证几何、时钟与合成；长录制、高分辨率性能和真实硬件按实际需要单独验证。缺少设备时写明未验证的范围，继续其他工作。

## 本次浏览器体验

使用 Myrecordly 的真实编辑器和 6 秒合成视频，在其测试配置的 1440×1000 视口操作：

- 约 3 秒处分割后显示左右两个片段；一次撤销恢复完整片段，重做可再次分割。
- 打开发布面板，选择本地；切换 GIF 后出现循环选项，切回 MP4 后可选择 60fps。
- 本次验证界面交互，没有执行 Myrecordly 实际录制、登录、云发布或成片导出。

临时测试入口、启动脚本、缓存和服务在体验结束后清理。后续产生的临时项目、截图与视频同样在检查结束后清理；需要交付的证据单独作为明确的产物保留。
