# AOT 实测报告（P3）

日期：2026-11-09　口径：L1（Data/Core 真 NativeAOT，UI 走 R2R + CsWinRT AOT，不裁剪）

## 结论

1. **L1 达成**：`Flint3.Core`、`Flint3.Data` 可 NativeAOT 发布。`tools/Flint.Cli` 在真实 851MB 词典库上查询结果与 JIT 版本完全一致；进程总耗时 103ms → 7ms，常驻内存 41.0MB → 15.2MB，发布体积 104.4MB → 6.9MB（不含 PDB）。
2. **UI 侧 R2R 生效且可降启动**：同一发布方式下，R2R 让"窗口可见"中位数从 785ms 降到 600ms，**"可输入"从 1209ms 降到 849ms**（约 -30%），代价是发布体积 +47.6MB。
3. **修复了发布流水线缺陷**：此前 UI 的 `publish` 产物缺少整个 `Assets` 目录与 XAML 产物（`.xbf`、`Flint3.pri`），导致发布版启动后无窗口（`MainWindow` 构造读取图标抛异常 → `App` 类型初始化失败）。修复后发布产物可正常启动并查词。
4. **L2（整应用 NativeAOT）不在本期范围**，依据见文末。

## 环境与测量口径

| 项 | 值 |
| --- | --- |
| 机器 | Intel Core i5-8300H / 32GB / Windows 11 专业版（非最低配机型） |
| SDK | .NET SDK 10.0.402，Windows App SDK 1.8.251106002 |
| 词典库 | `Flint3/Data/stardict.db`，851,288,064 字节 |
| CLI 测法 | 进程内 `Stopwatch`：`init_ms` 为打开数据库、`total_ms` 为 Main 入口到输出结果；内存取 `WorkingSet64` |
| UI 测法 | 外部脚本每 10ms 轮询 UIA：`window_ms` = 进程启动到 `MainWindowHandle` 非零，`content_ready_ms` = 搜索框可被 UIA 找到 |
| 采样 | 每项 3 次取中位数；首次样本单独列出（OS 文件缓存已热，真机冷启动会更慢） |

## L1：CLI 实测（JIT 自包含 vs NativeAOT）

命令：

```powershell
dotnet publish tools/Flint.Cli -c Release -r win-x64 -p:PublishAot=true
Flint.Cli <dbPath> <match|query|pattern|bench|json> <word> [limit]
```

| 指标 | JIT 自包含（无裁剪/无 R2R） | NativeAOT |
| --- | --- | --- |
| 进程总耗时（中位） | 103 ms | **7 ms** |
| 首次样本 | 615 ms | 51 ms |
| 数据库初始化（中位） | 76 ms | **6 ms** |
| 常驻内存 WorkingSet | 41.0 MB | **15.2 MB** |
| 200 次连续查询 p50 | 0.14 ms | **0.11 ms** |
| 200 次连续查询 p95 | 0.31 ms | **0.22 ms** |
| 发布体积 | 104.4 MB / 204 个文件 | **6.9 MB**（`Flint.Cli.exe` 5.02MB + `e_sqlite3.dll` 1.89MB，另有 25.4MB PDB 不进分发） |

查询结果示例（两种发布的 `words=` 输出完全一致）：

```
apple,apple acid,apple a day...,apple-a-day,appleade,appleades,Apple AirPlay,Apple AirPrint,apple-and-biscuit microphone,apple and pear
```

## AOT 下各项依赖的实际表现

| 依赖 | 表现 |
| --- | --- |
| CsWin32 源生成 P/Invoke | NativeAOT 编译 0 个 `IL2xxx/IL3xxx` 警告；`json` 模式调用 `KeyNameResolver` 解析出 `Win + Ctrl + A`，说明生成的 P/Invoke 未被裁剪 |
| JSON 源生成（`FlintJsonContext`） | 均走源生成上下文，AOT 下序列化/反序列化往返成功：`{"win":true,"ctrl":true,"alt":false,"shift":false,"code":65}` |
| SQLite 原生依赖 | `Microsoft.Data.Sqlite` 10.0.14（`SQLitePCLRaw` 2.1.13）；`e_sqlite3.dll` 自动复制到发布目录，**无需显式 `Batteries_V2.Init()`** 即可查询（bundle 的模块初始化在 AOT 下被保留） |
| zstd 解码（`ZstdSharp.Port`） | 纯托管实现，NativeAOT 编译通过、无 IL 警告；CSF 查询在 AOT 下与 JIT 输出一致（见 [stardict-storage-analysis.md](stardict-storage-analysis.md#8145-aotl1)） |
| 裁剪告警 | `Flint3.Core`/`Flint3.Data` 开启 `IsAotCompatible` 后构建无 IL 警告 |
| 中文反查索引（2026-11-11 复测） | 纯托管 mmap + `unsafe` 指针 + 由 `GeneratedRegex` 源生成的中文词条抽取；AOT 发布 0 个 IL 警告。真实 csf + 75.92 MB 索引：`index_exact` p50 0.003 / p95 0.004 ms，`index_prefix` p50 0.008 / p95 0.413 ms，端到端 p50 2.445 / p95 3.163 ms；`match apple 10` 进程总耗时 1~3 ms（热），WorkingSet 13.1 MB |

体积口径更新：本次复测 `Flint.Cli.exe` 7.31 MB + `e_sqlite3.dll` 1.98 MB = **9.29 MB**（不含 PDB），高于上方表格记录的 6.9 MB。差额来自上表之后接入的 mdx（`Flint3.Dict`）与 CSF（xxHash、Zstd），与中文反查无关——实测把中文抽取里的正则全部改成手写扫描只减少约 0.18 MB，故未保留该改动。

已知告警：AOT 发布时 `Microsoft.DotNet.ILCompiler` 10.0.13（SDK 10.0.402 自带）触发 `NU1903`（GHSA-p3jp-9v83-76w9）。它是构建期编译器工具、不进入发布产物；未显式覆盖版本以避免与 runtime pack 版本错配。

## UI：R2R + CsWinRT AOT（不裁剪）

配置：`PublishReadyToRun=true`、`PublishTrimmed=false`、`CsWinRTAotOptimizerEnabled=true`。

| 指标 | R2R 关闭 | R2R 开启 |
| --- | --- | --- |
| 窗口可见（中位） | 785 ms | **600 ms** |
| 可输入-搜索框就绪（中位） | 1209 ms | **849 ms** |
| 首次样本（窗口可见） | 3020 ms | 2553 ms |
| 常驻内存 WorkingSet | 183.7 MB | 176.2 MB |
| 发布体积（不含 `stardict.db`） | 231.3 MB | 278.9 MB |
| `Flint3.dll` | 628 KB | 1212 KB（R2R 已编译的标志） |

功能验证（发布目录直接双击、免打包）：启动后窗口可见，输入 `apple` 后列表首项为 `apple`，与 CLI 查询结果一致。

### 发布流水线修复

- **现象**：`dotnet publish` 产物里没有 `Assets/**`、14 个 `.xbf` 与 `Flint3.pri`；进程能起、也有窗口对象，但从不显示。
- **根因**：WinUI 的 `Assets` 只作为 `Content` 参与 build 阶段的复制（以及 AppX 打包），`.xbf`/项目 `.pri` 只经由 `_AllChildProjectItemsWithTargetPath` 进入 build 输出；三者都不会进入 `ResolvedFileToPublish`。运行时 `MainWindow` 构造读取 `Assets/Logos/flint_logo.ico` 抛 `FileNotFoundException`，`App` 类型初始化失败，`OnLaunched` 不再执行。
- **修复**（`Flint3/Flint3.csproj`）：
  - `<Content Update="Assets\**" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />`
  - `IncludeWinUIXamlArtifactsInPublish` 目标在 `ComputeFilesToPublish` 后把 `*.xbf` 与 `$(AssemblyName).pri` 补进 `ResolvedFileToPublish`。

## L2 结论：整应用 NativeAOT 不做

- WinUI 3 / Windows App SDK 依赖 XAML 运行时元数据与反射，官方要求 `PublishTrimmed=false`；本次 L1 也确认 `CsWinRTAotOptimizerEnabled` 只是生成 WinRT marshalling 代码，不改变 UI 进程的 JIT 运行时。
- 若要整应用 AOT，必须更换 UI 框架（Avalonia 是唯一可选项），等于 8.4k 行 UI 重写 + 托盘/单实例/主题/免打包集成重做，成本见计划 §3.4；与当前"Windows-only + 免打包自包含"的产品形态不匹配。
- 因此 L2 明确结论：**不在本期范围**，P4 之后若出现硬性体积/启动预算再重新评估。

## 未覆盖 / 限制

- UI 未启用裁剪，UI 侧 `CsWinRT1028`（类型未标 `partial`）与 `MVVMTK0049`（使用 `[INotifyPropertyChanged]`）是既有分析器提示，与 L1 无关，本次未处理；`Marshal.SizeOf(typeof(...))`（`NotifyIconTools`）同样只影响裁剪场景，未改。
- 测量机器不是最低配机型，且 OS 文件缓存已热；"可输入"依赖 UIA 轮询（10ms 粒度），不适用于评估亚百毫秒差异。
- JSON 源生成的运行时验证在 CLI（AOT）完成；UI 进程未 AOT，其设置读写路径由 P2 单测覆盖。
