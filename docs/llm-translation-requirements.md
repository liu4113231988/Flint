# LLM 翻译功能调研与需求（待评审）

> 目的：为“是否加、加到什么程度、按什么语义加”提供可判断的依据，不做方案宣传。
> 依据：本仓当前代码（`master`，版本号 3.4.3）与实际词典数据；本轮未接入任何 LLM 服务，未新增依赖，未改产品代码。
> 口径：标“代码事实”的均可对到文件；标“估算”的给出推导；标 **待确认** 的表示本轮无法用代码或实测证实，需实现前补测或由产品拍板。

---

## 0. 结论摘要

- 翻译**不应做成 `IWordSource`**。`IWordSource` 的契约是“按词条查询并返回 `WordEntry`”，而翻译是“把一段文本发给外部服务并拿回一段文本”，失败态、延迟、成本、并发语义都不同。做成源会被 `WordQueryService` 的并行收集、异常吞没与排序逻辑吃掉。
- 建议做成**独立服务 + 独立 UI 区块**：服务只接受“原文 + 方向 + 取消令牌”，返回结构化结果；UI 在词条展开后的正文末尾追加一块“AI 翻译”，不替换、不并入现有 `Translation`。
- 现状有两条硬约束必须写进实现前提：① `MatchWord` 每次键入都会触发查询（仅 300 ms 防抖），且会用正则**删掉空格与标点**，整句翻译拿不到原始句子；② 详情面板是 `FlintLitePage` 与 `FlintFullPage` 两份**几乎逐行重复**的 `Expander` 模板，且 `x:Bind Translation` 是 OneTime，异步/流式内容不会刷新。
- 协议上，**MVP 只做 OpenAI 兼容的 `/chat/completions`（非流式）** 即可覆盖国内主流实现与本地推理（Ollama / LM Studio / vLLM）；Anthropic 与 Gemini 放到后续，但配置模型要预留 `protocol` 字段，避免以后改配置格式。
- **默认关闭**。开启前必须一次性告知“词条文本会发送到你配置的第三方地址”；密钥用 DPAPI 加密后存入现有设置存储（不提供明文存储选项），并明确“密钥不随配置迁移”。
- 成本不是主要风险：按真实数据估，单次翻译约几百 input tokens、几十到几百 output tokens，单次成本远低于 0.01 元；**风险在请求次数**（自动翻译 + 每次键入 = 请求风暴），所以默认手动触发 + 内存缓存 + 全局最多 1 个在途请求。
- 技术面几乎零新增依赖：`HttpClient`、源生成 JSON、手写 SSE 解析都在 BCL 内；但 `System.Security.Cryptography.ProtectedData` 在 .NET 上是独立包，若坚持 0 新增依赖，需走 CsWin32 的 `CryptProtectData` 或改用凭据管理器。

---

## 1. 与现有架构的接合点

### 1.1 查询链路现状（代码事实）

| 阶段 | 文件 | 实际行为 | 对翻译的影响 |
|---|---|---|---|
| 输入 | [FlintFullPage.xaml.cs](file:///d:/workingfold/Flint/Flint3/Views/FlintFullPage.xaml.cs)、[FlintLitePage.xaml.cs](file:///d:/workingfold/Flint/Flint3/Views/FlintLitePage.xaml.cs) | `SearchTextBox_TextChanged` 每次键入直接调用 `ViewModel.MatchWord`，无回车/搜索按钮 | 若做成自动翻译，每敲一个字符都会产生一次外部请求 |
| 入口 | [MainViewModel_Flint.cs](file:///d:/workingfold/Flint/Flint3/ViewModels/MainViewModel_Flint.cs) | 300 ms 防抖；`BeginRequest()` 取消上一次；空输入清空集合；`?*_^$` 走模式搜索 | 可复用“取消旧请求”的模式，但翻译需独立实例 |
| 清洗 | 同上 | `WordSearchRegex().Replace(word, "")` 删除全部非 `\w` 字符 | **整句会被压成无空格串**（`Hello, world!` → `Helloworld`），翻译必须用清洗前的原始文本 |
| 分发 | [WordQueryService.cs](file:///d:/workingfold/Flint/Flint3.Core/Query/WordQueryService.cs) | 各源 `MatchAsync` 并行；`CollectAsync` 补 `Source`；`Rank` 按 精确/前缀/其他 三组排序 | 翻译结果若混进来，会被当候选词条排序与去重 |
| 容错 | 同上 | `SafeQueryAsync` 吞掉单源异常只写 `Trace.WriteLine`；`OperationCanceledException` 透传 | 翻译失败会被静默成“无结果”，用户无从判断是没查到还是请求失败 |
| 取消 | [SearchCoordinator.cs](file:///d:/workingfold/Flint/Flint3.Core/Query/SearchCoordinator.cs) | 单实例持 `CancellationTokenSource`，序号校验 `IsCurrent(sequence)` | 若翻译复用它，会与词条查询互相取消 |

### 1.2 为什么不做成 `IWordSource`（逐条对应代码）

`IWordSource` 只有三个方法：`QueryAsync`（精确）、`MatchAsync`（联想）、`PatternAsync`（模式），签名见 [IWordSource.cs](file:///d:/workingfold/Flint/Flint3.Core/Query/IWordSource.cs)；返回体是 [WordEntry.cs](file:///d:/workingfold/Flint/Flint3.Core/Query/WordEntry.cs)（`Word`/`StripWord`/`Phonetic`/`Definition`/`Translation`/`Exchange`/`Source`）。

1. **契约不匹配**：翻译没有“词条”，`QueryAsync`/`PatternAsync` 对翻译无意义，只能返回空，接口被撑变形。
2. **调用频率不匹配**：`MatchAsync` 是“每键入一次调一次”。接上去等于把本地毫秒级查询换成网络请求，直接违背现有 300 ms 防抖的体验基线。
3. **错误不可见**：`SafeQueryAsync` 捕获所有非取消异常后只写 Trace，翻译的 401/429/超时会全部退化成“查不到”。
4. **排序会污染**：`Rank` 按“输入串 vs `entry.Word`”分 精确/前缀/其他 三组；翻译文本没有对应关系，只能落在“其他”组，并与真实词条争抢 10 条上限。
5. **状态表达不了**：需要区分“未配置 Key / 正在请求 / 被取消 / 限流 / 额度耗尽 / 内容不合规”，`IReadOnlyList<WordEntry>` 没有位置放这些状态。

**结论**：新增独立服务（建议 `ILlmTranslationService`），返回专用结果类型（成功文本 + 用量 + 错误种类 + 是否可重试）。与现有链路的唯一交集是“取消/序号校验”这一模式，建议**新开一个 `SearchCoordinator` 实例**（或同构的轻量协调器），不复用词条查询那一个。

### 1.3 服务的职责边界

- 输入：`text`（原始文本，未经正则清洗）、`direction`（Auto/En2Zh/Zh2En）、`CancellationToken`。
- 输出：`TranslationResult { Text, DetectedDirection, PromptTokens, CompletionTokens, ElapsedMs }` 或 `TranslationFailure { Kind, Message, Retryable }`。
- 不负责：写回 `StarDictWordItem`、拼 prompt 之外的 UI 文案、持久化（缓存由服务内 LRU 承担，见 §3.5）。
- 放置建议：抽象与 HTTP 适配放 `Flint3.Core`（可单测、需保持 AOT 兼容）；密钥读取与 DPAPI 放 `Flint3`（App 层，Windows 专有）。

### 1.4 UI 落点与数据模型

**现状（代码事实）**

- 详情面板不是一个独立控件，而是 `FlintLitePage.xaml` 与 `FlintFullPage.xaml` 中各自一份 `ListView.ItemTemplate` + `muxc:Expander`；正文是 `Expander.Content` 里的 `Translation` → `Definition` → `Exchanges` 三个块。两份模板结构几乎一致，任何改动都要同步两处。
- 数据项是 [StarDictWordItem.cs](file:///d:/workingfold/Flint/Flint3.Data/Models/StarDictWordItem.cs)：继承 `ObservableObject`，但只有 `Description` 与 `Color` 通过 `SetProperty` 通知；`Word`/`Translation`/`Definition`/`Exchanges` 等全是普通 `{ get; set; }`。
- XAML 里 `Text="{x:Bind Translation}"` 未写 `Mode`，即 OneTime（`MakeupWordItem` 在构造时一次性填好）。**这意味着把翻译结果写回 `Translation` 不会刷新界面**——这也是“整体替换原释义”方案在现有实现下最贵的部分。
- Lite 页是紧凑弹窗：搜索框 56 px + 分隔线 + 结果列表，没有为长文本预留空间。

**三种落点对比**

| 方案 | 改动 | 能否承载流式 | 与原文关系 | 风险 |
|---|---|---|---|---|
| A. 写回 `StarDictWordItem.Translation` | 改 `Translation` 为可通知属性，两份模板都要改绑定 | 可以，但需高频 `PropertyChanged`，列表虚拟化下易卡 | **覆盖**原释义，用户失去本地词典结果 | 高：改变现有语义与 OneTime 性能假设 |
| B. 新增 `AiTranslation` 属性（可通知）挂在同一 item 上 | 加属性 + 两份模板各加一块 | 可以 | 追加/并列 | 中：item 变成“本地结果 + 网络状态”混合体，`MakeupWordItem` 需重置 |
| **C. 独立翻译 VM + 独立区块（推荐）** | 两块模板各加一个区块，绑定到独立的 `TranslationViewModel` | 可以 | 追加，完全不动原释义 | 低：与既有 OneTime 语义解耦，失败/加载态好表达 |

**建议**：
- 主窗（`FlintFullPage`）承载翻译区块，位置在 `Expander.Content` 的 `Exchanges` 之后，标题“AI 翻译”，内含：加载指示、结果文本（`IsTextSelectionEnabled`）、方向标签、耗时/模型脚注、重试按钮。
- Lite 页**不承载长文本**，只保留一个“在主窗翻译”的按钮（打开主窗并定位到该词）。
- 触发按钮放在 `Expander.Header` 右侧、与 TTS/生词本按钮同一排，受设置开关控制可见性（与 `EnableTTS`/`EnableGlossary` 的绑定方式一致）。

### 1.5 设置页扩展

**现状（代码事实）**：`SettingsPage.xaml` 是 38 px 标题 + 1 px 分隔线 + `ScrollViewer`/`StackPanel`，内容用 `CommunityToolkit.WinUI.Controls` 的 `labs:SettingsCard` / `labs:SettingsExpander`；已有“主题 / 背景 / 英文释义 / 生词本（可展开子卡片）/ 词典（mdx）/ 搜索框样式 / 开机自启 / 关于与版本”。`SettingsPage.xaml.cs` 里已有可复用的交互：`ShowDictionaryMessage`（InfoBar 提示）、`_importCancellation`（长任务取消）、`OnStartupSettingsCardLoaded`（异步加载系统状态）、`OnClickDbPath`（打开目录）。

**建议结构**（新增一个 `SettingsExpander`，放在“生词本”之后、“词典”之前）：

```
AI 翻译                                  [ToggleSwitch: 总开关，默认关]
  说明：文本将发送至你配置的服务，不经过 Flint 服务器
  ├ 服务协议      [ComboBox: OpenAI 兼容 / Anthropic / Gemini（MVP 仅第一项可选）]
  ├ 接口地址      [TextBox: https://api.example.com/v1]
  ├ 模型          [TextBox: 例如 deepseek-chat]
  ├ API Key       [PasswordBox: 已保存时显示占位符，不显示明文]
  ├ 自定义 Header [可选，1~3 行 name: value]
  ├ 高级          [超时 30s / max_tokens 512 / 缓存条数 200]
  └ [测试连接]  → InfoBar（成功显示模型回复首 20 字；失败显示错误种类）
```

要点：
- 总开关关闭时，其余子卡片 `IsEnabled=False`（与生词本子卡片同模式）。
- `x:Load="True"` 懒加载，避免设置页启动即读密钥。
- 测试连接复用 InfoBar，不弹新窗口。
- 版本号与“关于”区不动；若需要展示“已用请求数”，放在该 `SettingsExpander` 内，不塞进关于区。

### 1.6 `ISettingsStore` 能否承载密钥

**现状（代码事实）**：[ISettingsStore.cs](file:///d:/workingfold/Flint/Flint3.Core/Settings/ISettingsStore.cs) 只有一个索引器 `object? this[string key]`；打包态实现走 `ApplicationData.Current.LocalSettings`，免打包实现落到 `%LOCALAPPDATA%\Flint3\settings.json`（[JsonFileSettingsStore.cs](file:///d:/workingfold/Flint/Flint3.Core/Settings/JsonFileSettingsStore.cs)）。两者都是**明文键值**，没有加密、没有访问控制。

结论：**不能把 API Key 明文写进去**。可选方案见 §5.1；推荐“DPAPI 加密后的 Base64 字符串”复用现有 store 的 key（例如 `Llm.ApiKeyProtected`），加密层只在 App 层出现，`ISettingsStore` 本身不改。

---

## 2. 接口归一

### 2.1 三家协议差异（实现前必须逐项处理）

| 维度 | OpenAI 兼容 | Anthropic Messages | Google Gemini |
|---|---|---|---|
| 端点 | `{base}/chat/completions`（base 由用户填，通常已含 `/v1`） | `{base}/v1/messages` | `{base}/v1beta/models/{model}:generateContent` |
| 鉴权 | `Authorization: Bearer <key>` | `x-api-key: <key>` + `anthropic-version: 2023-06-01` | `x-goog-api-key: <key>`（**不要**用 `?key=`，会进 URL/日志） |
| 系统提示 | `messages[0].role="system"` | 顶层 `system` 字段（字符串或 block 数组） | `systemInstruction.parts[].text` |
| 必填字段 | `model`、`messages` | `model`、`max_tokens`（**必填**）、`messages` | `contents` |
| 消息角色 | `system`/`user`/`assistant` | `user`/`assistant` | `user`/`model`（无 system 角色） |
| 正文取值 | `choices[0].message.content` | `content[]` 中 `type=="text"` 的 `text` 拼接 | `candidates[0].content.parts[].text` 拼接 |
| 结束原因 | `choices[0].finish_reason`（`stop`/`length`/`content_filter`） | `stop_reason`（`end_turn`/`max_tokens`/`stop_sequence`） | `candidates[0].finishReason`（`STOP`/`MAX_TOKENS`/`SAFETY`） |
| 用量 | `usage.prompt_tokens` / `completion_tokens` | `usage.input_tokens` / `output_tokens` | `usageMetadata.promptTokenCount` / `candidatesTokenCount` |
| 流式 | SSE，`data: {...}`，结束发 `data: [DONE]` | SSE，带 `event:` 行；取 `content_block_delta.delta.text` | `:streamGenerateContent?alt=sse`，否则是 JSON 数组分块 |
| 错误体 | `{"error":{"message","type","param","code"}}` | `{"type":"error","error":{"type","message"}}` | `{"error":{"code","message","status"}}` |
| 常见错误码 | 401 `invalid_api_key`、429 `rate_limit_exceeded` / `insufficient_quota`、400 `context_length_exceeded` | 401 `authentication_error`、429 `rate_limit_error`、529 `overloaded_error` | 400 `INVALID_ARGUMENT`、403 `PERMISSION_DENIED`、429 `RESOURCE_EXHAUSTED` |
| 内容拦截 | `finish_reason=content_filter`，正文可能为空 | `stop_reason=refusal` | `promptFeedback.blockReason` / `finishReason=SAFETY` |

以上字段名按各厂商公开文档整理；**具体版本行为与限额需在实现前用真实 Key 验证（待确认）**。

### 2.2 最小公共子集

三家都能表达的最小请求是：

```
system: 一句翻译指令
user:   <待翻译文本>
→ 纯文本回复（非流式、单轮、无工具调用、无多模态）
```

因此抽象只需要 4 个配置项 + 1 个枚举：

```
Protocol (OpenAICompatible | Anthropic | Gemini)
BaseUrl  (用户填完整前缀，代码只拼路径，不自动加 /v1，避免重复)
ApiKey
Model
ExtraHeaders (可选，name→value)
```

适配器内部各自组装请求体、解析回复与错误；对外统一暴露 §1.3 的结果类型。**不做厂商自动识别**（靠 baseUrl 猜厂商在自建网关上必然出错），由用户显式选协议。

### 2.3 各协议是否值得支持

| 协议 | 覆盖的典型实现 | 差异处理成本 | 建议 |
|---|---|---|---|
| OpenAI 兼容 | DeepSeek、通义（compatible-mode）、Kimi、智谱、硅基流动、OneAPI/NewAPI 网关、Ollama（`127.0.0.1:11434/v1`）、LM Studio、vLLM | 低（一套） | **MVP 必做** |
| Anthropic | Claude 官方及兼容网关 | 中：system 位置、`max_tokens` 必填、`x-api-key`、SSE 事件名不同 | 后续第一优先 |
| Gemini | Google AI Studio / Vertex 兼容层 | 中：`contents/parts` 结构、角色名 `model`、Key 走 header、错误体不同 | 后续，视用户需求 |

补充：**Ollama / LM Studio / vLLM 本身就是 OpenAI 兼容**，所以“本地模型”不需要额外适配，只要允许 `http://127.0.0.1:...` 这类无 Key 的 baseUrl。

---

## 3. 产品语义（含建议默认值）

### 3.1 触发方式

| 选项 | 说明 | 建议 |
|---|---|---|
| 展开即自动翻译 | 每次展开词条都发请求 | **不做**：请求数与费用不可控，且与 300 ms 防抖的“每键入一次”叠加 |
| 手动按钮 | 展开后点“AI 翻译” | **推荐默认** |
| 仅整句输入才翻译 | 输入含空格/标点或长度超阈值时出现“翻译整句” | **推荐并存**：作为整句场景的入口，见 3.2 |

默认值：**手动**。理由：单次成本虽低，但 Flint 是“输入即查”的高频工具，自动翻译会把本地查询变成网络依赖；手动还天然规避了“用户没打算外发文本”的隐私问题。

### 3.2 输入判定与翻译方向

- 判定（建议）：清洗前的原文满足任一条件即视为“整句”——含空格或标点；英文词数 ≥2；汉字数 ≥8。否则按“词条”翻译，内容 = `Word` + `Phonetic` + `Translation` 的摘要。
- 方向：**自动识别 + 可手动切换**。识别规则用 CJK 字符占比（建议 ≥30% 判为中文，走中→英；否则英→中）；识别结果在区块上显示为小标签，用户可一键反向。
- 整句入口：搜索框输入命中“整句”判定时，列表上方出现“翻译整句”按钮（不进入 `SearchResultWordItems`，避免污染词条列表）。

### 3.3 与“英查中 / 汉英反查”的关系与边界

- 翻译**不参与检索**：不实现 `IWordSource`，不写入 `SearchResultWordItems`/`LiteSearchResultWordItems`，不改变 `Rank`、不改变 10/40 条上限。
- **英查中**：本地 `Translation` 已给中文释义，翻译是“补充说明/例句/语境”，不是替代；所以默认**并列展示**而不是覆盖。
- **汉英反查**（`stardict.zhx`）：输入一个中文词仍走本地索引返回英文词条；只有当输入被判定为整句（见 3.2）或用户主动点“翻译整句”时才发请求。**不因为加了翻译就放宽中文单字/短语的本地查询路径**。
- 边界写死一条：翻译永不修改本地词典数据，永不写回 `stardict.db`/`stardict.zhx`。

### 3.4 结果呈现

- 位置：`Expander.Content` 末尾独立区块，标题“AI 翻译”，与 `Translation`/`Definition`/`Exchanges` 并列。
- 形态：结果文本可选中复制；脚注显示方向、模型、耗时；失败时区块内显示错误种类与“重试”。
- **不做整体替换**：现有 `x:Bind Translation` 是 OneTime，替换方案要改属性通知与两份模板的绑定语义，收益（少一块 UI）远小于风险（用户失去本地释义）。
- Lite 页只放“在主窗翻译”入口，不显示长文本。

### 3.5 缓存

- **MVP：内存 LRU**，key = `protocol + model + direction + SHA256(原文)`，容量建议 200~500 条，进程退出即失效。
- 不落盘：避免明文译文滞留、避免“生词/句子”隐私在磁盘上堆积。
- 命中缓存时不发请求，区块标注“来自缓存”。

### 3.6 失败与降级（逐场景）

原则：**任何翻译失败都不影响本地查询结果**，也不改 `SearchHint`。

| 场景 | 行为 | 文案建议 | 可重试 |
|---|---|---|---|
| 未配置 Key / 总开关关闭 | 按钮不显示或置灰 | “在设置中配置 AI 翻译后可在此翻译” | — |
| 401 / 403 | 区块内错误，不弹窗 | “API Key 无效或无权限，请检查设置” | 否（改配置后重试） |
| 429 限流 | 保留原文，显示冷却提示 | “请求过于频繁，请稍后再试” | 是（可显示 `Retry-After` 秒数） |
| 额度耗尽（`insufficient_quota` / `RESOURCE_EXHAUSTED`） | 区块内错误 | “账户额度不足” | 否 |
| 网络不可达 / DNS 失败 | 区块内错误 | “无法连接到 {host}，请检查网络或代理” | 是 |
| 超时（建议总 30 s、首字节 10 s） | 取消请求 | “请求超时” | 是 |
| 返回空内容 / 内容被拦截 | 显示“无可用的翻译结果”+ 原因 | 依据 `finish_reason` 映射 | 是 |
| 返回非 JSON / 结构不符 | 显示“返回内容无法解析” | 原文不展示（可能含模型杂讯） | 是 |
| 被新请求取消 | 静默，不显示错误 | — | — |

### 3.7 并发与取消

- **全局最多 1 个在途请求**（不按词条并发）。
- 切换词条或再次点击时：取消上一个请求（`CancellationTokenSource.Cancel()` + 序号校验），旧结果到达后丢弃。
- 服务层与 UI 层都要校验序号：HTTP 取消存在竞态，响应可能在取消后仍到达（与 `PublishResults` 的 `IsCurrent(sequence)` 同理）。
- 不做队列、不做优先级；用户重复点击视为“重试”。

---

## 4. 成本与配额

### 4.1 真实长度基数（来自现有数据）

- `stardict.db` 中 `translation` 含中文的记录 3,376,001 条，正文合计 95,652,519 B（UTF-8），**平均 ≈28.3 B/条**；其中 CJK 字符 25,451,032 个，**平均 ≈7.5 个汉字/条**。
- 结论：单个词条的待翻译内容通常在几十字节量级；真正的长输入只有“整句”场景。

### 4.2 token 估算（估算）

| 场景 | 输入构成 | input tokens | output tokens |
|---|---|---|---|
| 单词 | 系统指令 ~80~120 + 词条 20~60 | 约 120~200 | 约 30~120 |
| 短句（≤30 汉字） | 系统指令 + 句子 | 约 150~250 | 约 60~200 |
| 长句（≤200 汉字） | 系统指令 + 句子 | 约 300~450 | 约 150~400 |

注：中文 token 折算随分词器差异较大（约 1~1.5 汉字/token），上表按偏保守估计；**精确值需用目标模型的 tokenizer 实测（待确认）**。

### 4.3 单价区间（待确认）

- 国内低价模型常见区间约 ¥0.1~2 / 百万 input tokens、¥0.2~8 / 百万 output tokens；国际主流模型约 $0.15~5 / 百万 input tokens。
- 按上表推算：**单词单次 ≈ ¥0.0001~0.002；长句 ≈ ¥0.001~0.02**（量级估算，不构成报价）。
- 价格变动快，文档与设置页都**不写死单价**，只在帮助文本里给“按量计费”的提示。

### 4.4 成本控制手段

| 手段 | 是否纳入 MVP | 说明 |
|---|---|---|
| 手动触发 | 是 | 最大的一道闸 |
| 内存 LRU 缓存 | 是 | 重复查同一词零成本 |
| 全局单在途 + 取消 | 是 | 避免快速切换时堆积请求 |
| `max_tokens` 上限（建议 512） | 是 | 防模型跑偏导致长输出 |
| 每次请求前确认 | 否 | 打扰；改为首次开启时一次性告知 |
| 本地模型 | 否（但零成本可用） | OpenAI 兼容指向 Ollama 即可，不需要额外适配 |
| 批量/预翻译 | 否 | 成本与隐私都不合适 |

### 4.5 是否需要自定义 HTTP header

**需要**，且建议进 MVP（限制 1~3 条）：
- 企业网关/OneAPI/NewAPI 常用额外的鉴权或路由 header；
- 官方兼容层可能需要 `OpenAI-Organization`、`HTTP-Referer` 等；
- 用户自建反代常需要自定义 token。

约束：header 名称与值**禁止写日志**；`Authorization`/`x-api-key` 由协议适配器自己设置，自定义 header 不允许覆盖这两个（或允许覆盖但明确提示）。

---

## 5. 安全与合规

### 5.1 密钥存储

| 方案 | 实现成本 | 安全性 | 便携性 | 结论 |
|---|---|---|---|---|
| 明文存 `ISettingsStore` | 最低 | 差（`settings.json` 可直接读） | 好 | **不提供** |
| DPAPI（CurrentUser）+ Base64 存现有 store | 低 | 中（同用户可解密，跨用户/跨机不可） | **差**：拷走配置后无法解密，需重新输入 | **推荐** |
| Windows 凭据管理器（`CredWrite`/`CredRead`） | 中 | 中（凭据库受系统保护） | 差（同样绑定用户+机器），且卸载不清理会留孤儿条目 | 备选 |
| 不存储，每次输入 | 低 | 最高 | 好 | 体验差，可作为“不保存 Key”选项 |

实现注意（重要）：`System.Security.Cryptography.ProtectedData` 在 .NET 上是**独立 NuGet 包**，不在共享框架里。若要维持“零新增依赖”，有两条路：
1. 用 `Microsoft.Windows.CsWin32`（`Flint3.csproj` 已引用）生成 `CryptProtectData`/`CryptUnprotectData` 绑定，直接 P/Invoke；
2. 改用凭据管理器（CsWin32 生成 `CredWriteW`/`CredReadW`）。

两者都会让加密逻辑绑定 Windows，建议放在 `Flint3`（App 层），不要下沉到 `Flint3.Core` 的跨平台抽象里。

**便携性冲突必须写进 UI**：当前免打包形态下配置在 `%LOCALAPPDATA%\Flint3\settings.json`，用户可能整目录拷走。DPAPI 加密后换机/换用户解密失败，应降级为“密钥需要重新输入”，**不能**因此清空其他设置。

### 5.2 日志与遥测

- 现状（代码事实）：全仓只有 `System.Diagnostics.Trace.WriteLine`，`App.xaml.cs` 未注册任何 `TraceListener`；没有文件日志、没有上报。风险面是调试器、DebugView 以及未来若加文件日志。
- 禁止项（需在实现评审时逐条对照）：
  - API Key（含 `Authorization`/`x-api-key`/`x-goog-api-key` 值）；
  - 完整请求体与响应体；
  - 含 Key 的 URL（Gemini 若用 `?key=` 会直接泄露，故强制 header）；
  - 自定义 header 的原始值。
- 允许项：协议名、HTTP 状态码、错误种类、耗时、模型名、缓存命中与否。
- 现有风险点提醒：`WordQueryService.SafeQueryAsync` 会打印异常对象；若翻译服务照抄这一模式，异常消息里可能带 URL/主机名，**必须只打印异常类型与状态码**。
- 设置页的 `PasswordBox.Password` 只在内存中读取，不得绑定到会参与日志/诊断的属性。

### 5.3 用户数据出站告知

- **默认关闭**（`EnableAiTranslation=false`）。
- 首次打开总开关时弹一次性确认：说明“查询文本与词条释义将发送至 `{用户配置的 host}`，Flint 不中转、不收集”。
- 设置页常驻一行说明，显示当前配置的 host，随配置变更刷新。
- 关闭开关后立即停止任何在途请求。

### 5.4 数据最小化

只发送当前词条/句子与必要上下文；**不发送**生词本、搜索历史、界面语言、设备信息、词典文件路径。

---

## 6. 技术约束

### 6.1 AOT / 免打包 / JSON

- 应用发布形态：免打包自包含（`WindowsPackageType=None`、`SelfContained`、`WindowsAppSDKSelfContained`），`PublishTrimmed=false`，`CsWinRTAotOptimizerEnabled=true`；CLI 另走 NativeAOT。
- `Flint3.Core` / `Flint3.Data` 均标 `IsAotCompatible=true`，且 `Flint3.Data` 关闭了 `JsonSerializerIsReflectionEnabledByDefault`。[FlintJsonContext.cs](file:///d:/workingfold/Flint/Flint3.Core/Serialization/FlintJsonContext.cs) 目前只登记了 `HotkeySettings`、`Dictionary<string,string>`、`WordleWordList`、`WordleState`。
- 结论：**LLM 的请求/响应模型必须走源生成**（新增 `[JsonSerializable]`，建议独立 `LlmJsonContext` 而不是继续膨胀 `FlintJsonContext`）；不能用匿名对象、`JsonSerializer.Serialize(object)`、`JsonNode` 反射式转换到自定义类型。
- `HttpClient` 在 AOT 下无需特殊处理；但**不要**引入 `Microsoft.Extensions.Http`/`IHttpClientFactory`（会带一串依赖），直接用单例 `HttpClient` + `SocketsHttpHandler`。
- `PublishTrimmed=false` 降低了裁剪风险，但 Core 的 AOT 兼容标记仍应保持，否则 CLI 发布会出现 IL 警告。

### 6.2 流式（后续项）在 AOT 下的注意点

- 用 `response.Content.ReadAsStreamAsync(ct)` + `StreamReader.ReadLineAsync(ct)` 手写分帧；处理 `data:` / `event:` 前缀、空行分隔、`[DONE]`、CRLF、可选 BOM。
- 每个 token 直接更新 UI 会打爆 `DispatcherQueue`，必须节流（建议 50~100 ms 合并一次）并做序号校验。
- 流式与“超时”要分开：总超时管整个请求，另设“首字节超时”（建议 10 s），否则慢模型会被整体超时误杀。
- 取消要能真正断流：`CancellationToken` 传到 `ReadLineAsync`，并在取消后 `Dispose` 响应。

### 6.3 依赖与体积

- 预计 **0 个新增 NuGet**（`HttpClient`、`JsonSerializer`、`StreamReader` 均为 BCL）。
- 若采用 DPAPI 包，则 +1 个小包；用 CsWin32 生成绑定则不加包（但增加少量生成代码）。
- 发布体积增量主要是新增代码与 JSON 元数据，**估算 < 100 KB**（未实测，标估算）。

### 6.4 网络配置

| 项 | 建议 | 备注 |
|---|---|---|
| 代理 | 使用系统默认代理（`HttpClient` 默认行为），不单独做代理设置 | 企业代理/环境变量行为 **待实测** |
| 超时 | 总 30 s，首字节 10 s，均可配置 | 与 `HttpClient.Timeout` 解耦，用 `CancellationTokenSource` 组合 |
| 重试 | 仅对 429 / 500 / 502 / 503 / 504 重试 1 次，遵守 `Retry-After`；401/403/400 不重试 | 避免放大限流 |
| 证书 | **不提供“忽略证书错误”开关** | 企业 MITM 需把根证书装进系统存储（待确认企业场景） |
| DNS/连接 | 复用单例 `HttpClient` + `PooledConnectionLifetime=5min` | 防止长连接僵死 |

### 6.5 离线可用性

- 无网络时：按钮可点，请求失败后区块显示“无法连接”，本地查词完全不受影响。
- 不因为翻译功能改变启动流程、不阻塞词典加载、不做“启动即探活”。
- 缓存仅在内存，重启后离线无翻译结果，属预期行为。

---

## 7. 范围切分

| 能力 | MVP | 后续 |
|---|---|---|
| 协议 | OpenAI 兼容 | Anthropic、Gemini |
| 流式 | 否（一次性返回） | 是 |
| 触发 | 手动按钮 + 整句入口 | 可选“自动翻译”开关 |
| 方向 | 自动识别 + 手动切换 | 术语表/风格（学术/口语） |
| 呈现 | 主窗独立区块 | Lite 页内联、并排对照 |
| 缓存 | 内存 LRU | 可选落盘（需加密与清理策略） |
| 密钥 | DPAPI（或凭据管理器） | 多配置档（工作/个人） |
| 输入 | 词条、单句 | 批量、生词本批量、文档翻译 |
| 模型 | 远程 API（含本地 OpenAI 兼容端点） | 内置离线模型 |
| Prompt | 内置固定模板 | 用户自定义模板 |

**MVP 明确不含**：流式渲染、Anthropic/Gemini、离线模型、批量翻译、自动翻译、自定义 prompt 模板、落盘缓存、多语言互译（仅中英）。

---

## 8. 风险与未决问题（需拍板）

| # | 问题 | 选项 | 建议 | 理由 |
|---|---|---|---|---|
| D1 | 触发方式 | 自动 / 手动 | **手动** | 请求次数可控；不把本地查询变成网络依赖 |
| D2 | 结果呈现 | 覆盖原释义 / 追加 / 独立区块 | **独立区块** | 现有 `x:Bind` 为 OneTime，覆盖方案改动大且丢本地释义 |
| D3 | Lite 页是否显示翻译 | 显示 / 仅跳转 | **仅跳转** | Lite 是紧凑弹窗，长文本会破坏布局 |
| D4 | 密钥存储 | 明文 / DPAPI / 凭据管理器 / 不存 | **DPAPI** | 明文不可接受；DPAPI 改动最小；需接受“不随配置迁移” |
| D5 | 默认开关 | 开 / 关 | **关** | 涉及数据出站，默认关是合规底线 |
| D6 | 自定义 header 是否进 MVP | 是 / 否 | **是（≤3 条）** | 企业网关与自建反代必需，成本很低 |
| D7 | 流式是否进 MVP | 是 / 否 | **否** | 非流式已能覆盖词条场景；流式引入节流/取消/断流三处复杂度 |
| D8 | 缓存是否落盘 | 是 / 否 | **否** | 译文属用户数据，落盘需加密与清理策略 |
| D9 | 是否支持 Anthropic/Gemini | 仅 OpenAI / 全部 | **先 OpenAI，预留 protocol** | 一套适配即覆盖绝大多数可实现；配置格式提前留位 |
| D10 | 失败是否自动重试 | 不重试 / 全部 / 仅限流与 5xx | **仅限流与 5xx，1 次** | 避免放大限流与重复计费 |
| D11 | 是否允许无 Key 的本地端点 | 允许 / 禁止 | **允许** | Ollama/LM Studio 可零成本离线；但需校验 host 为 http(s) 且提示“本机地址” |
| D12 | 是否展示 token/费用统计 | 是 / 否 | **展示 tokens，不折算金额** | 单价变动快，折算容易误导 |

其余待确认项：
- 各厂商错误码/限流头在 2026 年的实际行为（**待确认**，需真实 Key 验证）。
- .NET 在 Windows 上对系统代理与 PAC 的实际取用方式（**待确认**，需实测）。
- 企业 MITM 证书场景是否需要额外配置（**待确认**）。
- 中文 token 折算系数（**待确认**，需目标模型 tokenizer 实测）。

---

## 附：调研方法与限制

- 代码结论来自本轮直接阅读：查询链路（`IWordSource`/`WordEntry`/`WordQueryService`/`SearchCoordinator`/`MainViewModel_Flint`）、数据模型（`StarDictWordItem`）、视图（`FlintLitePage`/`FlintFullPage`/`SettingsPage`）、存储（`ISettingsStore`/`ApplicationDataSettingsStore`/`JsonFileSettingsStore`/`SettingsService`）、日志（`App.xaml.cs`）、工程（三个 `.csproj` 与 `FlintJsonContext`）。
- 数据基数复用 `docs/chinese-lookup-decision.md` 中已实测的 `stardict.db` 统计（3,402,564 条、`translation` 含中文 3,376,001 条、95,652,519 B）。
- 未做：真实 API 调用、tokenizer 实测、代理/证书实测、UI 原型、性能与内存测量。
- 本轮未新增依赖、未改产品代码；文档中的默认值（超时、`max_tokens`、缓存容量、阈值）均为建议值，可在实现前调整。
