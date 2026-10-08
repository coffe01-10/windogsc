// DogSC Windows 内测版补齐工作流
// 共享实施代理按依赖顺序逐项落地功能；每项完成后跑一次确定性门禁
// （dotnet test → dotnet build → 自检），由脚本读退出码决定是否推进。
// 独立校验代理最后过一遍全部改动，产出中文实施报告。

interface ImplResult {
  /** 一句话结论：这项任务最终落到什么状态。 */
  summary: string;
  /** 实际改动或新建的文件，工作区相对路径。 */
  changedFiles: string[];
  /** 为这项改动新增或更新的测试名；没有则为空数组。 */
  testsAdded: string[];
  /** 没做完的部分及原因；完成为空字符串。 */
  remaining: string;
}

interface GateFailure {
  /** 失败阶段：test / build / selftest。 */
  stage: string;
  /** 该阶段 stdout+stderr 的末尾摘要。 */
  output: string;
}

interface ReviewVerdict {
  /** 整体是否可交付：true 表示改动自洽、无明显破坏。 */
  approved: boolean;
  /** 按文件列出的问题；没有则为空数组。 */
  problems: string[];
  /** 给用户的总体评价，两三句话。 */
  comment: string;
}

interface Finding {
  /** 位置，工作区相对路径，可带行号："windows/src/x.cs:42"。 */
  where: string;
  /** 一句话说明是什么问题或发现。 */
  what: string;
  /** 依据：读到的行，或证明它的命令与输出。 */
  evidence: string;
  /** verified=独立代理或确定性命令确认过；unconfirmed=未能确认。 */
  status: "verified" | "unconfirmed";
  /** high 仅用于数据丢失、崩溃或错误结果。 */
  severity: "low" | "medium" | "high";
}

interface WorkflowReport {
  /** 两三句话回答用户要什么。 */
  conclusion: string;
  findings: Finding[];
  /** 这次运行实际验证了什么、怎么验证的。 */
  verified: string[];
  /** 没看或没法检查的部分及原因。 */
  notCovered: string[];
}

function tailOf(r: { stdout: string; stderr: string }, lines: number): string {
  const all = (r.stdout + "\n" + r.stderr).split("\n").filter((l) => l.trim().length > 0);
  return all.slice(Math.max(0, all.length - lines)).join("\n");
}

// 任务看板：逐项任务的实施/验证状态，给正在看 run 的人一眼进度。
artifact.board("board", {
  title: "任务进度",
  key: "item",
  status: "status",
  columns: ["implemented", "verified", "blocked"],
  cardTitle: "item",
  detail: [
    { field: "summary", label: "结果" },
    { field: "remaining", label: "未完成" },
  ],
});

const SHARED_CONTEXT =
  "这是 laogou717/dogsc 仓库里正在开发的 DogSC Windows 版：.NET 8 + WPF 录屏剪辑工具，" +
  "代码在 windows/ 下（src/DogSC.App 是 WPF 界面与录制，src/DogSC.Core 是时间线/导出核心，" +
  "tests/DogSC.Core.Tests 是 xUnit 测试）。macOS 版在 Sources/ 下是 Swift，仅供对照，不要编译它。" +
  "目标是先出 Windows 内测版。工作语言用中文。你拥有完整编辑权限，可以直接改代码；" +
  "按仓库现有代码风格写，注释用中文、保持精简。每次动手前先读相关文件再改；" +
  "不要提交 git。若某步在你看来是矛盾的、不可能完成的，或者改动会大面积重写现有结构，" +
  "直接上报说明，不要硬改也不要伪造完成。";

// —— 任务清单：按依赖与风险排序，前四项是内测版必须先修的 ——
const tasks = [
  {
    key: "settings-save",
    title: "修复设置保存的 NaN 崩溃",
    prompt:
      "修复设置保存 bug：默认窗口坐标的 NaN 让 JSON 序列化失败，连带录音选项、主题、备忘录等设置全部保存不了。" +
      "先读 windows/src/DogSC.App/WindowPlacement.cs、SettingsWindow.xaml.cs、AppHost.cs、ProjectDocument.cs，" +
      "找到 NaN 从哪来、写 JSON 的地方在哪；用不会写入 NaN/Infinity 的方式修（序列化前清洗或用支持 NaN 的写入并回读校验），" +
      "保证加载缺失/损坏配置时能回退到默认值。如果核心序列化在 DogSC.Core，把回退逻辑放 Core 里并在 tests 里补一条" +
      "「构造含 NaN 的设置→保存→重新加载→字段为默认值」的测试。",
  },
  {
    key: "export-missing-tracks",
    title: "修复缺轨片段的导出",
    prompt:
      "修复导出对缺轨片段的处理：现在要求每段都有声音才导出音轨、每段都有摄像头才导出画中画，中间某段缺失会让整条输出出错或丢轨。" +
      "读 windows/src/DogSC.Core/ExportGraph.cs、ExportService.cs、RecordingSession.cs、ProjectDocument.cs，" +
      "把判断改成「任一段有声音→导出音轨，没有的片段补静音；任一段有摄像头→导出画中画，没有的片段跳过」，" +
      "并在 tests 里加「中间片段无声音/无摄像头时导出图仍生成且时长正确」的用例。ffmpeg 滤镜拼接改动要小心，" +
      "滤镜图改动后跑 dotnet test 验证。",
  },
  {
    key: "volume-parity",
    title: "统一音量预览与导出",
    prompt:
      "修复音量不一致：界面允许调到 200%，预览播放器限到 100%，导出按实际设置。读 PreviewPlayer.cs、EditorWindow.xaml.cs、" +
      "ExportGraph.cs/ExportService.cs，统一成预览也按真实增益播放（WPF MediaElement 的 Volume 只到 1，可用音量包络或" +
      "在预览里提示超过 100% 的部分以导出为准），并把上限常量收敛到一处。改动要小，不要为了这个重写播放器。",
  },
  {
    key: "window-capture",
    title: "窗口录制跟随目标窗口",
    prompt:
      "窗口采集目前只录窗口初始位置的桌面区域。改成录制期间跟随窗口移动：读 RecorderBarWindow.xaml.cs、RecordingSession.cs、" +
      "RegionOverlay.xaml.cs、Native.cs 里窗口选择与抓帧的部分，每帧（或按合理频率）取目标窗口当前矩形作为采集区域；" +
      "窗口最小化或关闭时录最后一帧或黑帧并在时间线上标记。真机窗口跟随没法用单元测试完全验证，把矩形跟随逻辑抽成可测的小函数，" +
      "至少保证构建通过、自检通过。若做不了真实逐帧跟随，先做到按定时间歇跟随，并在 remaining 里写清楚限制。",
  },
  {
    key: "timeline-editing",
    title: "补齐时间线编辑操作",
    prompt:
      "补齐时间线编辑：1) 拖动片段重排顺序；2) 拖动边界时吸附到相邻片段边界和标记点；3) 时间线上绘制音频波形" +
      "（可用 ffmpeg 抽峰值或 NAudio 读 wav 缓存，先做片段级简化波形）；4) 支持恢复已裁掉的片段（裁切不丢数据，" +
      "拖回边界可还原）。读 TimelineControl.cs、Timeline.cs、EditorWindow.xaml.cs。波形如果太重，" +
      "先做异步懒加载的简化版并在 remaining 里说明；前三项优先。",
  },
  {
    key: "smooth-zoom",
    title: "缩放平滑过渡与手动缩放动画",
    prompt:
      "手动缩放目前是硬切倍数。读 EditorWindow.xaml.cs、PreviewPlayer.cs、CanvasLayout.cs、ExportGraph.cs，" +
      "给缩放进出加平滑过渡动画（预览端用 WPF 动画即可；导出端在 ffmpeg 滤镜里按时间插值 zoompan/crop 表达式，" +
      "若导出端实现代价高可先只保证预览平滑并在 remaining 里标注导出为近似）。",
  },
  {
    key: "auto-zoom",
    title: "自动运镜",
    prompt:
      "实现自动运镜：根据录制时记录的鼠标位置和点击事件，自动在点击点生成缩放 + 跟随镜头。先查 RecordingSession.cs" +
      "里是否已经记录鼠标轨迹；若没有，录制时按低频采样记录光标位置与点击到项目文件。然后在编辑器时间线生成缩放片段" +
      "（复用手动缩放那套表示）。导出端用同样的插值路径。工作量大的部分可拆分：先保证「点击处自动缩放」可用，" +
      "平滑跟随若做不完写进 remaining。",
  },
  {
    key: "screen-3d",
    title: "屏幕 3D 效果",
    prompt:
      "加屏幕 3D：倾斜、透视、聚焦虚化。预览端用 WPF Viewport2DVisual3D 或等价变换做倾斜/透视，虚化用模糊效果；" +
      "导出端对应 ffmpeg 的 perspective/frei0r 或近似链。读 EditorWindow.xaml.cs、CanvasLayout.cs、ExportGraph.cs。" +
      "这是内测之后的功能，做到「编辑器里能调倾斜/透视参数并预览、导出不崩」即可，效果质量写进 remaining。",
  },
  {
    key: "intro-animation",
    title: "开场动画",
    prompt:
      "加开场动画：画面入场动效（淡入+轻微缩放/上移之类，做两三种预设可选）。预览端 WPF 动画，导出端 ffmpeg fade/zoompan。" +
      "规模控制在小特性，不接复杂时间轴。",
  },
  {
    key: "effect-reuse",
    title: "效果复制粘贴",
    prompt:
      "效果复用：选中一个效果片段可以复制，再粘贴到另一段；另加「只粘贴属性」（比如只粘缩放和背景不粘时长）。" +
      "读 Timeline.cs、EditorWindow.xaml.cs 里片段的表示，把效果属性抽成可序列化的小对象，加复制/粘贴/粘贴属性三个操作。" +
      "在 tests 里补序列化往返的用例。",
  },
  {
    key: "cursor-effects",
    title: "光标效果",
    prompt:
      "光标效果：自定义样式、点击反馈、静止隐藏、始终隐藏。目前光标是直接录进视频的——如果录制端无法剔除光标，" +
      "改成录制时叠加层不录光标、编辑期根据记录的轨迹重绘光标（依赖 auto-zoom 任务的轨迹记录，若没有则先补记录）。" +
      "先在设置/编辑器里加光标选项并在预览生效；导出端用叠加绘制。工作量大就先做「点击反馈+静止隐藏」两个，" +
      "其余写进 remaining。",
  },
  {
    key: "decorations",
    title: "画面装饰",
    prompt:
      "画面装饰：在现有纯色背景基础上加 1) 图片/壁纸背景；2) 贴图（可放一张图到画面上）；3) 局部柔化（模糊选中区域）；" +
      "4) 样机外框（简单设备边框即可）。读 CanvasLayout.cs、EditorWindow.xaml.cs、ExportGraph.cs。导出端对应 ffmpeg 的" +
      "overlay/gblur 链。四选三先做，样机外框最可后做。",
  },
  {
    key: "webcam-editing",
    title: "摄像头画中画编辑",
    prompt:
      "摄像头画中画目前是固定大小圆形、只能拖位置。加大小调节（拖角或滑杆）、布局选择（圆/方/无框，位置预设九宫格），" +
      "以及进入/退出动画。读 RecordingSession.cs、EditorWindow.xaml.cs、ExportGraph.cs 里画中画的部分。",
  },
  {
    key: "ci-release",
    title: "Windows 构建发布流水线",
    prompt:
      "补 Windows 的 GitHub Actions 构建/发布流程：读 .github/ 下现有 macOS 工作流和 build-app.sh，新增 windows 工作流：" +
      "windows-latest 上 dotnet restore/build/test/publish 出单文件或免安装 zip，把 ffmpeg 的获取（下载 essentials build 或" +
      "提示用户自带）和版本号写进流程，tag 推送时出 release 附件。本地没法真跑 CI，写完工作流 YAML 后用" +
      "dotnet publish 本地验证产物能产出，把本地验证命令与结果写进 summary。",
  },
  {
    key: "updater",
    title: "软件更新检测",
    prompt:
      "加 Windows 更新检测：启动时（或设置里手动点）查 GitHub releases 最新版本，有新版本提示并给下载链接；不要自动替换自身。" +
      "读 AppHost.cs、SettingsWindow.xaml.cs，做成小服务类+设置开关。",
  },
  {
    key: "project-compat",
    title: "项目兼容方案",
    prompt:
      "Windows 版打不开 macOS 的 .dogscproject。做一条转换路径：读 Sources/ 下 macOS 项目格式（Package.swift 相关模型）" +
      "和 windows/src/DogSC.Core/ProjectDocument.cs，写一个把 macOS 项目包转成 Windows 项目格式的导入器（命令行小工具或" +
      "编辑器里的「导入 macOS 项目」入口），至少映射片段、时长、缩放、背景这些共有字段；对不上的字段列出并丢弃。" +
      "若格式差异大到做不完，输出一份字段映射文档到 docs/ 并在 remaining 里写清差距。",
  },
];

const impl = agent("实施工程师", {
  system:
    SHARED_CONTEXT +
    "你是实施工程师：按 task 逐项实现，先读懂现状再动手，做完一项把结论、改动文件、新增测试写清楚。" +
    "每项任务做到「能编译、相关逻辑有测试（逻辑在 Core 里时）、不破坏既有行为」为止，不要追求一步到位的大而全。" +
    "你不需要自己跑 dotnet 命令——脚本会替你跑；你把代码改到自认为正确就返回，脚本用编译和测试来验收。" +
    "若你改了 ffmpeg 滤镜图（ExportGraph/ExportService），顺手在 DogSC.Core.Tests 里加对应的图结构断言。",
});

const outcomes: { key: string; title: string; result: ImplResult | null; gate: string }[] = [];
const allChanged = new Set<string>();
const allTests = new Set<string>();
let broken = false;

phase("逐项实施并验证每项改动");
for (const t of tasks) {
  if (broken) {
    outcomes.push({ key: t.key, title: t.title, result: null, gate: "skipped" });
    continue;
  }
  log(`开始：${t.title}`);
  const result = await impl.ask<ImplResult>(
    `当前任务：${t.title}\n\n${t.prompt}\n\n返回 summary/changedFiles/testsAdded/remaining。`,
  );
  outcomes.push({ key: t.key, title: t.title, result, gate: "pending" });
  report({ item: t.title, status: "implemented", summary: result.summary, remaining: result.remaining }, "board");
  for (const f of result.changedFiles) allChanged.add(f);
  for (const f of result.testsAdded) allTests.add(f);

  // 门禁：先 test（含 build），失败修；再 build（test 已过理论上能编），再自检。
  let gateFailure: GateFailure | null = null;
  for (let round = 1; round <= 3; round++) {
    phase("验证本次改动");
    const test = await world.run("dotnet", ["test", "windows/tests/DogSC.Core.Tests", "-v", "q", "-nologo"], {
      timeoutMs: 600000,
    });
    if (test.exitCode !== 0) {
      gateFailure = { stage: "test", output: tailOf(test, 25) };
    } else {
      const selftest = await world.run(
        "dotnet",
        ["run", "--project", "windows/src/DogSC.App", "--", "--self-test"],
        { timeoutMs: 600000 },
      );
      if (selftest.exitCode !== 0) gateFailure = { stage: "selftest", output: tailOf(selftest, 25) };
      else gateFailure = null;
    }
    if (gateFailure === null) break;
    phase("修复验证发现的问题");
    await impl.ask(
      `刚才「${t.title}」的改动没通过验证（第 ${round} 轮）。失败阶段：${gateFailure.stage}\n输出：\n${gateFailure.output}\n` +
        "修好它；如果是测试断言写错了改测试，如果是实现问题改实现。不要绕过验证。",
    );
  }
  const o = outcomes[outcomes.length - 1];
  if (gateFailure !== null) {
    o.gate = `failed:${gateFailure.stage}`;
    report(
      { item: t.title, status: "blocked", summary: `门禁失败(${gateFailure.stage})，停止后续任务`, remaining: gateFailure.output },
      "board",
    );
    broken = true;
  } else {
    o.gate = "passed";
    report({ item: t.title, status: "verified", summary: result.summary, remaining: result.remaining }, "board");
  }
}

// 总结：列出每个任务最终状态，供审查与报告使用
const taskSummaryLines = outcomes.map((o) => {
  if (o.result === null) return `- ${o.title}：未执行（前序任务门禁失败）`;
  return `- ${o.title}：${o.gate === "passed" ? "通过验证" : `未通过验证(${o.gate})`}。${o.result.summary}` +
    (o.result.remaining ? ` 未完成：${o.result.remaining}` : "");
});

phase("请人独立复核全部改动");
const review = await agent("复核工程师", {
  system:
    SHARED_CONTEXT +
    "你是复核工程师：只看别人改完的代码，不改任何文件。对照仓库现有结构和任务目标，找出改出来的问题：" +
    "逻辑错、边界漏、风格不一致、把不该改的改了。按文件给结论，问题列清楚；没有真问题就直说。",
}).ask<ReviewVerdict>(
  `下面是这次为 Windows 内测版逐项实施的改动清单，请独立复核（不要编辑任何文件）：\n\n` +
    `涉及文件：${[...allChanged].join(", ") || "无"}\n\n逐项结果：\n${taskSummaryLines.join("\n")}\n\n` +
    "重点看：改动的文件之间是否自洽、有没有破坏未列出的既有功能、export 滤镜图改动是否前后一致、" +
    "测试是否真覆盖了声明的行为。返回 approved/problems/comment。",
);

const findings: Finding[] = review.problems.map((p) => ({
  where: "",
  what: p,
  evidence: "复核工程师独立阅读改动后报告",
  status: "unconfirmed",
  severity: "medium",
}));

phase("整理最终结果并生成报告");
const finalTest = await world.run("dotnet", ["test", "windows/tests/DogSC.Core.Tests", "-v", "q", "-nologo"], {
  timeoutMs: 600000,
});
const finalSelf = await world.run(
  "dotnet",
  ["run", "--project", "windows/src/DogSC.App", "--", "--self-test"],
  { timeoutMs: 600000 },
);
const gateNote =
  `最终门禁：dotnet test 退出码 ${finalTest.exitCode}（${finalTest.exitCode === 0 ? "通过" : "失败"}），` +
  `自检退出码 ${finalSelf.exitCode}（${finalSelf.exitCode === 0 ? "通过" : "失败"}）。`;

const reportMd = [
  `# DogSC Windows 内测版补齐实施报告`,
  "",
  `## 门禁`,
  gateNote,
  "",
  `## 逐项结果`,
  ...taskSummaryLines,
  "",
  `## 独立复核`,
  review.comment,
  ...(review.problems.length > 0 ? ["", "### 复核提出的问题", ...review.problems.map((p) => `- ${p}`)] : []),
  "",
  `## 改动文件`,
  ...[...allChanged].map((f) => `- ${f}`),
  "",
  `## 新增/更新的测试`,
  ...(allTests.size > 0 ? [...allTests].map((f) => `- ${f}`) : ["- 无"]),
].join("\n");

try {
  await artifact.markdown("report", reportMd, {
    title: "实施报告",
    description: "逐项功能落地情况、验证结果与复核意见。",
    primary: true,
  });
} catch {
  // 报告发布失败不致命，return 里仍带结论
}

const verifiedList: string[] = [
  `dotnet test windows/tests/DogSC.Core.Tests（每轮门禁 + 最终一次，退出码 ${finalTest.exitCode}）`,
  `dotnet run --project windows/src/DogSC.App -- --self-test（每轮门禁 + 最终一次，退出码 ${finalSelf.exitCode}）`,
  `覆盖文件：${allChanged.size > 0 ? [...allChanged].join(", ") : "无改动"}`,
];
const notCovered: string[] = [
  "真实录屏与声音同步、暂停续录、摄像头、多显示器/不同 DPI、长时间录制、4K/60FPS 导出——需要真机验证，未做",
  "Windows 安装包/免安装包的实际安装与运行——CI 工作流仅本地 publish 验证",
  "复核列出的问题未逐条独立确认，标为 unconfirmed",
];
if (outcomes.some((o) => o.result === null))
  notCovered.push("部分任务因前序门禁失败未实施");

const reportResult: WorkflowReport = {
  conclusion:
    `DogSC Windows 内测版补齐已完成 ${outcomes.filter((o) => o.gate === "passed").length}/${tasks.length} 项；` +
    gateNote +
    (broken ? "其中一项未通过门禁，其后任务未继续。" : ""),
  findings,
  verified: verifiedList,
  notCovered,
};
return reportResult;
