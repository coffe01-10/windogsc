# README 素材来源

- `zh-CN/` 与 `en/` 下各有 `editor.png`、`motion.png`、`screen3d.png`、`effects.png`、`export.png`：2026-10-11 从本仓库当前 Windows / WPF 开发版重新采集，分别启用程序原生的中文和英文语言设置。根目录 `README.md` 使用中文图片，`README.en.md` 使用英文图片，`windows/README.md` 使用中文图片。
- 被编辑的视频中的 NORTH 工作台为专门绘制的中英文演示内容，客户姓名、邮箱和数据均为虚构；邮箱使用 `example.com`。这部分是源视频内容，产品控件来自真实程序。演示项目仅用于截图，拍摄后删除。
- `platform.svg`、`runtime.svg`、`license.svg`：本地静态 SVG，分别表示当前发布目标、应用技术栈与项目许可，不表示实时 CI 状态。
- 根 README 使用的应用图标 `Resources/AppIconMaster.png` 继承自 [laogou717/DogSC](https://github.com/laogou717/dogsc)，项目按根目录 MIT 许可证保留上游版权声明。
- 图片由实际 WPF 控件通过 `RenderTargetBitmap` 以 192 DPI（2× 像素密度）直接绘制，再以 `PngBitmapEncoder` 无损保存，替换此前经过有损窗口捕获的图片。没有放大旧截图、拼接控件或生成产品 UI。概览保留完整工作区；运镜、3D 与柔化图聚焦预览及属性面板，裁掉桌面、窗口边框与无关时间线。导出图保留完整导出面板。
- 截图宿主加载当前应用程序集和独立临时演示项目，未修改已有核对项目或用户语言设置；拍摄后清理宿主与演示素材。
- `../../videos/dogsc-launch.mp4`：用户提供的 `DogSC_发布片_1080p60.mp4` 原片，62 秒，1920 × 1080，60 FPS，未重新编码。原片上传为本仓库的 [GitHub 媒体附件](https://github.com/user-attachments/assets/3c5d73f9-e42f-4c88-be03-00b7a0ee2208)；两份 README 用独立段落中的附件地址嵌入 GitHub 原生播放器，可在主页内播放。两份 README 使用同一部中文原片，未将其标为英文配音版。
- 更新界面后应重新拍摄两种语言的对应页面，并同步 `README.md`、`README.en.md`、`windows/README.md` 的入口与描述。`docs/images/` 下原有的 macOS 图片属于上游历史素材，当前 Windows README 不引用它们。
