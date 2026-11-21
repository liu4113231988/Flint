# 计划：产出 LLM 翻译需求文档

## 目标

只新增一个文件：`docs/llm-translation-requirements.md`。不改产品代码、不加依赖、不改 csproj/脚本/README。

## 文档内容（8 节，与用户要求一一对应）

1. **架构接合点**：给代码事实（`IWordSource` 契约、`MatchWord` 每键入触发 + 300 ms 防抖、正则清洗删空格标点、`SafeQueryAsync` 吞异常、`Rank` 参与排序），结论是独立服务 + 独立 UI 区块；详情面板是两份重复的 `Expander` ItemTemplate，`x:Bind Translation` 为 OneTime，流式必须新属性/独立 VM；Lite 窗口不承载长文本；设置页用 `SettingsExpander` 扩“AI 翻译”卡片；`ISettingsStore` 明文，需 DPAPI。
2. **接口归一**：OpenAI 兼容 / Anthropic / Gemini 的路径、鉴权 header、system 位置、必填字段、回复取值、SSE 帧、错误体对比表；MVP 只做 OpenAI 兼容，Anthropic 次之，Gemini 后续。
3. **产品语义与默认值**：手动触发、自动方向、独立区块、内存缓存、失败矩阵、全局单在途 + 切换即取消。
4. **成本**：按实测 27 B/条（≈9 汉字）估 token，给单价区间并标待确认；控制手段为手动 + 缓存 + `max_tokens` 上限；需支持自定义 header。
5. **安全**：DPAPI(CurrentUser) 与便携性冲突；Trace 不得记录 Key/请求体/URL；默认关闭 + 首次告知。
6. **技术约束**：HttpClient 单例、JSON 源生成（仓库已关闭反射）、SSE 手写解析与 UI 节流、代理/超时/重试、离线降级、0 新增 NuGet。
7. **范围**：MVP 含与不含逐条列出（不含流式、多厂商、离线模型、批量）。
8. **待拍板清单**：每条给选项 / 建议 / 理由。

## 验证

- 每条代码结论附 `file:///` 链接，能对上真实文件与行号。
- 无法证实项统一标 **待确认**。
- 完成后 `git status` 只新增该 docs 文件。
