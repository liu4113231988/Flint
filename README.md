<p align="center">
    <img src="Flint3/Assets/Logos/flint_logo.png" alt="logo" height="96" width="96"/>
</p>

<h3 align="center">燧石，随地查单词</h3>

经常在电脑上查阅英文内容的朋友，你是否和我有一样的困扰：
每当遇到一个陌生的单词，就要打开浏览器 - 在搜索引擎中输入单词 - 然后跳转到结果页面 - 最后从众多结果中找到想要的答案。这繁琐的搜索过程让人身心俱疲，更别提这期间出现的转圈圈和大量广告了。

现在，借助这款词典应用"燧石"，整个流程就简单了很多，你只需要打开"燧石"，然后输入单词，甚至都不需要再按回车键，答案就跃然屏幕之上。而且"燧石"使用的是源于 [ECDICT](https://github.com/skywind3000/ECDICT) 的离线词典数据库，因此也无需与网络打交道（虽然这让 APP 的存储占用大了不少，但这一切都是值得的）。

#### 点击下方按钮即可下载

<a href="https://apps.microsoft.com/detail/9p8735fcs5s9?mode=full">
	<img src="https://get.microsoft.com/images/zh-cn%20dark.svg" width="200"/>
</a>

#### 功能状态

- [x] 快捷键唤起窗口改为纯 C# 实现（不再依赖 C++/WinRT Interop），升级到 .NET 10
- [x] AOT L1：`Flint3.Core`、`Flint3.Data` 可 NativeAOT 发布，UI 使用 R2R + CsWinRT AOT（不裁剪）；整应用 NativeAOT（L2）不在本期范围
- [x] 支持导入 mdx 词库：未加密 + zlib/无压缩 + UTF-8/UTF-16 纯文本词条
- [x] 单词模式匹配（`?` / `_` 单字母、`*` 任意长度、`^` / `$` 锚定）与内置 Wordle
- [x] 汉英反查：输入中文精确或前缀匹配英文词，旁挂索引缺失/损坏时自动降级且不影响英文查词
- [x] 免打包下的开机自启（写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`）与界面版本号

#### 已知限制

- mdx 加密、LZO 压缩、mdx 1.2 版本以及 GBK 等非 UTF-8/UTF-16 编码暂不支持，导入时会给出明确错误
- `.mdd` 图片 / 发音等资源尚未支持，导入的 mdx 只提供词条文本
- 整应用 NativeAOT（L2）未做；UI 仍依赖 WinUI 3 的运行时，因此保留 R2R 且不裁剪
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

见 [docs/build.md](docs/build.md)。简要命令：

```powershell
powershell -ExecutionPolicy Bypass -File tools/prepare-data.ps1
dotnet build Flint.sln -c Debug -p:Platform=x64
dotnet test tests\Flint3.Core.Tests\Flint3.Core.Tests.csproj
dotnet test tests\Flint3.Data.Tests\Flint3.Data.Tests.csproj
dotnet test tests\Flint3.Dict.Tests\Flint3.Dict.Tests.csproj
```

#### 历史实现

早期 UWP 版本（原 `Flint/`、`Flint.Core/`、`Flint.Data/`）已归档到 `legacy/`，不参与解决方案构建，仅保留作行为对照。新代码统一使用 `Flint3*` 项目，`Flint3.Data` 指新的数据层（CSF 只读格式优先、SQLite 回退），与旧的 `Flint.Data` 无关。

#### 一睹芳容
![screenshot.png](README/screenshot.png)
