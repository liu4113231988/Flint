# 构建与运行

## 环境要求

- Windows 10 1809+ / Windows 11，x64
- Visual Studio 2026（含 Windows SDK 10.0.26100，不再需要 C++ 工作负载）
- .NET 10 SDK
- 7-Zip（仅数据准备需要）

## 1. 准备词典数据

`Flint3/Data/stardict.db` 由 ECDICT 转换而来，体积约 812 MB，不入库（见 `.gitignore`）。

```powershell
powershell -ExecutionPolicy Bypass -File tools/prepare-data.ps1
```

脚本会查找 `Flint3/Data/stardict.7z.001` 分卷并用 7-Zip 解压，然后校验词条数与 `sw` 索引；已存在完整数据库时跳过解压，`-Force` 可强制重新解压。

## 2. 构建

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" `
    Flint.sln /t:Build /p:Configuration=Debug /p:Platform=x64
```

- 应用为 **免打包（unpackaged）自包含** 形态：`WindowsPackageType=None`、`SelfContained`、`WindowsAppSDKSelfContained`、`RuntimeIdentifiers=win-x64`
- 不产出 MSIX，也不需要证书签名；`Package.appxmanifest` 保留在仓库但不参与构建
- 解压数据后，`stardict.db` 会随构建复制到输出目录的 `Data/` 下
- `Flint3.Core` 是纯 .NET 库（快捷键钩子、P/Invoke），Win32 API 由 CsWin32 源生成，无 C++/CLI 依赖；修改 `NativeMethods.txt` 后重新构建即可刷新签名

## 3. 运行

```
Flint3\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Flint3.exe
```

整个 `win-x64` 目录可直接拷贝到其他 x64 Windows 机器双击运行（无需安装 .NET 运行时与 Windows App Runtime）。

免打包运行时没有包标识，因此：

- 设置写入 `%LOCALAPPDATA%\Flint3\settings.json`
- 应用数据位于 `%LOCALAPPDATA%\Flint3\FlintData`
- 开机自启（`StartupTask`）不可用

## 4. 测试

```powershell
dotnet test tests\Flint3.Core.Tests\Flint3.Core.Tests.csproj
dotnet test tests\Flint3.Data.Tests\Flint3.Data.Tests.csproj
```

- 核心层用例覆盖快捷键 handle 编码、`HotkeySettings` 校验/显示名，以及固定键盘布局下的按键名解析
- 数据层用例使用临时目录里的小型 fixture 库，不依赖真实 `stardict.db`
- 真实库校验用例默认跳过；`tools/prepare-data.ps1` 会设置 `FLINT_STARDICT_DB` 单独运行它

## 5. 常见问题

| 现象 | 原因与处理 |
| --- | --- |
| `NativeMethods.txt` 新增 API 后编译报找不到类型 | CsWin32 仅在构建时生成代码，先还原再构建（`/restore`）；生成的 `PInvoke` 成员为 `public`（见 `NativeMethods.json`） |
| `APPX0702 payload file ... stardict.db does not exist` | 历史 MSIX 打包错误；免打包形态不再出现，仍建议先跑数据准备脚本 |
| 启动后无窗口 | 查看是否有 `%LOCALAPPDATA%\Flint3` 写入权限；免打包下依赖包标识的 API 已回退到本地文件 |
