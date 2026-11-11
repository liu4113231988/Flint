#### 词典数据文件

仓库只分发 `stardict.csf`（CSF 只读格式，115.58 MiB）。为绕过 GitHub 单文件 100 MiB 限制，
按 95 MiB 分卷压缩为 `stardict.csf.7z.001`、`stardict.csf.7z.002`。

运行 `tools/prepare-data.ps1` 会自动解出 `stardict.csf` 并校验 SHA256；解压产物本身不入库（见 `.gitignore`），
无需再手动设置"内容"属性——`Flint3.csproj` 会在文件存在时复制到输出目录。

注意：仓库已不再携带 `stardict.db` 分卷。`stardict.csf` 是派生数据，若将来 CSF 格式变更需要重建索引，
必须先从 ECDICT 上游重新获取 `stardict.db` 放入本目录，再执行 `powershell -File tools/prepare-data.ps1 -Rebuild`。
