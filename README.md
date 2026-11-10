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

- [x] 基于 C# 重构快捷键唤起窗口，升级到 .NET 10，核心库通过 NativeAOT、UI 使用 R2R（不裁剪）
- [x] 支持导入 mdx 词库：首期只支持**未加密 + zlib 压缩 + 纯文本词条**，加密 / LZO / `.mdd` 图片留作后续
- [x] 单词模式匹配（`?` / `_` 单字母、`*` 任意长度、`^` / `$` 锚定）与内置 Wordle

#### 项目结构

| 目录 | 说明 |
| --- | --- |
| `Flint3/` | WinUI 3 应用（免打包、自包含、x64） |
| `Flint3.Core/` | 纯逻辑：查询抽象、快捷键编码、Wordle 状态机与词表 |
| `Flint3.Data/` | SQLite 数据访问：ECDICT 与 mdx sidecar 索引查询 |
| `Flint3.Dict/` | mdx 格式解析与索引构建 |
| `tests/` | Core / Data / Dict 三套 xunit 用例 |
| `tools/` | 数据准备、mdx 测试数据与 Wordle 词表生成脚本 |
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

早期 UWP 版本（原 `Flint/`、`Flint.Core/`、`Flint.Data/`）已归档到 `legacy/`，不参与解决方案构建，仅保留作行为对照。新代码统一使用 `Flint3*` 项目，`Flint3.Data` 指新的 SQLite 数据层，与旧的 `Flint.Data` 无关。

#### 一睹芳容
![screenshot.png](README/screenshot.png)
