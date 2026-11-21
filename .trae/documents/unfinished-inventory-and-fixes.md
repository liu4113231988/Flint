# 未完成项盘点与本轮修复计划

## Context

用户要求以**代码实际行为**为准，核对 `docs/refactor-plan.md`、`README.md`、`docs/build.md`、`docs/aot-report.md`、`docs/stardict-storage-analysis.md` 的声明与代码，产出三类清单，并优先修复“影响用户可用性 > 数据正确性 > 性能 > 整洁度”中本轮可完成的项。约束：不改动已入库的 CSF 格式与索引版本位；需产品决策或风险过大的项停下说明；每块自测后再继续。

盘点已完成（证据见下）。本轮只修复可安全落地、且能自测的三块：mdx 导入可取消/不卡 UI、初始化注释失真、文档与实现不符。汉英查词与 100 词 mdx 黄金用例因触碰 CSF 格式或缺少样本生成能力，本轮不动。

---

## 一、盘点清单

### (a) 计划内但确认未完成

| # | 项 | 依据 | 当前代码实际状态 | 缺失程度 |
|---|---|---|---|---|
| a1 | 汉英查词（`stardict_zh` + FTS5 trigram / LIKE 兜底） | refactor-plan P4 交付物 3（L363-366）、验收 L380、总表 L504 | 全仓库无 `ChineseIndexDataAccess`、无 `stardict_zh`、无中文索引；`WordQueryService` 仅英文精确/前缀/模式 | 100%，**阻塞**：需扩展 CSF 格式（违反本轮“不改格式”约束）或另建索引，且 §10.4 中文匹配语义待决策 |
| a2 | mdx 100 词查询黄金用例 + 3 类样本词典 | P4 交付物 5（L368）、验收 L379、总表 L504 | `basic.mdx` 仅 3 条（alpha/beta/gamma）；另有加密样本；无 100 词 fixture、无生成脚本 | 100%，`tools/` 仅 `prepare-data.ps1`、`Flint.Cli`、`wordle/gen_wordle_words.py`，无 mdx 样本生成器 |
| a3 | mdx 导入“500MB 不卡 UI、可取消” | P4 验收 L381 | `ImportAsync` 已接收 `CancellationToken` 并传给索引构建；但 UI 未传 token、无取消按钮，且 `File.Copy` 同步执行在 UI 线程 | 部分：取消能力代码具备，UI 与大文件复制路径缺失（本轮修复） |
| a4 | mdx 词库优先级 | P4 交付物 2（L362）、UI 交付物 4（L367） | `MdxDictionaryInfo` 仅 `Id/Name/FileName/IndexFileName/EntryCount/Enabled`，无 `Priority`；`CreateSources()` 按登记顺序返回 | 完全缺失 |
| a5 | 连续 200 次唤起无泄漏 | P1 验收 L290 | 无自动化用例、无实测记录 | 未验证 |
| a6 | 冷启动连续 20 次“启动即输入”无空结果 | P0 验收 L263、总表 L500 | 无实测记录 | 未验证 |
| a7 | 最小 CI（windows-latest build+test） | P6 L418（“若条件允许”） | 无 `.github/workflows` 等任何 CI 配置 | 未做 |

### (b) 文档声明/勾选与实际不符（如实指出）

| # | 项 | 依据 | 代码实际 | 说明 |
|---|---|---|---|---|
| b1 | D1“数据库初始化改为可等待 Task，查询前统一 await” | P0 交付物 5（L253）、D1（L107） | `StarDictDataAccess.InitializeDatabase` 返回 `Task.CompletedTask`（`StarDictDataAccess.cs:35-47`），不执行任何初始化 | 未按原方案实现；实际是“首次查询时同步惰性建后端”，功能可用但计划措辞失真 |
| b2 | `MainViewModel_Flint` 注释称“查询方法会等待同一个初始化任务” | `MainViewModel_Flint.cs:68-69` | 只调用无副作用的 `_ = StarDictDataAccess.InitializeDatabase();`，查询并不 await 任何任务 | **注释误导**，本轮修正 |
| b3 | P0 交付物 1 仍写“解压 `stardict.7z.001/002`、校验 db 行数、验证 sw/word 索引” | refactor-plan L249 | 现流程为 `stardict.csf.7z.001/002` 解压 + SHA256 + `verify`，仓库不再分发 db 分卷 | 文档过期 |
| b4 | P4 UI 为独立 `Views/DictionarySourcesPage` | P4 交付物 4（L367）、§6 L448 | 实际并入 `Views/SettingsPage.xaml` 的“mdx 词库”卡片（导入/启停/删除/进度） | 文档过期 |
| b5 | Wordle 词表来自 db 的 `wordle_words` 表 | §7 L479 | 实际为嵌入资源 `Flint3.Core/Wordle/wordle_words.json`，由 `tools/wordle/gen_wordle_words.py` 生成 | 文档过期 |
| b6 | README “`tools/`：数据准备、mdx 测试数据与 Wordle 词表生成脚本” | README L43 | `tools` 下无 mdx 测试数据脚本 | 文档不符 |
| b7 | README “`Flint3.Data` 指新的 SQLite 数据层” | README L60 | 现为 CSF 优先、SQLite 回退 | 文档不符 |
| b8 | 附录“证据索引”仍引用重构前文件/行号 | refactor-plan L523-541 | 多数行号已失效 | 整洁度问题 |

### (c) 已明确“不做/留待后续”（本轮不处理）

- 整应用 NativeAOT（L2）：`docs/aot-report.md` §L2 结论明确不做。
- mdx 加密、LZO、mdx 1.2、GBK；`.mdd` 资源：README 已知限制 + P4 风险。
- UI 裁剪、`CsWinRT1028`/`MVVMTK0049`、`Marshal.SizeOf(typeof(...))`：aot-report “未覆盖/限制”。
- FST、Parquet(.NET 未测)、LMDB 延迟、CSF 冷缓存、Defender 评估：storage-analysis §7/§8.13。
- 应用内语言切换、自启仅 HKCU。
- `Flint3` → `Flint` 重命名：P6 明确“需单独立项”。

---

## 二、本轮修复

### 块 1（可用性）：mdx 导入可取消且不阻塞 UI

**文件**
- `Flint3.Data/Mdx/MdxDictionaryStore.cs`
- `Flint3/Views/SettingsPage.xaml`、`SettingsPage.xaml.cs`
- `tests/Flint3.Data.Tests/MdxSourceTests.cs`

**改动**
1. `ImportAsync` 中的 `File.Copy` 换成可取消的分块异步复制（1MB buffer，`ReadAsync/WriteAsync` + `ThrowIfCancellationRequested`），复制阶段映射到进度 0–0.15，索引构建阶段映射到 0.15–1.0；保持 `IProgress<double>` 总区间仍为 0–1，UI 无需改换算。
2. 复用现有 `try/catch` 清理逻辑：取消时删除已复制 mdx 与半成品索引（`catch` 已 `TryDelete` 后重抛）。
3. SettingsPage 增加 `CancellationTokenSource` 字段与“取消”按钮（导入时显示），把 token 传入 `ImportAsync`；捕获 `OperationCanceledException` 提示“已取消导入”，仅在成功时刷新词库列表与查询源。
4. 新增单测：
   - 传入已取消 token：抛 `OperationCanceledException`，登记为空、目录无残留文件。
   - 用同步 `IProgress<double>` 在首次进度回调时取消：验证索引构建中断后同样无残留。

**不改**：`MdxIndexDatabase` 的索引结构、`dictionarySources.json` 格式、`MdxDictionaryInfo` 字段。

### 块 2（整洁度/正确性）：移除无效预热与误导注释

**文件**：`Flint3/ViewModels/MainViewModel_Flint.cs`

- 删除 `_ = StarDictDataAccess.InitializeDatabase();` 及“查询方法会等待同一个初始化任务”的注释，改为说明“后端在首次查询时惰性创建（CSF 为 mmap，打开成本低）”。
- 不动 `StarDictDataAccess` 公开签名与后端逻辑。

### 块 3（文档一致性）：校正 (b) 清单

**文件**：`README.md`、`docs/refactor-plan.md`

1. README：
   - L43 `tools/` 描述改为“数据准备、CSF 构建/校验 CLI 与 Wordle 词表生成脚本”。
   - L60 数据层描述改为“CSF 只读格式优先、SQLite 回退”。
2. refactor-plan：
   - P0 交付物 1 改写为当前 csf 分卷解压 + SHA256 + `verify` 流程，并注明源 db 需从 ECDICT 上游获取。
   - P0 交付物 5 / D1 标注实际方案（惰性后端，未引入可等待初始化）与理由，避免“已完成”误读。
   - P4 交付物 4 注明 UI 实际落在 `SettingsPage`，无独立页面。
   - §7 Wordle 词表来源改为嵌入 JSON + 生成脚本。
   - 附录证据索引加一句“行号基于重构前快照，仅作历史定位”。
   - 在 P0/P1/P4 验收处标注 a1/a2/a5/a6 未完成状态（不伪装成已验收）。

### 本轮明确不做（需决策/风险大/不划算）

- **a1 汉英查词**：需改 CSF 格式或新增索引与版本位，直接违反本轮约束；且 §10.4 语义未决策。仅更新文档状态。
- **a2 100 词 mdx 黄金用例**：仓库无 mdx 写入器/样本生成脚本，伪造 100 词 fixture 需先实现 mdx 打包，风险与工作量不成比例。保持未完成标注。
- **a5/a6 200 次唤起、20 次冷启动**：需真实 UI 自动化与长时间采样，当前环境无法给出可信数据，不提交半成品。
- **a7 CI**：P6 标注“若条件允许”；windows-latest 构建 WinUI/WindowsAppSDK 易受工作负载与缓存影响，本轮不引入易红流水线。

---

## 三、验证

1. `dotnet build Flint.sln -c Debug -p:Platform=x64` 0 error。
2. `dotnet test tests\Flint3.Data.Tests\Flint3.Data.Tests.csproj`、`tests\Flint3.Core.Tests`、`tests\Flint3.Dict.Tests` 全绿（记录新增用例数）。
3. 手动冒烟：启动 Debug 构建，设置页导入 `tests\Flint3.Data.Tests\Fixtures\basic.mdx`，确认导入进度、成功提示、查询源刷新；再导入时点“取消”确认提示且无残留登记/文件。
4. 回归确认：`Flint3.Data` 与 `StarDictDataAccess` 公开签名未变；未触碰 CSF 格式与索引版本位；`git status` 无 bin/obj/运行产物。

## 四、提交与推送

1. `fix(mdx): 导入支持取消且复制不阻塞 UI`（Data + UI + 测试）。
2. `refactor(vm): 移除无效的数据库预热调用与误导注释`。
3. `docs: 校正未完成项盘点与实现不符的说明`。
4. 推送前 `git fetch origin` 确认无新提交/分叉；普通推送，不改写历史；报告哈希、推送结果与最终 `git status`。

## 五、风险

- 进度映射从“索引 0–1”变为“复制 0–0.15 + 索引 0.15–1”，仅影响显示节奏，不改契约；单测不依赖具体进度值。
- 取消时索引临时文件清理依赖 `MdxIndexDatabase.Build` 既有 `try/finally`，需在测试中确认无残留后再提交。
- `Flint3/Flint3.csproj` 仍有 stat-only 的 `M` 状态（`git diff` 为空），提交时按文件名显式 `git add`，不纳入。
