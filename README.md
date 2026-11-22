<p align="center">
    <img src="Flint3/Assets/Logos/flint_logo.png" alt="logo" height="96" width="96"/>
</p>

<h3 align="center">燧石，随地查单词</h3>

在电脑上读英文，遇到一个生词就要切到浏览器、输进搜索框，再从结果里找释义。查完回到原文，有时还得重新找读到哪一行。

燧石是一款 Windows 桌面词典。打开窗口，输到一半就能看到候选，查到的释义直接显示，无需按回车。也可以在网页或文档里选中文字，用快捷键在原处弹出释义。准备好基于 ECDICT 的词库后，日常查词可以离线完成。[ECDICT](https://github.com/skywind3000/ECDICT)

#### 下载与使用

从 GitHub 下载 Windows x64 自包含便携版，完整解压后双击 `Flint3.exe`。[下载最新版本](https://github.com/liu4113231988/Flint/releases/latest)

便携包需要单独准备词库。将 `stardict.csf` 放入程序旁的 `Data/` 目录即可离线查词。中文反查还需要同目录下的 `stardict.zhx`。已有 MDX 词库也可以在设置中直接导入。[词典数据准备方法](docs/build.md#1-准备词典数据)

#### 主要功能

- **离线查词**。支持单词、短语、前缀补全和中文反查。手输和取词优先显示原文精确释义，词条可查看音标、词形变化与中英文释义，也可用系统语音朗读。
- **快捷键、划词与屏幕取词**。可用快捷键唤出小窗，也可开启划词图标。遇到无法选中文字的画面，可以用屏幕 OCR 识别。[划词与屏幕取词说明](docs/selection-icon.md)
- **AI 翻译**。配置兼容的模型接口后，可翻译词条、整句和取词原文。取词原文在点击「翻译原文」后发送到所配置的接口。[AI 翻译说明](docs/llm-translation-requirements.md)
- **MDX 词库**。支持导入兼容的纯文本词库并调整顺序。不同词库收录同一词时，可以展开释义查看各自来源。
- **生词本与词汇练习**。可在本机收藏词条、添加备注和颜色，浏览内置考试词表。支持通配符查词，内置 Wordle。

如果你主要在 Windows 上读英文，可以用燧石承担日常查词和本地生词整理。[与有道词典桌面版的功能对照](docs/youdao-alternative-review.md)

#### 已知限制

- MDX 导入适用于未加密、使用 zlib 或未压缩、编码为 UTF-8 或 UTF-16 的受支持词库。MDX 1.2、LZO 和 `.mdd` 图片与发音资源暂未支持。
- OCR 依赖相应的 Windows 语言包。AI 翻译依赖配置的模型接口。
- 整应用 NativeAOT（L2）未做；UI 仍依赖 WinUI 3 的运行时，采用自包含发布并保留完整运行时
- 没有应用内语言切换，界面语言跟随系统，且只保留中文与英文资源
- 开机自启只支持当前用户（HKCU），不写入全局启动项
- 中文反查只做精确 + 前缀：不做拼音、简繁转换与分词，子串匹配（如输入"果"查"苹果"）留待后续

#### 项目结构

| 目录 | 说明 |
| --- | --- |
| `Flint3/` | WinUI 3 应用（免打包、自包含、x64） |
| `Flint3.Core/` | 纯逻辑：查询抽象、快捷键编码、Wordle 状态机与词表 |
| `Flint3.Data/` | 词典数据访问：CSF 只读格式（缺失时回退 SQLite）、中文反查旁挂索引与 mdx sidecar 索引查询 |
| `Flint3.Dict/` | mdx 格式解析与索引构建 |
| `tests/` | Core / Data / Dict 三套 xunit 用例 |
| `tools/` | 数据准备、CSF 构建/校验、中文反查索引构建/基准 CLI 与 Wordle 词表生成脚本 |
| `docs/` | [构建说明](docs/build.md)、[AOT 报告](docs/aot-report.md)、[重构计划](docs/refactor-plan.md) |

#### 构建、测试与数据准备

见 [docs/build.md](docs/build.md)。仓库分发的词典数据为 `stardict.csf` 分卷（约 121 MB）与中文反查索引 `stardict.zhx.7z`（31.83 MB，约增加 26% 的 clone 体积），执行 `prepare-data.ps1` 后会解压到 `Flint3/Data` 并校验 SHA256。在 Visual Studio Developer PowerShell 中运行：

```powershell
powershell -ExecutionPolicy Bypass -File tools/prepare-data.ps1
msbuild Flint.sln /t:Build /p:Configuration=Debug /p:Platform=x64
dotnet test tests\Flint3.Core.Tests\Flint3.Core.Tests.csproj
dotnet test tests\Flint3.Data.Tests\Flint3.Data.Tests.csproj
dotnet test tests\Flint3.Dict.Tests\Flint3.Dict.Tests.csproj
```

#### 历史实现

早期 UWP 版本（原 `Flint/`、`Flint.Core/`、`Flint.Data/`）已归档到 `legacy/`，不参与解决方案构建，仅保留作行为对照。新代码统一使用 `Flint3*` 项目，`Flint3.Data` 指新的数据层（CSF 只读格式优先、SQLite 回退），与旧的 `Flint.Data` 无关。

#### 一睹芳容
![screenshot.png](README/screenshot.png)
