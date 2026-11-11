# 燧石（Flint）重构计划

> 版本：v0.1（初稿）
> 日期：2026-11-09
> 依据：当前仓库 commit `60a22bb`，结论均以代码与一次实际构建为准，不依赖 README 描述。
> 适用范围：`Flint.sln` + 仓库内全部工程。

---

## 1. 现状结论（以代码为准）

### 1.1 仓库拓扑

| 目录 | 是否在 `Flint.sln` | 技术栈 | 定位 |
| --- | --- | --- | --- |
| `Flint/` | 否 | UWP（`Microsoft.NETCore.UniversalWindowsPlatform` 6.2.14） | 旧实现，第 1 版 UI |
| `Flint.Core/` | 否 | UWP 类库 | 旧实现，设置/存储工具 |
| `Flint.Data/` | 否 | UWP 类库 + `Microsoft.Data.Sqlite` 7.0.8 | 旧实现，StarDict 访问 |
| `Flint3/` | 是 | WinUI 3 / Windows App SDK 1.8，`net10.0-windows10.0.26100.0` | 当前产品 |
| `Flint3.Data/` | 是 | `net10.0-windows10.0.22000.0`，`IsAotCompatible=true` | 当前数据层 |
| `Flint3.Interop/` | 是 | C++/CLI（`CLRSupport=NetCore`）+ Win32 低级键盘钩子 | 当前快捷键实现 |

关键点：

1. `Flint.sln` 只包含 `Flint3`、`Flint3.Data`、`Flint3.Interop` 三个工程（[Flint.sln](file:///d:/workingfold/Flint/Flint.sln#L6-L11)），旧的 `Flint*` 三件套仍在 Git 中但已不在解决方案内，属于死代码。
2. 新旧两套代码的包标识相同：`22189NoMewing.6191006733C16` / `CN=E6D8FAFA-3EF4-4E47-B885-52144C67BFF5`。旧清单版本 `1.0.0.0`，新清单版本 `3.4.3.0`。**分发形态已定为免打包 unpackaged（见 §10.1），进程没有包标识，上述 Identity 与 MSIX 升级链路不再生效；`ApplicationData` 相关能力已回退到本地文件存储。**
3. 新旧代码没有共享工程，只有“复制后改造”的关系（`StarDictWordItem`、`StarDictDataAccess`、exchange 解析逻辑均各有一份）。

### 1.2 旧实现（Flint / Flint.Core / Flint.Data）

- UI：UWP `Windows.UI.Xaml` + WinUI 2（`Microsoft.UI.Xaml` 2.8.4）+ `Microsoft.Toolkit.Uwp.UI.Controls` 7.1.3。
- 目标平台：`TargetPlatformVersion=10.0.22621.0`，最低 `10.0.18362.0`；Release 使用 UWP .NET Native（`UseDotNetNativeToolchain=true`），不是 .NET NativeAOT。
- 功能面：单窗口 `MainPage`，三个搜索框；`QueryWord`（精确）与 `MatchWord`（前缀，limit=10）两套查询；设置页（主题、搜索框样式、英英释义开关）；无生词本、无托盘、无 TTS。
- **旧实现里没有任何全局快捷键/托盘代码**（全目录检索 `Hotkey|Shortcut|RegisterHotKey` 无结果）。因此 README TODO 第 1 条“基于 C# 重构快捷键唤起窗口”实际指的不是“从旧 UWP 迁移”，而是**把 Flint3 现有的 C++/CLI 快捷键实现改回纯 C#**。

### 1.3 新实现（Flint3 / Flint3.Data / Flint3.Interop）

已完成的能力（以代码计）：

- WinUI 3 迁移完成：主窗口 `MainWindow` + 简洁窗口 `LiteWindow`，均为 `WindowEx`；Mica/Acrylic 背景、标题栏配色、窗口尺寸持久化、系统主题跟随。
- 单实例：`Program.cs` 自建 `Main`（`DISABLE_XAML_GENERATED_MAIN`），`AppInstance` 重定向激活。
- 托盘：`NotifyIcon`（`SetWindowSubclass` + `Shell_NotifyIcon`），支持显示/退出/关闭按钮行为。
- 查词：`FlintFullPage` / `FlintLitePage` 输入即查，目前**只有前缀匹配**（`sw >= $word ORDER BY sw, word COLLATE NOCASE LIMIT 10`），精确词通过结果排序置顶实现；`QueryWord` 在数据层存在但 UI 未调用。
- 生词本：用户词库落在 `Documents\NoMewing\Flint\flint_glossary.db`；内置词库基于 ECDICT 的 `tag`/`oxford` 列（oxford/ielts/toefl/gre/ky/cet6/cet4/gk/zk）。
- 设置：`ApplicationData.Current.LocalSettings`；快捷键 JSON 落 `ApplicationData.Current.LocalFolder\FlintData`。
- TTS、开机自启动（`StartupTask`）、应用内更新提示 UI 等。

未完成/半成品：

- 快捷键仍是 C++/CLI（见 1.6），是 AOT 的硬阻塞。
- 无“汉英查词”，无 mdx，无 wordle。
- 无任何测试工程、无 CI；`Flint3.Data` 能编译，整解决方案需要 C++ 工作负载。
- `Flint3/Data/stardict.db` 被 `.gitignore` 排除（第 365 行），仓库只有 `stardict.7z.001/002` 分卷，需手工解压后设为“内容”。当前工作区中该文件不存在，`Content Include="Data\stardict.db"` 指向缺失文件，**冷启动查词会全部失败且只写 Trace**。

### 1.4 功能差距一览

| 能力 | 旧 Flint | 新 Flint3 | 结论 |
| --- | --- | --- | --- |
| UI 框架 | UWP + WinUI 2 | WinUI 3 + WinAppSDK 1.8 | 已完成迁移 |
| 目标框架 | UWP 6.2.14（.NET Native） | .NET 10 + 自包含 WinAppSDK | 已完成升级 |
| 快捷键唤起 | 无代码 | C++/CLI 低级键盘钩子 | 需纯 C# 重写 |
| 托盘常驻 | 无 | 有 | 新实现已超越旧版 |
| 生词本 | 无 | 用户词库 + 9 个内置词库 | 新实现已超越旧版 |
| TTS | 无 | 有 | 新实现已超越旧版 |
| 精确查词 | 有 `QueryWord` | 数据层有，UI 未用 | 行为差异，需统一 |
| 汉英查词 | 无 | 无 | TODO 2 |
| mdx 导入 | 无 | 无 | TODO 2 |
| wordle | 无 | 无 | TODO 3 |
| 自动化测试 | 无 | 无 | 两套都没有 |

### 1.5 复用与边界

- **可复用**：ECDICT 的 `stardict` 表结构与 SQL 语义、`exchange` 解析规则、`GlossaryDataAccess` 的全部 SQL（用户词库格式已经是线上数据，不能改表结构）、各种 Converter/ThemeResource/控件（`KeyVisual`、`ShortcutControl`）。
- **不可直接复用**：旧 UWP 的 XAML 与页面代码（控件树、标题栏、导航、返回键处理都是 UWP API），只能作为行为参考。
- **应新建的边界**：当前查询、exchange 解析、设置读写、快捷键都散在 `MainViewModel_*.cs` 与 code-behind 中，mdx 与 wordle 继续加进去会失控。建议新增 `Flint3.Core`（纯 .NET、无 WinUI 依赖、可单元测试、`IsAotCompatible`），UI 只保留交互与绑定。

### 1.6 Interop 与 native 依赖（AOT 关键）

`Flint3.Interop` 是 C++/CLI 混合程序集（`CLRSupport=NetCore`，`TargetFramework=net7.0-windows`，平台工具集 `v145`），提供：

1. `HotkeyManager`：注册/反注册热键，基于 `WH_KEYBOARD_LL` 低级钩子（`KeyboardHook.cpp` 的 `SetWindowsHookEx`），命中后发送一个 dummy `VK 0xFF` keyup 以抑制开始菜单。
2. `KeyboardHook`：托管委托 + 原生钩子回调，`FilterKeyboardProc` 用 `GetAsyncKeyState` 组装当前按键状态。
3. `LayoutMap` / `LayoutMapManaged`：用 `MapVirtualKeyExW` + `ToUnicodeEx` 生成按键显示名（`keyboard_layout.cpp`）。

构建依赖：`packages.config` 里的 WIL（`Microsoft.Windows.ImplementationLibrary` 1.0.220914.1），且 vcxproj 用 `..\..\..\packages` 这种相对路径导入 targets（第 265-272 行），换机器/换目录极易失败；需要 VS 的“C++ 桌面开发 + C++/CLI 支持”与 v145 工具集（本机 VS 18 Professional 已装 MSVC 14.51，即 v145）。

**硬结论：.NET NativeAOT 官方明确不支持 C++/CLI 程序集。只要 `Flint3` 还引用这个 vcxproj，就不可能整应用 AOT。这是 TODO 1 的真正阻塞点，而不是 XAML。**

### 1.7 构建与测试现状（实测）

- 本机：.NET SDK `10.0.402`；VS 18 Professional（MSVC 14.51 / v145）。
- `dotnet build Flint3.Data\Flint3.Data.csproj -c Release` **成功**（0 error），但稳定出现 `NU1903`：`SQLitePCLRaw.lib.e_sqlite3` 2.1.11 存在已知高危漏洞。
- **实测整解决方案构建失败**：`MSBuild Flint.sln /restore /t:Build /p:Platform=x64` 在 `Flint3.Interop.vcxproj(271,5)` 报错——`packages.config` 里的 WIL targets 被写死为 `..\..\..\packages\Microsoft.Windows.ImplementationLibrary.1.0.220914.1\build\native\...targets`（从工程目录算起指向 `D:\packages`），NuGet 还原不会在该位置生成文件（本机还原耗时 ~33s 但仍缺文件）。`Flint3.Data` 能正常产出；`Flint3` 的打包环节未走到。
- 除上述外，打包还受 `AppxPackageDir` 硬编码（`C:\Users\Shock Jockey\Desktop\x64\`）与未入库的 `Flint3_TemporaryKey.pfx`（`.gitignore` 忽略 `*.pfx`）影响，在别人机器上不可复现。
- 无测试工程、无 CI 配置、无代码覆盖率基线。

### 1.8 现存缺陷（会被后续阶段放大的）

| # | 位置 | 问题 | 影响 |
| --- | --- | --- | --- |
| D1 | `MainViewModel_Flint.cs` `InitViewModel4Flint` | 数据库初始化 `_ = StarDictDataAccess.InitializeDatabase();` 未 await，仅 fire-and-forget | 冷启动立即输入时连接未打开，查询异常被吞，表现为“偶发查不到词” |
| D2 | `MainViewModel_Flint.cs` `MatchWord` | `async void`，无 `CancellationToken`、无请求序号 | 快速输入时结果乱序覆盖，旧结果覆盖新结果 |
| D3 | `StarDictDataAccess` | 单个静态 `SqliteConnection` 被所有并发查询共享，`MatchWord` 无取消 | 高并发下 “connection is busy”/异常后静默返回空 |
| D4 | `StarDictDataAccess` | 建索引 SQL 全部被注释（第 20-42 行），依赖发布库自带的索引 | 换库/导入 mdx 后前缀查询退化为全表扫描 |
| D5 | `SettingsService` / `StorageFilesService` / `AppVersionUtil` / `SettingsPage` | 直接使用 `ApplicationData.Current`、`Package.Current`、`StartupTask` | 免打包下会抛异常 → 设置/数据目录已加本地回退；`StartupTask` 仍不可用 |
| D6 | `NotifyIconTools.cs` | `cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA))` 为反射式尺寸计算 | 裁剪/AOT 下需要元数据；应改 `Marshal.SizeOf<T>()` 或常量 |
| D7 | `HotkeySettings` | `JsonSerializer` 反射序列化 | AOT 下需 `JsonSerializerContext` 源生成 |
| D8 | `Flint3.Interop` | 原生工程 + `packages.config` + 硬编码相对路径 | 构建门槛高、无法 AOT、不可跨机复现 |
| D9 | `Flint3.csproj` | 硬编码输出目录与证书、`AppxBundlePlatforms` 只有 `x64` 但工程支持 ARM64 | ~~发布不可复现，ARM64 包缺失~~ → 已关闭打包链路并收敛为 `win-x64`，见 §10.1 |

---

## 2. 本项目的 “AOT” 应定义成什么

必须先把目标拆级，否则会做出无法交付的计划：

- **L1（可实现，推荐承诺）**
  - `Flint3.Data`、新建的 `Flint3.Core`、`Flint3.Dict` 全部 `IsAotCompatible=true`，`PublishAot` 可发布为原生库/控制台工具，零 `IL2026/IL3050` 警告。
  - UI 工程保持 `PublishReadyToRun=true` + `CsWinRTAotOptimizerEnabled=true`（当前已开），以“AOT 友好”为验收。
- **L2（需 spike，暂不承诺）**
  - 整应用 NativeAOT。障碍是 WinUI 3/XAML 的类型激活与 WinRT 元数据，以及当前 `PublishTrimmed=false`（官方已知：裁剪会移除 XAML/WinRT 元数据导致启动崩溃）。Windows App SDK 1.8 的官方发布链路是自包含 + R2R + CsWinRT AOT 源生成。
  - 如果产品必须“整应用原生 AOT”，技术上唯一被官方支持的现成路线是 **UWP + 现代 .NET + NativeAOT（VS 2026 已 GA）**，但那是回退 UI 框架，不建议为了 AOT 放弃 WinUI 3。
- **L0（必须达成，无争议）**
  - 删除 C++/CLI 依赖。这一条同时解决构建门槛、AOT 阻塞和可复现性问题。

> 建议在 README 中把 TODO 1 改写为：“删除 C++/CLI，快捷键用 C# 重写；核心库 NativeAOT 兼容；UI 采用 R2R + CsWinRT AOT”。

---

## 3. UI 框架是否/是否应该替换

### 3.1 UI 代码规模与结构（实测，commit `60a22bb`）

统计范围：`Flint3/` 下全部 `*.cs` / `*.xaml`，排除 `bin/`、`obj/`。

| 指标 | 数值 |
| --- | --- |
| XAML | 16 个文件 / 2909 行 |
| C# | 46 个文件 / 5507 行 |
| 窗口 | 2 个：`MainWindow`（254 行 code-behind）、`LiteWindow`（209 行），均继承 WinUIEx `WindowEx` |
| 页面 | 7 个：`FlintFullPage`、`FlintLitePage`、`GlossariesPage`、`GlossaryWordsPage`、`GlossaryWordInfoPage`、`GlossaryPropertyPage`、`SettingsPage`；经 `Frame.Navigate` + `NavigationCacheMode="Required"` |
| 自定义控件 | 4 个：`KeyVisual`（自定义 `Control`，带 139 行模板 XAML）、`ShortcutControl`（371 行，内含 `ContentDialog`）、`ShortcutDialogContentControl`、`AddToGlossaryControl` |
| 绑定方式 | `x:Bind` 187 处、普通 `{Binding}` 22 处、`TemplateBinding` 65 处（约 89% 为编译期绑定） |
| MVVM | `CommunityToolkit.Mvvm` 8.4（`ObservableObject` / `[INotifyPropertyChanged]`）；`MainViewModel` 拆 4 个 partial 共 928 行，但 `Views` code-behind 合计 **1174 行**，页面级逻辑仍在 code-behind |
| 样式资产 | `CustomControlStyle.xaml` 501 行 + `ThemeDictionary.xaml` 45 行 + 各页局部 `Page.Resources` |
| 分层现状 | `Views` 14 文件 / 3124 行、`ViewModels` 4 / 928、`Helpers` 8 / 1138、`Controls` 8 / 1043、`Models` 5 / 308、`Converters` 14 / 471、`ThemeResource` 2 / 546 |

结论：**代码体量不大（约 8.4k 行），但“视觉资产”占比高**——两个窗口的 Mica/亚克力与标题栏、`SettingsCard/SettingsExpander/Segmented`、`TeachingTip/AnimatedIcon`、501 行控件样式、65 处 `TemplateBinding`。换框架的真实成本主要不是重写行数，而是这些 WinUI 专有外观与交互的等价重建。

依赖面（决定迁移成本）：

| 依赖 | 用途 | 是否 WinUI 专属 |
| --- | --- | --- |
| `Microsoft.WindowsAppSDK` 1.8 | WinUI 3、`AppWindow`、`AppLifecycle` | 是 |
| `WinUIEx` 2.9 | `WindowEx`、`IsAlwaysOnTop`、窗口辅助 | 是 |
| `CommunityToolkit.WinUI.Controls.SettingsControls` | 设置页全部卡片 | 是 |
| `CommunityToolkit.WinUI.Controls.Segmented` | 生词本切换 | 是 |
| `CommunityToolkit.WinUI.Animations` / `CommunityToolkit.WinUI.UI` 7.1.2 | 动画与旧控件 | 是 |
| `Microsoft.Windows.CsWin32` | P/Invoke 源生成 | 否 |
| `CommunityToolkit.Mvvm` | MVVM 源生成 | 否 |

### 3.2 当前实际用到的平台能力与可移植性

| 能力 | 代码位置 | 换框架后能否实现 | 成本 |
| --- | --- | --- | --- |
| Mica / 亚克力背景 | `MainWindow.xaml.cs:231`、`LiteWindow.xaml.cs:197`（`SystemBackdrop` + `MicaBackdrop`/`DesktopAcrylicBackdrop`） | WPF 无内置，需 DWM 私有接口拼装；Avalonia 11 有 `TransparencyLevelHint` 近似；WinForms 不可 | 中～高 |
| 自定义标题栏 | `MainWindow.xaml.cs:30`、`:198`（`ExtendsContentIntoTitleBar` + `AppWindow.TitleBar`） | WPF `WindowChrome`；Avalonia `ExtendClientArea`；均需重写拖动区/按钮 | 中 |
| 双窗口 + 置顶 | `MainWindow.xaml.cs:20`、`LiteWindow.xaml.cs:20`、`:31` | 所有框架都有 `Topmost`，但 `WindowEx` 的窗口管理需自建 | 低 |
| 托盘 + 消息子类化 | `NotifyIcon.cs`、`NotifyIconTools.cs`（`Shell_NotifyIcon`、`SetWindowSubclass`） | 纯 Win32 P/Invoke，全部框架可移植 | 低 |
| 单实例 + 激活重定向 | `Program.cs:66` `AppInstance`、`App.xaml.cs:68` | WPF/Avalonia 需 `Mutex` + 命名管道 + 命令行转发 | 中 |
| 系统主题 / 强调色 | `ElementTheme`、`ActualThemeChanged`、`ThemeDictionary.xaml` | 都有主题机制，但资源键名与控件模板完全不同 | 高（样式重写） |
| `SettingsCard` / `SettingsExpander` / `Segmented` | `SettingsPage.xaml`（494 行）、`GlossariesPage.xaml:47` | WPF/Avalonia 无等价控件，需自建或换第三方库 | 高 |
| `TeachingTip` / `AnimatedIcon` / `ProgressRing` / ListView 过渡 | `FlintFullPage.xaml:221-235`、`GlossaryWordsPage.xaml:154`、各页 ListView | 需替代实现或动画库 | 中～高 |
| `Frame` 导航 + 页面缓存 | `MainWindow.xaml:20` | WPF `Frame` 类似；Avalonia 需自建导航 | 中 |
| TTS | `TextToSpeechHelper.cs`（`Windows.Media.SpeechSynthesis`） | OS API，任何框架经 Windows SDK projection 都可用 | 低 |
| 开机自启 | `SettingsPage.xaml.cs:96`（`StartupTask`） | 依赖 MSIX 包身份，与 UI 框架无关 | 低 |
| 全局快捷键 | `Flint3.Interop` + `KeyboardHelper` | 纯 Win32，与 UI 框架无关；P1 后为纯 C# | 低 |
| 图标字体 | 全项目大量 `FontIcon` + Segoe Fluent Icons | 字体可复用，但字形与尺寸需逐项核对 | 中 |

关键判断：**决定成本的只有 4 项——背景材质、标题栏、设置页控件、样式资源**；其余（托盘、快捷键、TTS、自启）都是 Win32/OS 能力，换不换框架成本几乎一样。

### 3.3 候选方案对比

> “启动/内存”“还原度”为工程量预估，仓库当前没有性能基线；P0 应先补测，再用于决策。AOT 一列与第 2 节的 L1/L2 目标联动。

| 方案 | 迁移工作量 | AOT / 裁剪 | 启动速度与内存 | 设计还原度 | mdx / wordle 可行性 | 维护成本与生态 | 结论 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **保留 WinUI 3** | 0 | L1 可达成；L2 受 XAML 类型激活与裁剪限制，需 spike | 现状即基线；WinUI 3 自带 WinAppSDK 运行时，内存偏高，需实测 | 100% | 完全可行（与 UI 无关，走 `WordQueryService`） | WinAppSDK 迭代快、偶有回归；但本项目已深度适配 | **推荐** |
| WPF | 高：2 个窗口 + 7 页 + 4 控件 + 全部样式重写，`x:Bind` 改 `{Binding}`/编译绑定 | **NativeAOT 不可行**（依赖 COM 封送），只能 R2R；裁剪受限 | 通常优于 WinUI 3（需实测） | 中：Mica/亚克力、Fluent 控件、动效需自绘，约 60–70% | 可行 | 生态最成熟、长期稳定；但现代 Fluent 控件缺失 | 不推荐 |
| Avalonia 11 | 高：全量 XAML 重写 + 主题/控件自建；`DispatcherQueue`→`Dispatcher`；`x:Bind`→`CompiledBinding` | **官方支持 NativeAOT**（需全量编译绑定、裁剪配置） | 通常更轻（需实测） | 中高：`TransparencyLevelHint` 可近似 Mica/亚克力，细节与系统控件观感有差异 | 可行 | 增长快，但第三方控件质量参差、Windows 集成需自己补 | 仅在跨平台或 UI 必须 NativeAOT 时选 |
| WinForms | 中高：控件树重建，无样式/主题体系 | 官方未将 WinForms 列为 NativeAOT 支持场景，仅部分裁剪 | 最优（需实测） | 低（<40%），需重新设计视觉 | 可行 | 稳定但老化，现代交互需大量自绘 | 不推荐 |
| Uno Platform | 低：WinUI XAML 基本兼容 | Windows 后端仍是 WinUI 3，**对 AOT 无增益** | 与 WinUI 3 相当 | 高（Windows 上等价于现状） | 可行 | 生态一般，跨平台才有价值 | 不推荐（本项目 Windows-only） |
| .NET MAUI | 中高：XAML/控件模型不同，Windows 后端仍是 WinUI 3 | 无改善 | 更重 | 中 | 可行 | 一般 | 不推荐 |
| Win2D | —（不是 UI 框架，是 2D 绘图库） | 不适用 | 不适用 | 仅用于自绘 | 只对 wordle 动画/绘图有意义 | 微软维护 | 不作为替换候选 |

补充：**WinUI 3 不是“AOT 不可用”，而是“整应用 NativeAOT 未获官方支持”**；本项目真正的 AOT 阻塞是 C++/CLI（P1 删除），与 UI 框架选择无关。换成 Avalonia 能把 UI 也带进 NativeAOT，但代价是全量重写，且不会改善查词/DB 这类真正的性能热点。

### 3.4 明确推荐：保留 WinUI 3

理由按权重排序：

1. **换框架解决不了主要矛盾**：AOT 的硬阻塞是 `Flint3.Interop`（C++/CLI），P1 删除后 L1 即可达成；UI 层 L2 的取舍不应以整体重写为代价。
2. **成本集中在不可低成本迁移的视觉资产**：`SettingsCard/SettingsExpander`（设置页几乎全部）、`Segmented`、Mica/亚克力、自定义标题栏、`TeachingTip/AnimatedIcon`、501 行控件样式。代码只有 8.4k 行，但这些是“重写一遍再调一遍”的工作。
3. **产品形态 Windows-only + 免打包自包含目录分发（已定，见 §10.1）**：Avalonia 的跨平台优势无法变现；且免打包形态下托盘、单实例、系统主题等 Windows 集成仍要逐个适配，换框架等于把 WinUI 3 已跑通的集成重做一遍。
4. **现有集成已踩完坑**：单实例激活重定向、托盘子类化、双窗口、TTS 都已跑通，重写等于把已知问题再踩一遍（开机自启依赖包身份，免打包形态下不可用，需另做）。
5. **mdx/wordle 与 UI 框架无关**：两者都应落在 `Flint3.Core`/`Flint3.Dict`，UI 只做展示；换框架不会让它们更容易。

### 3.5 什么条件下应该换，什么条件下不值得换

**应该换（满足任一，且需先用数据证明）**

- 产品必须支持 macOS/Linux → 选 **Avalonia**；前提是先完成 P2 分层，保证 `Core/Data/Dict` 可复用，UI 层单独重写。
- “整应用 NativeAOT”成为硬指标，且 P3 的 L2 spike 证明 WinUI 3 不可行 → 此时先确认能否接受“UI 层 R2R + 核心库 AOT”（已定目标）；其余分支（UWP + 现代 .NET + NativeAOT）已失效：UWP 必须 MSIX 包身份，与已定的免打包分发形态冲突。
- 低端设备有硬性启动/内存预算，且 P0 基线实测 WinUI 3 超出预算 30% 以上 → 再评估 Avalonia/WPF；没有基线数据前不启动迁移。
- 产品战略转为“极简悬浮查词窗”，可放弃 Fluent 设计与动画 → WPF/WinForms 单窗方案才可能划算。

**不值得换**

- 目标只是“体积更小”“启动更快”，但没有量化预算与基线。
- 期望换框架能降低维护成本：实际是 8.4k 行 UI 重写 + 重新适配托盘/单实例/主题/打包，短期成本远高于收益。
- 为了 AOT 而换：AOT 主阻塞（C++/CLI）在 P1 就能消除；换 UI 框架只影响 L2，且只有 Avalonia 能改善。
- 为了 mdx/wordle 而换：这两个功能不依赖 UI 框架，换框架是无效投入。

### 3.6 与现有阶段计划的关系

**不调整 P0–P6 的顺序**，理由：

1. P3 的 L2 spike 仍需回答“WinUI 3 UI 层在 AOT/裁剪下的具体边界”，但其用途从“是否换框架”改为“确定 R2R/AOT 的验收标准”。
2. P1（删 C++/CLI）对任何框架都是净收益，必须先做。
3. P2 分层是 mdx/wordle 等后续功能与长期维护的前置条件。

**决策门已简化**：“保留 WinUI 3”已是定论（详见 §10.6），因此**取消**原“P3 结束做 Go/No-Go 决定是否转 Avalonia”的门及其 P3.5 迁移阶段；P3 仅输出 L2（UI 层 AOT/裁剪/R2R）的技术结论，不再以“切换 UI 框架”为选项。P0–P6 的顺序不变。

### 3.7 P3 前必须补的度量

- 冷启动到“可输入”的时间、空闲内存、连续输入时 P95 查词延迟；在目标最低配置机器上各测 3 次取中位数。
- 记录 WinUI 3 在 `PublishAot` 下的具体失败点（`IL2026/IL3050`、XAML 元数据缺失、启动崩溃），作为确定“核心库 AOT + UI R2R”验收标准的依据。
- 记录 3.2 表的 WinUI 3 专有能力清单在免打包形态下的可用性（如 `StartupTask` 不可用），避免后续按打包形态估算工作量。

---

## 4. 分阶段计划

### P0 基线：可复现构建与可回归

**目标**：任何人克隆仓库后，能按文档一步构建、运行、验证。

交付物：
1. `tools/prepare-data.ps1`：从 `Flint3/Data/stardict.csf.7z.001/002` 解出 `stardict.csf` 并校验固定 SHA256；本地存在 `stardict.db` 时再执行 `Flint.Cli verify` 逐条对拍与 `RealDatabaseTests`（仓库不再分发 db 分卷，CSF 格式变更后的重建需先从 ECDICT 上游获取源库）。
2. `Flint3.csproj` / `Flint3.Interop.vcxproj` 清理（**已完成**）：关闭打包链路（`WindowsPackageType=None`、移除 `AppxPackageDir`/临时证书/MSIX 能力声明），`SelfContained` + `WindowsAppSDKSelfContained`，`RuntimeIdentifiers=win-x64`，`stardict.db` 复制到输出目录；删除未使用的 WIL `packages.config` 引用，消除 `..\..\..\packages` 硬路径导致的构建失败。
3. 新建 `Flint3.Data.Tests`（xUnit）：先用**当前行为**写黄金用例（`MatchWord`、`GetAllExtraGlossaryWords`、exchange 解析），作为后续重构的护栏。
4. `docs/build.md`：前置条件（SDK、VS C++/CLI、7z）、构建/发布命令。
5. 处理 D1（**已按不同方案落地**）：未引入可等待的 `InitializeAsync`；`StarDictDataAccess` 改为首次查询时同步惰性创建后端（CSF 走 mmap，打开成本低），`InitializeDatabase` 仅保留给测试指定库路径，`MainViewModel` 不再调用它。

风险：
- 分卷 7z 解压路径/文件句柄问题；`stardict.db` 约数百 MB，测试中应使用裁剪后的 fixture 库而不是全库。
- 免打包形态没有包标识：`ApplicationData`/`StartupTask`/`FileOpenPicker` 等 API 不可用，必须走本地路径回退（设置与数据目录回退、HKCU 开机自启均已实现）。

验收：
- 干净目录 + 脚本 + `msbuild Flint.sln /p:Configuration=Release /p:Platform=x64` 成功；
- 应用启动后输入 `hello` 命中；
- `dotnet test` 全绿，且测试不依赖真实 `stardict.db`；
- 冷启动脚本连续 20 次“启动即输入”不再出现空结果。

**验收状态（2026-11-11）**：构建、`hello` 命中与测试全绿已满足；20 次冷启动未做实测记录。

---

### P1 纯 C# 快捷键（TODO 1 核心，去 C++/CLI）

**目标**：删除 `Flint3.Interop`，快捷键、按键名显示全部用 C# 实现，行为对齐。

交付物：
1. `Flint3.Core/Input/HotkeyService.cs`：
   - `SetWindowsHookEx(WH_KEYBOARD_LL, ...)` + `CallNextHookEx`，钩子委托用静态字段持有，避免 GC 回收；
   - `GetAsyncKeyState` 组合修饰键（与现 `FilterKeyboardProc` 等价）；
   - 命中后 `SendInput` 发送 dummy keyup 抑制开始菜单（与现实现一致）；
   - `Register/Unregister` 用 `handle` 字典，语义与现 `HotkeyManager` 一致（覆盖同名热键）。
2. `Flint3.Core/Input/KeyNameResolver.cs`：`MapVirtualKeyEx` + `ToUnicodeEx` 生成显示名，特殊键（方向键、F1-F24、Win/Ctrl/Alt/Shift、Tab、CapsLock）走显式映射表。
3. 全部 P/Invoke 改为 CsWin32 生成（工程已引用 `Microsoft.Windows.CsWin32`，扩展 `NativeMethods.txt`），删除 `interop` 命名空间引用与 `LayoutMapManaged`。
4. 删除 `Flint3.Interop/*` 与 `Flint3.csproj` 里的 `ProjectReference`；从 `Flint.sln` 移除。
5. 增加单元测试：热键 handle 编码、`HotkeySettings.IsValid/IsAccessibleShortcut`、按键名解析（固定 HKL 的用例）。

风险：
- 低级键盘钩子回调线程与 UI 线程的封送：现实现是 C++/CLI 直接在钩子线程调用托管委托；纯 C# 版本必须**不阻塞**且通过 `DispatcherQueue.TryEnqueue` 回到 UI 线程（现 `App.ShowMainWindow` 是静态方法，内部已 `TryEnqueue`）。
- 全局钩子可能被安全软件拦截；`SetWindowsHookEx` 失败要有可观测日志与降级（提示用户）。
- 行为回归风险最高的是 “Alt+Space 不再弹出开始菜单” 与 “窗口未激活时也能唤起”。

验收：
- 解决方案不再包含 `.vcxproj`，`dotnet build` 无需 C++ 工作负载；
- 默认 Alt+Space、自定义快捷键（含 Win 组合）、简洁/完整两种窗口模式均正确唤起；
- 连续触发 200 次无失效、无句柄泄漏（退出后钩子已卸载）；
- 快捷键设置 JSON 仍可读写（格式不变）。

**验收状态（2026-11-11）**：去 C++/CLI、默认/自定义快捷键、JSON 读写已满足；200 次唤起与句柄泄漏未做自动化或实测记录。

---

### P2 分层与查询抽象（为 mdx/wordle 铺路）

**目标**：把 `MainViewModel` 的业务逻辑下沉到可测试的 `Flint3.Core`，并引入多词典源抽象。

交付物：
1. `Flint3.Core` 新工程（`net10.0`，`IsAotCompatible`，无 WinUI 引用）：
   - `IWordSource`：`QueryAsync(word)`、`MatchAsync(prefix, limit)`、`PatternAsync(pattern, limit)`；
   - `WordQueryService`：聚合多个源，去重、排序（精确 > 前缀 > 其他）、来源标记；
   - `ExchangeParser`：把 `p:/d:/i:/3:/r:/t:/s:/0:` 解析逻辑从 VM 移出；
   - `ISettingsStore`：抽象 `ApplicationData`，为 unpackaged 做准备。
2. `EcdictSource`：包装现有 `StarDictDataAccess`，保持 SQL 与结果不变。
3. 搜索链路修复 D2/D3：`TextChanged` → 300ms 防抖 → 可取消查询 → 序号校验后才写入 `ObservableCollection`。
4. `SettingsService` 通过 `ISettingsStore` 注入；`JsonSerializerContext` 覆盖 `HotkeySettings` 与新的源配置。
5. 测试：50 条查询语料的黄金输出，与 P0 基线逐字比对。

风险：
- 重构期 UI 行为漂移（排序、精确词置顶、空输入清空）。
- 引入 DI 容器会过度设计 → 建议只做**构造函数注入 + 简单工厂**，不引入容器。

验收：
- 黄金语料输出与基线 100% 一致；
- 快速输入 20 字符不再出现乱序结果；
- `Flint3.Core` 单测覆盖查询编排、exchange 解析、设置读写；
- `MainViewModel_*.cs` 不再直接出现 SQL 字符串。

---

### P3 AOT 落地

**目标**：兑现第 2 节的 L1；对 L2 给出有数据支撑的结论。

交付物：
1. `Flint3.Data`、`Flint3.Core`、`Flint3.Dict`：`IsAotCompatible=true`、`PublishAot` 控制台 harness（`tools/Flint.Cli`），可对指定 db 执行查询。
2. 逐项消除裁剪/AOT 警告：
   - `JsonSerializer` → 源生成上下文（D7）；
   - `Marshal.SizeOf(typeof(...))` → `Marshal.SizeOf<T>()`/常量（D6）；
   - 手写 `DllImport` → CsWin32；
   - `Package.Current`/`ApplicationData` 走 `ISettingsStore` 的打包/非打包双实现（D5）。
3. 升级 `SQLitePCLRaw`/`Microsoft.Data.Sqlite` 修复 `NU1903`，并验证 `PublishAot` 输出中 `e_sqlite3.dll` 被正确复制。
4. `docs/aot-report.md`：L1 实测结果（启动时间、内存、发布体积）+ L2 spike 结论。

风险：
- `PublishAot` 与 `Microsoft.Data.Sqlite` 的原生资产复制；`SQLitePCLRaw` 的 provider 初始化在 AOT 下需要显式 `Batteries_V2.Init()`。
- L2 若验证失败，需要向干系人明确“整应用 NativeAOT 不在本期范围”，避免无限投入。
- 升级 SQLite 包可能改变 SQL 行为（排序/大小写）→ 用 P0 黄金用例回归。

验收：
- `dotnet publish tools/Flint.Cli -p:PublishAot=true -r win-x64` 成功且可查词；
- `Flint3.Data`/`Flint3.Core`/`Flint3.Dict` 构建 0 个 `IL2xxx/IL3xxx` 警告；
- UI Release 发布仍可启动，`PublishTrimmed=false`（保持官方建议），记录 R2R 启动耗时对比；
- L2 有明确结论与依据（成功或不可行的具体报错点）。

---

### P4 mdx 导入与汉英查词（TODO 2）

**目标**：支持导入 `.mdx`（可选 `.mdd`），并支持中文反查英文。

交付物：
1. 新工程 `Flint3.Dict`（纯 .NET，AOT 兼容）：
   - `MdxReader`：解析 header（UTF-16LE 字符串、`GeneratedByEngineVersion`、`Encrypted`、`Encoding`）、KeyBlockInfo/RecordBlockInfo、key block 解压（zlib）、词条遍历；
   - 记录块解压：优先 `System.IO.Compression.ZLibStream`（AOT 安全、无原生依赖）；LZO 块需引入纯托管 LZO 或标记“该词典不支持”；
   - `Encrypted=2` 的块头解密（RIPEMD-128）需明确支持范围，不支持时给出可读错误；
   - HTML 词条提取：去 script/style、保留文本与换行，输出到既有 `Definition/Translation` 结构。
2. 导入流水线：
   - 首次导入生成 sidecar 索引库 `mdx_<sha1前8位>.db`（`word`, `word_lower`, `block_index`, `record_offset`, `record_size`），按 `word_lower` 建索引；
   - `dictionarySources.json`（源生成序列化）记录来源名、路径、优先级、启用状态；
   - 词库文件复制到 `LocalFolder\FlintData\Dicts`，卸载/升级不丢。
3. 汉英查词（MVP 已落地）：
   - 原方案 A（`stardict_zh` + FTS5 `trigram`）**经实测对 2 字中文查询无效**（`苹果`/`银行`/`果` 均返回 0，仅 ≥3 字可用），见 [chinese-lookup-decision.md](chinese-lookup-decision.md)；
   - 已实现 ECDICT 旁挂有序索引 `stardict.zhx`（`Flint3.Data/Chinese`）：只做精确 + 前缀，排序为 精确 > 前缀 → Collins 降序 → frq 升序（缺失排后）→ 词长 → 字母序；不改 CSF 格式与版本位；索引缺失/损坏/版本不符/记录数不一致时中文降级、英文不受影响；
   - 实测（3,402,564 条）：词条 2,648,701、倒排 4,642,415、构建 25.7 s、75.92 MB（7z 31.83 MB）；索引侧精确 p95 0.017 ms、前缀 p95 0.607 ms，端到端（含 CSF 回读 10 条）p50 3.65 / p95 9.10 ms；
   - 留二期：bigram 子串（输入"果"查"苹果"）、拼音、简繁转换、分词；
   - 方案 B 兜底（`translation LIKE`）实测单次 1.5~3.1 s，只能用于“回车后查”，不能做逐键联想。
4. UI：**实际并入 `Views/SettingsPage` 的“mdx 词库”卡片**（列表、导入、启用/禁用、删除、进度与取消），未单独建 `DictionarySourcesPage`；导入使用 `FileOpenPicker` + `IInitializeWithWindow`（非打包）。
5. 测试：3 个样本 mdx（小型 CC-CEDICT、一个带 HTML 的词库、一个加密词典）+ 100 词查询黄金用例；索引构建耗时与内存预算。

风险：
- mdx 生态实现碎片化（现有 C# 库多为社区实现：`MDict.Csharp`、`mdict.csharp`），质量/许可证/AOT 兼容性参差 → **建议自研 reader，只借鉴格式文档**，避免把核心能力绑定到无人维护的包。
- 大型 mdx（数百 MB～数 GB）导入耗时与内存峰值；必须在后台线程 + 进度 + 可取消。
- `.mdd` 图片资源会显著增大体积，首期可不支持，仅解析文本。
- 汉英检索的正确性与性能：中文分词/子串语义需要明确产品定义（`LIKE` 子串 vs 分词）。
- 许可证：用户导入的词典版权由用户承担，需在 UI 明确提示。

验收：
- 导入 3 个样本词典成功，重启后仍可用；
- 100 词英文查询结果与 Golden 文件一致；
- 20 个中文词（如“苹果”“计算机”“银行”）能返回合理英文词条；
- 导入 500MB 词典时 UI 不卡死，可取消，内存峰值有记录；
- 删除词典后索引文件被清理，不影响 ECDICT。

**验收状态（2026-11-11）**：导入、启停、删除、重启后可用与索引清理已满足；**汉英查词未实现**（无中文索引，且 §10.4 语义待决策）；**100 词黄金用例未完成**（现有样本仅 3 条词目，仓库无 mdx 生成器）；500MB 导入已具备进度、取消与后台索引构建，但未做 500MB 实测与内存峰值记录。

---

### P5 wordle（TODO 3）

**目标**：先做“按格式匹配单词”，再做内置 wordle 游戏。

交付物：
1. 匹配模式（低成本、先交付）：
   - 在 `WordQueryService` 增加 `PatternAsync`，语法：`?`=任意单字母、`*`=任意多字母、`a_e` 下划线同 `?`、可选 `^`/`$` 锚定；
   - 转译为 SQLite `GLOB`（大小写敏感）或 `LIKE`（大小写不敏感，配合 `word_lower`），限制返回条数；
   - UI：搜索框前缀语法（如 `?a?e`）或独立“模式匹配”入口，结果列表复用现有模板。
2. 内置 wordle：
   - `Flint3.Core/Wordle/WordleGame.cs`：纯状态机（答案选择、猜测校验、绿/黄/灰计算、胜负、每日种子），完全可单测；
   - 候选词表：预生成 `wordle_words(len, word)` 表（筛 ECDICT 5 字母常用词，按 `bnc/frq` 排序），**不在运行时扫描全库**；
   - UI：`Views/WordlePage` + `WordleViewModel`，复用 `KeyVisual` 控件做键盘；
   - 状态持久化到 `LocalFolder\FlintData\wordle_state.json`（源生成序列化）。
3. 测试：匹配器边界（多 `*`、空模式、超长、非 ASCII）；wordle 判定用例（重复字母是关键场景）。

风险：
- 模式匹配若走 `LIKE '%x%'` 会全表扫描 → 必须限制模式形态或先按长度过滤（`length(word)=5`），并在 P0 阶段确认索引；
- 候选词表质量决定游戏体验，需人工抽样校对；
- 每日答案与用户本地时区/日期边界。

验收：
- `?a?e` 类查询 200ms 内返回且结果正确；
- wordle 判定用例全绿（含重复字母：`allee` vs `apple`）；
- 游戏状态跨重启恢复；切换日期后答案变化且可复现。

---

### P6 清理与收尾

- 删除 `Flint/`、`Flint.Core/`、`Flint.Data/`（或移到 `legacy/` 并在 README 说明），消除双份 `StarDictDataAccess`/`StarDictWordItem`。
- README 更新：新架构图、AOT 结论、mdx 支持范围、数据准备步骤。
- 若条件允许，加最小 CI：`windows-latest` 上 `dotnet build` + `dotnet test`（不含打包）。
- 统一命名：`Flint3` → 是否改回 `Flint` 需单独立项（涉及命名空间、程序集名、MSIX 可执行文件名；**包 Identity 不能改**）。

验收：仓库中不存在未引用的工程；`dotnet build Flint.sln` 与 `dotnet test` 可复现；README 与实际一致。

---

## 5. AOT 兼容性专项清单（提前处理）

| # | 处理点 | 现状 | 动作 |
| --- | --- | --- | --- |
| A1 | C++/CLI 程序集 | `Flint3.Interop`（`CLRSupport=NetCore`） | P1 删除，换纯 C# + CsWin32 |
| A2 | JSON 反射序列化 | `JsonSerializer.Serialize/Deserialize<HotkeySettings>` | 加 `JsonSerializerContext`，全项目 `JsonSerializerIsReflectionEnabledByDefault=false` |
| A3 | 正则 | 已用 `[GeneratedRegex]` | 保持；禁止运行时 `new Regex` |
| A4 | `Marshal.SizeOf(Type)` | `NotifyIconTools.NOTIFYICONDATA.cbSize` | 改 `Marshal.SizeOf<T>()` 或写死 956/按版本分支 |
| A5 | 手写 `DllImport` | `NotifyIcon.cs`、`NotifyIconTools.cs`、`KeyboardHelper.cs` | 逐步迁移到 CsWin32（`NativeMethods.txt` 已存在） |
| A6 | WinRT / 包身份 API | `Package.Current`、`ApplicationData.Current`、`StartupTask`、`UserDataPaths` | 抽象 `ISettingsStore`/`IAppEnvironment`；提供打包与非打包实现 |
| A7 | XAML 类型激活 | `Frame.Navigate(typeof(Page))`、DataTemplate | L2 spike 必测项；优先 `x:Bind`、避免反射绑定 |
| A8 | 裁剪 | `PublishTrimmed=false` | 保持 false；不要为“缩体积”打开裁剪（官方已知会导致 WinUI 启动崩溃） |
| A9 | SQLite 原生资产 | `Microsoft.Data.Sqlite` 10.0.1 + `SQLitePCLRaw.lib.e_sqlite3` 2.1.11（NU1903） | 升级到修复版本；`PublishAot` 后验证 `e_sqlite3.dll` 存在；显式 `Batteries_V2.Init()` |
| A10 | MVVM 源生成 | `CommunityToolkit.Mvvm` 8.4 | 保持；避免运行时反射式命令绑定 |
| A11 | `Activator`/`Type.GetType` | 未使用 | 新增代码禁用 |
| A12 | 委托生命周期 | 钩子委托在 C++/CLI 内持有 | C# 版本用静态字段持有，防 GC |

---

## 6. mdx 导入与汉英查词的插入位置

```
Flint3（UI）
  ├─ Views/SettingsPage.xaml(.cs)     ← 词库管理（导入/启停/删除/取消）实际位置
  └─ ViewModels/MainViewModel_Flint   ← 只调用 WordQueryService，不写 SQL

Flint3.Core（纯逻辑，AOT）
  ├─ IWordSource / WordQueryService   ← 多源聚合、去重、排序、模式匹配
  ├─ ExchangeParser
  └─ Wordle/*

Flint3.Dict（纯逻辑，AOT）
  ├─ Mdx/MdxReader                    ← 格式解析、zlib、可选 LZO/RIPEMD128
  ├─ Mdx/MdxIndexBuilder              ← 生成 sidecar SQLite 索引
  └─ Sources/MdxWordSource            ← 按索引查词，按需解压记录

Flint3.Data（SQLite，AOT）
  ├─ StarDictDataAccess               ← ECDICT（保持 SQL 与表结构）
  └─ ChineseIndexDataAccess           ← 汉英：stardict_zh / FTS5 trigram
```

插入原则：

1. **不要在 `Flint3.Data` 里塞 mdx 解析**：Data 只负责 SQLite；mdx 是文件格式解析 + 自有索引，独立工程才能单测与 AOT 验证。
2. **不要在 `MainViewModel` 里判断“当前是英文还是中文”**：输入语言判定（`\p{IsCJKUnifiedIdeographs}`）放在 `WordQueryService`，UI 只展示 `ResultGroup`。
3. **导入即建索引，查询只读索引**：mdx 词条 HTML 解析是重活，不能在每次按键时做；sidecar 索引库与源文件同生命周期。
4. **复用现有结果模型**：`StarDictWordItem` 增加 `Source`（`Ecdict/Mdx:<name>`）字段即可，避免再造一套 UI 模板；注意该类型是 `ObservableObject`，新增字段不改变用户词库表结构。

## 7. wordle 的插入位置

- 逻辑：`Flint3.Core/Wordle/`（无 UI、无 IO，构造参数为答案与词表接口）。
- 模式匹配：并入 `WordQueryService.PatternAsync`，与 wordle 共用“按长度/字母集筛选”的底层能力，避免两套实现。
- UI：`Flint3/Views/WordlePage.xaml(.cs)` + `Flint3/ViewModels/WordleViewModel.cs`；入口放 `FlintFullPage` 标题栏或设置页（与生词本入口同级），不要塞进搜索框。
- 词表：由 `tools/wordle/gen_wordle_words.py` 从 ECDICT 生成 `Flint3.Core/Wordle/wordle_words.json`，作为嵌入资源随程序发布；运行时只读，不扫描词库。
- 键盘：复用 `Controls/KeyVisual`，状态色复用现有主题资源，避免新配色体系。

## 8. 建议动手顺序与理由

1. **P0 基线** — 现在连“改完是否变坏”都无法判断：数据文件缺失、无测试、发布路径硬编码。先建立可复现构建与黄金用例，后面所有阶段才有验收依据。
2. **P1 去 C++/CLI** — 它同时是 AOT 硬阻塞、构建门槛、跨机不可复现的根源；越早删，后续阶段越少被原生工程拖累。且 README TODO 1 的核心就是这件事。
3. **P2 分层与查询抽象** — mdx、汉英、wordle 三者都要“按不同规则查词”。在加功能前抽象 `IWordSource`，否则会继续把逻辑堆进 `MainViewModel_*.cs`，后面再拆成本翻倍。
4. **P3 AOT** — 放在功能之前，是因为 AOT 会反向约束实现（序列化、P/Invoke、反射）；等 mdx/wordle 写完再改，返工面积最大。同时 L2 的 spike 结论会影响 mdx 的导入交互（打包 vs 免安装）。
5. **P4 mdx + 汉英** — 用户价值最高、工作量最大，依赖 P2 的抽象与 P3 的 AOT 结论。先做“只读文本词条 + zlib”，把加密/LZO/`.mdd` 放到后续增量。
6. **P5 wordle** — 风险最低、可独立交付；模式匹配可以先于游戏上线，验证查询层是否真的可复用。
7. **P6 清理** — 最后做，避免早期删旧代码后失去行为参考。

不推荐的顺序：先做 mdx（会直接撞上 `MainViewModel` 耦合与 AOT 约束）、或先做整应用 NativeAOT（C++/CLI 未删，必然失败）。

---

## 9. 里程碑验收总表

| 阶段 | 一句话验收 | 量化门槛 |
| --- | --- | --- |
| P0 | 干净机器可构建可运行 | 构建 0 error；20 次冷启动即输入无空结果；黄金用例全绿 |
| P1 | 无 C++/CLI，快捷键行为不变 | 200 次唤起无失效；无需 C++ 工作负载即可构建；钩子可卸载 |
| P2 | 查询逻辑可单测、无乱序 | 50 条语料与基线一致；快速输入无旧结果覆盖；VM 无 SQL |
| P3 | 核心库真 AOT，UI AOT 友好 | `PublishAot` 成功；0 个 IL2xxx/IL3xxx；L2 有结论 |
| P4 | 可导入 mdx、可汉英互查 | 3 个样本词典；100 词黄金用例；500MB 导入可取消不卡 UI（**汉英互查、100 词用例未完成，见 P4 验收状态**） |
| P5 | 模式匹配 + 可玩 wordle | 模式查询 <200ms；判定用例全绿；状态可恢复 |
| P6 | 仓库干净、文档一致 | 无未引用工程；README/命令可复现 |

---

## 10. 待决策问题

1. **分发形态（已定）**：只支持 Windows，**不要 MSIX**，直接产出可双击运行的**免打包 unpackaged 自包含目录**（`WindowsPackageType=None` + `SelfContained` + `WindowsAppSDKSelfContained`，`RuntimeIdentifiers=win-x64`，无证书签名）。带来的后果与处理：`ApplicationData` 不可用 → 设置落 `%LOCALAPPDATA%\Flint3\settings.json`、数据目录落 `%LOCALAPPDATA%\Flint3\FlintData`；`StartupTask` 开机自启不可用，后续如需自启改用启动目录/注册表。
2. **AOT 预期**：是否接受“核心库 NativeAOT + UI R2R”作为 TODO 1 的完成标准？若不接受，需要评估回退 UWP 现代 .NET 的代价（该分支已因免打包形态失效）。
3. **mdx 支持范围**：首期是否只支持未加密、zlib、纯文本词条？`.mdd` 图片、LZO、加密词典是否进入首期？
4. **汉英检索语义（已定，MVP 已落地）**：采用旁挂有序索引，只做精确 + 前缀，不做子串/拼音/简繁/分词。实测依据与体积、延迟数据见 [chinese-lookup-decision.md](chinese-lookup-decision.md)；trigram 对 2 字中文查询无效。
5. **旧工程处置**：直接删除，还是保留在 `legacy/` 分支/目录一个版本周期？
6. **UI 框架（已定）**：**保留 WinUI 3**，不做 UI 迁移；原“P3 结束 Go/No-Go 是否转 Avalonia”的决策门已取消，仅保留 L2 技术结论。

---

## 附录：证据索引

> 下表行号与项目路径基于重构前的快照，仅作历史定位；`Flint3.Interop`、`Flint/` 等工程已删除或移入 `legacy/`，现行结构以 `Flint.sln` 为准。

| 结论 | 代码位置 |
| --- | --- |
| 解决方案只含 3 个新工程 | `Flint.sln` 第 6-11 行 |
| 旧工程为 UWP + .NET Native | `Flint/Flint.csproj` 第 14-15、51、238-246 行 |
| 新工程为 WinUI 3 / .NET 10 | `Flint3/Flint3.csproj` 第 4-22、107、122-123 行 |
| 新数据层声明 AOT 兼容 | `Flint3.Data/Flint3.Data.csproj` 第 8-12 行 |
| C++/CLI 与 v145 | `Flint3.Interop/Flint3.Interop.vcxproj` 第 29、39-41 行 |
| 低级键盘钩子 | `Flint3.Interop/KeyboardHook.cpp` 第 28-39 行 |
| 按键名解析 | `Flint3.Interop/keyboard_layout.cpp` 第 55-58 行 |
| 只有前缀查询 | `Flint3.Data/StarDictDataAccess.cs` 第 94-120 行 |
| 搜索无取消、async void | `Flint3/ViewModels/MainViewModel_Flint.cs` 第 38-67 行 |
| 数据库初始化未 await | `Flint3/ViewModels/MainViewModel_Flint.cs` 第 28-32 行 |
| 建索引 SQL 被注释 | `Flint3.Data/StarDictDataAccess.cs` 第 20-42 行 |
| 设置依赖包身份 | `Flint3/Helpers/SettingsService.cs` 第 25 行；`StorageFilesService.cs` 第 25 行 |
| 用户词库路径 | `Flint3/ViewModels/MainViewModel_Glossary.cs` 第 398-401 行 |
| 包标识与版本 | `Flint3/Package.appxmanifest` 第 10-12 行；`Flint/Package.appxmanifest` 第 9-12 行 |
| 数据库文件未入库 | `.gitignore` 第 365 行；`Flint3/Data/README.md` |
| 发布路径/证书硬编码 | `Flint3/Flint3.csproj`（已移除，改为 `WindowsPackageType=None` + 自包含 `win-x64`，见 §10.1） |
| SQLite 漏洞告警 | 实测 `dotnet build` 的 `NU1903` |
