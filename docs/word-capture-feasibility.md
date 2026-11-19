# 划词翻译与屏幕取词可行性评估（待评审）

> 目的：评估在 Windows 平台为 Flint 增加类似有道词典的"划词翻译"与"屏幕取词"能力，是否可行、走哪条技术路线、分几期做。
> 依据：本仓当前代码（`master`，版本号 3.5.0）。本轮未新增依赖、未改产品代码、未做真机 OCR/UIA 实测。
> 口径：标"代码事实"的均可对到文件；标"估算"的给出推导；标 **待确认** 的需实现前补测或由产品拍板。

---

## 0. 结论摘要

- **两个功能都可行**，且与现有架构接合度高：全局快捷键（`HotkeyService`）、按键注入（`InputSimulator`）、置顶迷你窗（`LiteWindow`）、本地毫秒级查词（`WordQueryService`）、AI 整句翻译（`LlmTranslationService`）全部已就绪，缺的只是"从别的应用拿到文本"这一环。
- **划词翻译推荐 MVP 走"快捷键 + 模拟 Ctrl+C + 读剪贴板"**：零新增 NuGet 依赖，复用现有三个组件即可闭环。有道式的"选中即自动取词"（UI Automation 路线）可行但复杂度高一档，放二期。
- **屏幕取词推荐走 `Windows.Media.Ocr` + GDI 截屏**：OCR 引擎是 Windows 10+ 系统自带 WinRT API，当前 TFM（`net10.0-windows10.0.26100.0`）直接可用，免打包应用可调用，零新增依赖；`OcrWord.BoundingRect` 提供词级坐标，能支撑"点哪个词查哪个词"的取词交互。
- **不建议做"鼠标悬停 continuous 取词"**（有道经典模式）：需要持续截屏 + OCR 或全局 UIA 遍历，性能与兼容性代价大，收益可被"快捷键触发区域取词"覆盖大部分。
- 主要风险不在技术而在环境：OCR 语言包是否安装、管理员权限窗口（UIPI）取不到词、剪贴板管理器冲突、部分应用（Chrome/Electron）UIA 默认不暴露文本。均有降级路径，详见 §4。
- 与现有约束兼容：免打包（`WindowsPackageType=None`）、仅 x64、Core/Data NativeAOT（L1）均不受影响；新代码只要避开反射式 COM 互操作包即可维持 AOT 兼容。

---

## 1. 现有可复用基础（代码事实）

| 能力 | 位置 | 现状 | 对新功能的作用 |
|---|---|---|---|
| 全局快捷键 | [HotkeyService.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/HotkeyService.cs) | `WH_KEYBOARD_LL` 低级钩子，支持 Win/Ctrl/Shift/Alt 组合，命中后吞键并抑制开始菜单 | 划词/取词的触发入口，直接 `Register` 新组合键 |
| 按键注入 | [InputSimulator.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/InputSimulator.cs) | `SendInput` 封装，`dwExtraInfo` 可让自家钩子忽略注入键 | 模拟 Ctrl+C 复制选中文本；注入键不会被自己的热键钩子误判 |
| 键盘钩子基建 | [KeyboardHook.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/KeyboardHook.cs) | 静态持有委托防 GC、函数指针 thunk、安装失败降级 | 若二期加鼠标钩子（`WH_MOUSE_LL`）可照此模式扩展 |
| 置顶迷你窗 | [LiteWindow.xaml.cs](file:///d:/workingfold/Flint/Flint3/LiteWindow.xaml.cs) | `IsAlwaysOnTop`、不进 Alt-Tab、无标题栏、可聚焦搜索框 | 划词/取词结果的弹出载体，无需新窗口类型 |
| 本地查词 | [WordQueryService.cs](file:///d:/workingfold/Flint/Flint3.Core/Query/WordQueryService.cs) | 多源并行、毫秒级返回 | 取词拿到单词后直接查本地词典 |
| AI 翻译 | [LlmTranslationService.cs](file:///d:/workingfold/Flint/Flint3.Core/Translation/LlmTranslationService.cs) | OpenAI 兼容协议，独立服务 | 划词选中整句时走翻译而非查词 |
| P/Invoke 生成 | 两项目的 `NativeMethods.txt` + CsWin32 0.3 | 已含 `GetCursorPos`、`MonitorFromPoint`、`GetDpiForMonitor` 等 | 截屏/剪贴板/鼠标钩子所需 Win32 API 按需追加即可，AOT 友好 |
| 快捷键设置 UI/持久化 | [MainViewModel_Shortcut.cs](file:///d:/workingfold/Flint/Flint3/ViewModels/MainViewModel_Shortcut.cs) | 默认 Alt+Space，设置可改、JSON 落盘 | 新增"划词翻译键""屏幕取词键"沿用同一套模式 |

**缺口**（全仓 grep 确认）：目前没有任何剪贴板、OCR、UI Automation 相关代码；`InputSimulator` 只有单键注入，没有组合键序列封装。

**环境约束**（代码事实）：
- 免打包分发（`WindowsPackageType=None`，[Flint3.csproj](file:///d:/workingfold/Flint/Flint3/Flint3.csproj)），无 MSIX 包身份。剪贴板、`Windows.Media.Ocr`、GDI 截屏、UIA 均不要求包身份，**不受影响**。
- 仅 x64；`SupportedOSPlatformVersion=10.0.22000`（Win11）。本文所有系统 API 的下限都低于此，无版本冲突。
- AOT 现状见 [aot-report.md](file:///d:/workingfold/Flint/docs/aot-report.md)：Core/Data 真 NativeAOT，UI 走 R2R + CsWinRT AOT。新增 Win32 API 走 CsWin32、WinRT API 走内置投影，均 AOT 兼容；**不要引入反射式互操作包**（如老式 `Interop.UIAutomationClient`）。

---

## 2. 划词翻译：三条路线对比

### 路线 A：快捷键 + 模拟 Ctrl+C + 读剪贴板（推荐 MVP）

流程：用户选中文字 → 按热键 → 注入 Ctrl+C → 轮询读剪贴板文本 → 单词走 `WordQueryService`、整句走 `LlmTranslationService` → `LiteWindow` 在光标附近弹出。

- **可行性：高**。所有环节都有现成组件或 BCL/CsWin32 API：
  - 组合键注入：扩展 `InputSimulator` 支持按下 Ctrl → 按下 C → 抬起 C → 抬起 Ctrl（估算 <30 行）。
  - 读剪贴板：WinRT `Windows.ApplicationModel.DataTransfer.Clipboard`（TFM 内置投影，免打包可用）或 CsWin32 `OpenClipboard/GetClipboardData`。前者更省事。
- **必须处理的细节**（估算，均有成熟做法）：
  1. **时序**：注入 Ctrl+C 后目标应用异步写剪贴板，需带超时轮询（如 50ms × 10 次），并先清空或记录剪贴板序号（`GetClipboardSequenceNumber`）判断"确实更新了"。
  2. **现场保护**：读完后恢复原剪贴板内容（至少支持文本/图片格式），否则用户复制的东西被覆盖。恢复本身有竞态，**待确认**是否做到完美恢复还是只恢复文本。
  3. **剪贴板历史污染**：Win+V 历史会记下这次注入的复制。可写入 `ExcludeClipboardContentFromMonitorProcessing` 等标记格式规避，**待确认**各 Windows 版本行为。
  4. **线程**：剪贴板 API 需 STA/UI 线程调用；热键回调在钩子线程，须 `Dispatcher.TryEnqueue` 切回（现有 `OnShortcutActivated` 已是这个模式）。
  5. **自吞**：注入键带 `dwExtraInfo` 标记，现有钩子过滤器需同样忽略（`KeyboardHook` 已支持 extraInfo 语义）。
- **覆盖盲区**：不支持 Ctrl+C 的应用（部分游戏、自绘控件、PDF 扫描件）取不到 → 正好由屏幕取词（§3）兜底。
- **权限边界**：目标窗口以管理员运行而 Flint 未提权时，UIPI 阻止 `SendInput` 送达，**取词静默失败**。需检测并提示（不推荐默认要求提权）。
- **工作量估算：小**（1 个服务类 + 热键注册 + LiteWindow 定位弹出 + 设置项，无新依赖）。

### 路线 B：鼠标钩子 + UI Automation 自动获取选区（有道式"选中即译"，二期）

流程：`WH_MOUSE_LL` 监听左键抬起 → 用 UIA `TextPattern.GetSelection()` 直接读当前焦点控件选中文本，不碰剪贴板。

- **可行性：中**。技术上成立（PowerToys、Ditto 类工具均用 UIA），但：
  - UIA COM 接口需经 CsWin32 生成（`CUIAutomation`/`IUIAutomationTextPattern`）或手写 vtable 互操作，工作量明显大于路线 A；CsWin32 生成的 COM 是 AOT 友好的。
  - **覆盖率是硬伤**：Chrome/Edge/Electron 应用默认不暴露完整 UIA 文本树（浏览器需检测到辅助技术客户端才开启无障碍），首次查询可能慢或为空；WPF/WinForms/Win32 原生控件覆盖良好。
  - UIA 跨进程调用可能挂起（目标进程无响应时），必须放后台线程 + 超时，否则拖死钩子/输入。
  - "选中即弹"打扰性强，需要按应用白名单/黑名单、双击 Ctrl 之类的轻量触发等交互设计，**待确认**产品形态。
- **建议**：作为路线 A 的增强（A 取不到时尝试 UIA；或提供"自动划词"开关），不做 MVP。其中"A 取不到时尝试 UIA"已落地（见 §9）；"自动划词"开关已落地（默认关闭、opt-in，见 §10）。
- **工作量估算：中到大**。

### 路线 C：鼠标抬起自动注入 Ctrl+C（不推荐）

每次选中都自动复制，等于全程劫持用户剪贴板，与复制粘贴工作流冲突严重，且误触发率高。仅作为记录，不建议实现。

---

## 3. 屏幕取词：OCR 路线评估

### 3.1 OCR 引擎选型

| 选项 | 依赖 | 免打包可用 | AOT | 结论 |
|---|---|---|---|---|
| **`Windows.Media.Ocr`（系统自带）** | 无（TFM 内置 WinRT 投影） | 是 | 兼容（CsWinRT） | **推荐** |
| Tesseract（如 `tesseract` NuGet + 原生 dll） | 原生二进制 + 语言数据 ~20MB+ | 是 | 需验证包装层 | 备选，违背项目轻依赖倾向 |
| PaddleOCR / 云 OCR | 重依赖或网络+费用 | — | — | 不推荐：本地离线场景不该引入 |

`Windows.Media.Ocr` 关键事实（系统 API 文档口径）：
- Win10 10240+ 可用，低于本项目 Win11 下限，无版本问题。
- `OcrEngine.TryCreateFromUserProfileLanguages()` / `AvailableRecognizerLanguages`：**依赖系统已安装对应语言的 OCR 语言包**。英文包通常随语言自带；中文 OCR 是"可选功能"，精简系统可能没有 → 启动时检测并引导安装（设置 → 语言 → 可选功能），**待确认**目标用户群的安装率。
- `OcrResult.Lines[].Words[]` 带 `BoundingRect`（词级屏幕坐标）→ 可实现"OCR 后点选/就近命中单词"，这是取词交互的核心。
- 有 `MaxImageDimension` 上限（约 2600px 量级，**待确认**精确值），4K 全屏截图需缩放或分块；区域截图（取词典型场景）远小于上限，无压力。
- 识别质量：对屏幕渲染的清晰文本效果好；小字号、低对比度、艺术字弱于云 OCR。取词场景（正文屏幕文本）属于其强项。

### 3.2 截屏方式

| 选项 | 说明 | 结论 |
|---|---|---|
| **GDI `BitBlt`（CsWin32）** | 对屏幕 DC 拷贝指定矩形，代码量小；拿不到 DWM 独占/受保护内容（极少数场景黑屏） | **MVP 推荐** |
| `Windows.Graphics.Capture` | Win10 1903+，可按窗口/显示器捕获，免打包可用（需 HWND interop）；帧池模型复杂 | 二期若要"窗口级取词"再上 |

多显示器 + 混合 DPI：`NativeMethods.txt` 已有 `GetCursorPos`/`MonitorFromPoint`/`GetDpiForMonitor`，物理像素坐标换算有现成 API 可用（代码事实）。

### 3.3 交互形态建议

- **MVP：快捷键触发区域取词**。按热键 → 冻结当前屏幕（全屏置顶窗口贴一张截图）→ 拖选区域 → OCR → 按点击位置/词框命中单词 → `LiteWindow` 显示释义；命中整句可转 AI 翻译。
- 简化变体：热键直接 OCR 光标周围固定半径区域（如 300×100px），免拖选，交互更快，作为默认模式可选。
- **不做**：鼠标悬停连续取词。估算：每次 OCR 10–50ms 量级 + 截屏开销，跟随鼠标持续跑会显著占 CPU；UIA 悬停取词则回到 §2 路线 B 的覆盖率问题。收益/代价比差。

### 3.4 工作量估算

中等：截屏封装（小）+ 区域选择覆盖窗（中，WinUI 全屏无边框 + 鼠标拖选）+ OCR 封装与词命中（小）+ 结果弹出复用 LiteWindow（小）+ 语言包检测/设置项（小）。无新增 NuGet 依赖。

---

## 4. 风险清单与降级路径

| 风险 | 影响 | 降级/对策 |
|---|---|---|
| OCR 语言包未安装（尤其中文） | 屏幕取词不可用 | 启动检测 `AvailableRecognizerLanguages`，设置页提示安装路径；英文用户基本无感 |
| 管理员窗口 UIPI | 划词 Ctrl+C 送不进去 | 检测前台窗口提权状态，提示"以管理员身份运行 Flint 后可取词"；不默认提权 |
| 剪贴板被其他管理器占用/恢复竞态 | 偶发取词失败或覆盖用户剪贴板 | 重试 + 序号校验；恢复失败时静默放弃并写 Trace |
| Chrome/Electron UIA 文本缺失（二期） | 自动划词覆盖率不足 | 路线 A 兜底；UIA 仅作增强 |
| 全屏游戏/DRM 内容截屏黑屏 | 取词失败 | 提示不支持；GDI 换 Graphics Capture 也只能部分缓解 |
| 注入键被安全软件拦截 | 划词失效 | 与现有热键钩子同等暴露面，无新增风险类型 |
| AOT 兼容 | 破坏 L1 发布 | 只用 CsWin32 + 内置 WinRT 投影；CI 保持 Core AOT 发布验证 |

---

## 5. 分期建议

| 期 | 内容 | 依赖 | 估算规模 | 状态 |
|---|---|---|---|---|
| P1 | 划词翻译 MVP：热键 + Ctrl+C + 剪贴板 + LiteWindow 弹出（单词查词典 / 整句走已配置的 LLM） | 无新依赖 | 小 | **已实现**（2026-11-19，见 §7） |
| P2 | 屏幕取词 MVP：热键 + 区域截图 + `Windows.Media.Ocr` + 词级命中 + 语言包检测 | 无新依赖 | 中 | **已实现**（2026-11-19，见 §8） |
| P3 | UIA 增强：自动划词开关、划词失败时 UIA 兜底、（可选）悬停取词实验 | CsWin32 生成 UIA COM | 中到大 | **已实现**（UIA 兜底见 §9；自动划词开关见 §10，2026-11-19）；悬停取词收益/代价比差，不做（见 §4） |

P1/P2 互不阻塞，可并行或按用户呼声排序。

---

## 6. 待确认清单（实现前补测）

1. ~~目标机器中英文/中文 OCR 语言包实际安装率；`OcrEngine.MaxImageDimension` 精确值。~~ **已实测**（2026-11-19，见 §8）：本机仅 zh-Hans-CN 语言包；MaxImageDimension=10000；zh 引擎可识别英文。
2. 剪贴板恢复策略：只恢复文本是否可接受；`ExcludeClipboardContentFromMonitorProcessing` 在 Win11 各版本对 Win+V 历史的实际效果。
3. 典型目标应用（Chrome、VS Code、Word、微信、PDF 阅读器）的 Ctrl+C 可达性与 UIA 文本覆盖率抽查。
4. ~~产品形态拍板：划词默认热键组合；取词默认"拖选区域"还是"光标就近"；是否要做按应用黑名单。~~ **部分已定**（2026-11-19）：划词默认热键 Ctrl+Alt+D（P1）；自动划词默认关闭、opt-in，设置页 + 托盘右键两处开关（§10）。按应用黑名单仍未做。
5. 整句划词是否默认走 LLM（涉及费用与隐私提示，沿用 [llm-translation-requirements.md](file:///d:/workingfold/Flint/docs/llm-translation-requirements.md) 的"默认关闭 + 明示外发"原则）。

---

## 7. P1 实现记录（2026-11-19）

按路线 A 落地，零新增 NuGet 依赖。与评估稿的差异：划词开关**默认开启**（热键需主动按下才取词，无自动行为）；整句且 AI 翻译已开启时弹**主窗**并自动发起翻译（整句卡/AI 面板只在主窗），其余情况弹光标旁的简洁窗。

| 模块 | 文件 | 内容 |
|---|---|---|
| 取词服务 | [SelectionCaptureService.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/SelectionCaptureService.cs) | 前台窗口属本进程则跳过；记录剪贴板序号 → 抬起按住的修饰键 → 注入 Ctrl+C → 50ms×12 轮询读文本 → 序号变化才恢复原剪贴板（文本 + DIB 图片），恢复内容写 `ExcludeClipboardContentFromMonitorProcessing` 避免 Win+V 历史重复；`Interlocked` 保证全局单在途 |
| 按键注入 | [InputSimulator.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/InputSimulator.cs) | 新增注入标记 `0x464C494E`、`SendCtrlC`、`ReleaseHeldModifiers`（抬 Win 后补 dummy keyup 防开始菜单） |
| 钩子过滤 | [HotkeyService.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/HotkeyService.cs) | 过滤器忽略带注入标记的按键，防止自家注入触发热键 |
| 热键与路由 | [MainViewModel_Selection.cs](file:///d:/workingfold/Flint/Flint3/ViewModels/MainViewModel_Selection.cs) | 默认 **Ctrl+Alt+D**，可配置；与唤醒热键同组合键时让位；单词→简洁窗查词，整句+AI 开启→主窗 `TryTranslateText`，取词失败→简洁窗提示 |
| 窗口 | [App.xaml.cs](file:///d:/workingfold/Flint/Flint3/App.xaml.cs)、[LiteWindow.xaml.cs](file:///d:/workingfold/Flint/Flint3/LiteWindow.xaml.cs) | `ShowLiteWindow(atCursor)` 光标右下弹出、超工作区翻转；`TrySetSearchTextBoxText` 回填走既有 TextChanged 查词链路 |
| 设置 | [SelectionTranslationSettings.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/SelectionTranslationSettings.cs)、[SettingsPage.xaml](file:///d:/workingfold/Flint/Flint3/Views/SettingsPage.xaml) | 开关 + 热键，JSON 存 `selectionsettings`（源生成序列化）；设置页"行为"区新增两张卡 |

**已验证**（真机自动化探针 + 单元测试）：
- 记事本选词 → 取词服务返回正确文本，剪贴板原内容（文本）完整恢复；
- 端到端：自建窗口选词 → 注入 Ctrl+Alt+D → 简洁窗弹出且搜索框为选中词、剪贴板恢复，连续两次通过；Alt+Space 唤醒对照正常；
- `dotnet build` 0 错误；Core 测试 123/123 通过（含新增设置模型序列化测试）；CLI NativeAOT 发布仍成功（L1 不回归）。

**未验证/遗留**：整句→LLM 路径需用户已配置 AI 接口后手测；管理员窗口（UIPI）、不支持 Ctrl+C 的应用目前统一落到"未获取到选中文本"提示，未做细分归因；剪贴板仅恢复文本与 DIB，其他格式（如文件列表）会被复制动作覆盖。

---

## 8. P2 实现记录（2026-11-19）

按 §3 推荐路线落地：热键冻结屏幕 → 拖选区域或单击文字 → `Windows.Media.Ocr` → 词级命中/整段文本 → 复用 P1 的结果路由。零新增 NuGet 依赖。

与评估稿的差异/交互拍板：
- **拖选 + 单击混合交互**（覆盖 §3.3 两种形态，无需模式开关）：拖选区域 OCR 整段文本（区域内仅一个词时取该词）；单击则 OCR 光标邻域（±160×48 DIP）并按矩形边缘距离命中最近词（≤24 DIP）。
- 覆盖窗只覆盖**光标所在显示器**（多显示器 MVP 限制）；Esc / 右键 / 再按一次热键取消。
- 默认热键 **Ctrl+Alt+S**，可配置；同组合键冲突优先级：唤醒 > 划词 > 屏幕取词（后者让位）。
- OCR 引擎 `TryCreateFromUserProfileLanguages()`；无语言包时简洁窗提示去"设置 → 时间和语言 → 可选功能"安装。
- 结果路由完全复用 P1：单词/短语→光标旁简洁窗查词典；整句且 AI 翻译开启→主窗 `TryTranslateText`；未识别到文字→简洁窗提示。

| 模块 | 文件 | 内容 |
|---|---|---|
| 截屏 | [ScreenCaptureService.cs](file:///d:/workingfold/Flint/Flint3.Core/Capture/ScreenCaptureService.cs) | GDI `BitBlt`（SRCCOPY\|CAPTUREBLT）+ 自上而下 DIB → BGRA 字节；`Crop` 边界夹取；`EncodeBmp` 供覆盖窗背景与 OCR 缩放路径解码 |
| 词命中 | [ScreenWordHitTest.cs](file:///d:/workingfold/Flint/Flint3.Core/Capture/ScreenWordHitTest.cs) | 点到词框边缘距离最近者命中，平局取阅读顺序先出现者 |
| 设置模型 | [ScreenCaptureSettings.cs](file:///d:/workingfold/Flint/Flint3.Core/Capture/ScreenCaptureSettings.cs) | 开关 + 热键，JSON 存 `screencapturesettings`（源生成序列化） |
| OCR 封装 | [OcrService.cs](file:///d:/workingfold/Flint/Flint3/Helpers/OcrService.cs) | `byte[]`→`SoftwareBitmap`(Bgra8)→`RecognizeAsync`；超 `MaxImageDimension` 时 `BitmapTransform` 缩放并把词坐标换算回原图 |
| 覆盖窗 | [ScreenCaptureWindow.xaml](file:///d:/workingfold/Flint/Flint3/ScreenCaptureWindow.xaml) / [.xaml.cs](file:///d:/workingfold/Flint/Flint3/ScreenCaptureWindow.xaml.cs) | WindowEx 置顶无边框，`MoveAndResize` 到显示器物理矩形，DWM 关圆角；DIP↔物理像素按 `ActualWidth` 比例换算；`CrosshairGrid` 十字光标 |
| 热键与路由 | [MainViewModel_ScreenCapture.cs](file:///d:/workingfold/Flint/Flint3/ViewModels/MainViewModel_ScreenCapture.cs) | 注册/让位/持久化；划词热键变化后经 `UpdateSelectionRegistration` 的 finally 联动校准 |
| 设置页 | [SettingsPage.xaml](file:///d:/workingfold/Flint/Flint3/Views/SettingsPage.xaml) | "屏幕取词"开关 + "取词快捷键"两张卡 |

**实测环境**（回答 §6.1）：
- 本机 OCR 语言包仅 `zh-Hans-CN`（无独立英文包）；`MaxImageDimension=10000`（评估稿估算的 ~2600 偏保守），单 monitor 区域截图不会触发缩放。
- zh 引擎可识别英文：Arial 渲染分词正常；Segoe UI 小字号偶发分词错误（"q uick"/"foxjumps"），属引擎特性，应用侧不干预。中文按单字分词，单击中文取到单字后走既有中文反查链路。

**已验证**（真机自动化探针 + 单元测试）：
- 端到端连续两轮通过：单击取词→简洁窗搜索框为选中词（serendipity）；Esc 取消覆盖窗且无残留弹窗；拖选整句→按已开启的 AI 翻译路由到主窗（本机已配置 AI 接口，`TryTranslateText` 路径实测走通，顺带覆盖了 P1 遗留的整句→LLM 路由验证）。
- `dotnet build` 0 错误；Core 测试 139/139 通过（新增 16 个：裁剪/BMP 编码/词命中/设置模型）；CLI NativeAOT 发布仍成功（L1 不回归）。
- 测试干扰归因：主窗（搜索框含整句）遮挡目标文字时，OCR 识别的是屏幕实际内容——所见即所得，属预期行为而非缺陷。

**未验证/遗留**：多显示器 + 混合 DPI 仅单 monitor 实测；GDI 拿不到 DWM 独占/DRM 内容（黑屏）未细分归因；全屏游戏场景未测；覆盖窗弹出期间不监听鼠标悬停（设计如此，见 §3.3"不做"）。

---

## 9. P3 实现记录（UIA 兜底，2026-11-19）

按 §2 路线 B 的"作为路线 A 增强"建议落地**划词失败时 UIA 兜底**这一项；"自动划词开关"另见 §10，"悬停取词"收益/代价比差不做（§4）。零新增 NuGet 依赖。

行为：划词热键触发后仍先走 P1 的 Ctrl+C + 剪贴板路径；**仅当剪贴板没取到文本时**（应用不响应复制快捷键、剪贴板被占用等），追加 UIA `TextPattern.GetSelection()` 直接读焦点控件选区。UIA 路径不碰剪贴板，因此不会覆盖用户剪贴板、也不产生 Win+V 历史。对正常支持 Ctrl+C 的应用零影响（兜底不触发）。

| 模块 | 文件 | 内容 |
|---|---|---|
| UIA 读取 | [UiaSelectionReader.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/UiaSelectionReader.cs) | CsWin32 生成 UIA COM（指针式 vtable 结构，AOT 友好）：`CUIAutomation.CreateInstance` → `GetFocusedElement` → `GetCurrentPatternAs(TextPatternId)` → `GetSelection` → 逐 range `GetText(-1)`（最多 32 段，`\n` 连接）；独立后台线程 + 1500ms 超时，目标进程挂起时放弃该线程不阻塞主流程；COM 初始化/释放、BSTR `SysFreeString` 全程配对 |
| 兜底接入 | [SelectionCaptureService.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/SelectionCaptureService.cs) | `CaptureCore` 在恢复剪贴板后、返回前，若文本为空则调 `UiaSelectionReader.TryReadSelection()`；自窗口保护、单在途、剪贴板恢复逻辑均不变 |
| P/Invoke | [NativeMethods.txt](file:///d:/workingfold/Flint/Flint3.Core/NativeMethods.txt) | 追加 `CUIAutomation`/`IUIAutomation`/`IUIAutomationElement`/`IUIAutomationTextPattern`/`IUIAutomationTextRange`/`IUIAutomationTextRangeArray`/`TreeScope`/`UIA_PATTERN_ID`/`CoInitializeEx`/`CoUninitialize`/`COINIT` |

**已验证**（真机跨进程自动化探针 + 单元测试）：
- 跨进程 UIA 直读：目标窗口选中 serendipity → 主进程 `UiaSelectionReader` 读到 serendipity；无选区 → 返回 null（不返回整段文本）。
- 正常路径回归：目标窗口选词 → `CaptureAsync` 经 Ctrl+C 取到 serendipity（兜底未触发，P1 行为不变）。
- **兜底端到端**：目标进程锁住剪贴板使 Ctrl+C 无法写入 → `CaptureAsync` 仍经 UIA 取到 serendipity，证明失败兜底链路打通。
- `dotnet build` 0 错误；Core 测试 139/139 通过；CLI NativeAOT 发布仍成功（L1 不回归，UIA COM 结构 AOT 兼容）。

**未验证/遗留**：
- 覆盖率硬伤仍在（§2 路线 B）：Chrome/Edge/Electron 默认不暴露完整 UIA 文本树，兜底对这些应用可能仍取不到——此时落到 P1 的"未获取到选中文本"提示，或改用屏幕取词（P2）。典型应用（Chrome、VS Code、Word、微信、PDF）的 UIA 覆盖率抽查仍属 §6.3 待办。
- 超时放弃的线程若目标进程长期挂起会滞留（后台线程，进程退出即回收），未做强制中止；1500ms 超时为经验值，未做大规模应用时延统计。
- 管理员窗口（UIPI）下 UIA 跨进程读取同样可能受限，未细分归因。

---

## 10. P3 实现记录（自动划词开关，2026-11-19）

落地 §2 路线 B 的"自动划词"开关：选中文字后自动取词查询，无需按快捷键。参考有道词典的交互，提供**设置页 + 托盘图标右键菜单**两处开关。零新增 NuGet 依赖。

产品决策（原 §6.4 待拍板项）：
- **默认关闭、opt-in**：自动行为打扰性强，须用户主动开启；与划词热键开关相互独立（关热键仍可开自动，反之亦然）。
- **手势判定**：左键按下 → 移动 ≥6px → 左键抬起，才算一次选词；过滤单击与手抖（平方距离 ≥36）。
- **防抖 800ms**：连续划选只触发一次取词。
- **失败静默**：自动模式取不到文本时不弹窗（区别于热键主动取词的失败提示），避免每次划选都打扰。
- **被动观察**：`WH_MOUSE_LL` 钩子始终 `CallNextHookEx` 放行，不吞任何鼠标事件；装在 UI 线程（有消息循环），回调切回 UI 线程发起取词。
- 取词与结果路由完全复用 P1 管线（Ctrl+C + 剪贴板 + UIA 兜底；单词→简洁窗、整句+AI→主窗）；前台属本进程时 `CaptureAsync` 自会跳过，故无需额外判断覆盖窗/简洁窗。

| 模块 | 文件 | 内容 |
|---|---|---|
| 鼠标钩子 | [MouseHook.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/MouseHook.cs) | `WH_MOUSE_LL` 被动钩子，函数指针 thunk + 静态委托列表防 GC；`MouseEvent`（消息号/屏幕坐标/注入标记）；始终放行 |
| 手势判定 | [SelectionGestureDetector.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/SelectionGestureDetector.cs) | 左键按下→移动≥6px→抬起的状态机，过滤单击/抖动，手势间状态重置 |
| 自动监视器 | [AutoSelectionMonitor.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/AutoSelectionMonitor.cs) | 组合钩子 + 手势判定，忽略自家注入标记，命中触发 `SelectionGesture` 事件；`Start`/`Dispose` 启停 |
| 开关与防抖 | [MainViewModel_Selection.cs](file:///d:/workingfold/Flint/Flint3/ViewModels/MainViewModel_Selection.cs) | `SelectionAutoEnabled` 属性（读写 `AutoEnabled` + 启停监视器 + 持久化）；`OnAutoSelectionGesture` 800ms 防抖后 `CaptureAndShowAutoAsync`（失败静默）；`Dispose` 释放监视器 |
| 设置模型 | [SelectionTranslationSettings.cs](file:///d:/workingfold/Flint/Flint3.Core/Input/SelectionTranslationSettings.cs) | 新增 `autoEnabled`（默认 false，源生成序列化） |
| 设置页 | [SettingsPage.xaml](file:///d:/workingfold/Flint/Flint3/Views/SettingsPage.xaml) | "行为"区新增"自动划词"卡（ToggleSwitch 双向绑定 `SelectionAutoEnabled`） |
| 托盘菜单 | [NotifyIcon.cs](file:///d:/workingfold/Flint/Flint3/Helpers/NotifyIcon.cs)、[App.xaml.cs](file:///d:/workingfold/Flint/Flint3/App.xaml.cs) | 右键菜单新增"自动划词"勾选项（`MF_CHECKED`/`MF_UNCHECKED` 反映当前状态），点击翻转开关；顺带修复菜单 `DestroyMenu` 缺失的句柄泄漏 |

**已验证**（真机跨进程自动化探针 + 单元测试）：
- 端到端（autoEnabled=true）：目标窗口单击不触发；拖选 serendipity → 简洁窗自动弹出且搜索框为 serendipity。
- 端到端（autoEnabled=false）：单击与拖选均不触发（开关确实生效）。
- 隔离钩子探针：真实拖选触发 `SelectionGesture`、单击不触发（Core 手势判定正确）。
- `dotnet build` 0 错误；Core 测试 146/146 通过（含新增手势判定 7 项 + 设置模型 `AutoEnabled` 序列化）；CLI NativeAOT 发布仍成功（L1 不回归）。

**未验证/遗留**：
- 覆盖率与打扰性权衡：自动模式对不支持 Ctrl+C 且 UIA 文本缺失的应用（Chrome/Electron 等）静默失败，用户无感知；按应用黑名单/白名单仍未做（§6.4）。
- 6px 阈值与 800ms 防抖为经验值，未做大规模手势误触/漏触统计；高 DPI 下阈值为物理像素，未随 DPI 缩放。
- 管理员窗口（UIPI）下注入 Ctrl+C 与 UIA 读取同样可能受限，统一落到静默失败。