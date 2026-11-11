# 解压 ECDICT 词典库（stardict.7z.001/002 -> stardict.db），构建并校验 CSF 只读词典
# 用法：powershell -ExecutionPolicy Bypass -File tools/prepare-data.ps1 [-Force] [-SkipVerify]
param(
    [string]$DataDir = (Join-Path $PSScriptRoot "..\Flint3\Data"),
    [switch]$Force,
    [switch]$SkipVerify
)

$ErrorActionPreference = "Stop"
$DataDir = (Resolve-Path $DataDir).Path
$archive = Join-Path $DataDir "stardict.7z.001"
$database = Join-Path $DataDir "stardict.db"
$minSize = 500MB

$needExtract = $true
if ((Test-Path $database) -and -not $Force) {
    $existingSize = (Get-Item $database).Length
    if ($existingSize -ge $minSize) {
        Write-Host "已存在 stardict.db（$([math]::Round($existingSize / 1MB, 1)) MB），跳过解压。需要重新解压请加 -Force。"
        $needExtract = $false
    }
}

if ($needExtract) {
    if (-not (Test-Path $archive)) {
        throw "未找到分卷压缩包：$archive"
    }

    $sevenZip = @(
        (Join-Path $env:ProgramFiles "7-Zip\7z.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "7-Zip\7z.exe")
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $sevenZip) {
        $found = Get-Command 7z -ErrorAction SilentlyContinue
        if ($found) { $sevenZip = $found.Source }
    }

    if (-not $sevenZip) {
        throw "未找到 7-Zip，请先安装（https://www.7-zip.org/）或把 7z 加入 PATH。"
    }

    Write-Host "使用 7-Zip：$sevenZip"
    Write-Host "解压 $archive 到 $DataDir ..."
    & $sevenZip x $archive "-o$DataDir" -y | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip 解压失败，退出码 $LASTEXITCODE"
    }
}

if (-not (Test-Path $database)) {
    throw "未找到 $database"
}

$size = (Get-Item $database).Length
if ($size -lt $minSize) {
    throw "stardict.db 体积异常（$size 字节），可能未完整解压。"
}
Write-Host "stardict.db 就绪：$([math]::Round($size / 1MB, 1)) MB"

# 构建 CSF 只读词典（运行时优先使用，缺失或损坏时自动回退 stardict.db）
$csf = Join-Path $DataDir "stardict.csf"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$cliProject = Join-Path $repoRoot "tools\Flint.Cli\Flint.Cli.csproj"

$needBuild = $true
if ((Test-Path $csf) -and -not $Force) {
    if ((Get-Item $csf).LastWriteTimeUtc -ge (Get-Item $database).LastWriteTimeUtc) {
        $csfSize = [math]::Round((Get-Item $csf).Length / 1MB, 2)
        Write-Host "已存在 stardict.csf（$csfSize MB），跳过构建。需要重建请加 -Force。"
        $needBuild = $false
    }
}

if ($needBuild) {
    Write-Host "构建 stardict.csf ..."
    dotnet run --project $cliProject -c Release -- $database build $csf
    if ($LASTEXITCODE -ne 0) {
        throw "CSF 构建失败，退出码 $LASTEXITCODE"
    }
}

if (-not $SkipVerify) {
    Write-Host "对拍 CSF 与 SQLite（精确/前缀/模式/tag 计数）..."
    dotnet run --project $cliProject -c Release -- $database verify $csf
    if ($LASTEXITCODE -ne 0) {
        throw "CSF 对拍失败，请勿发布该文件。"
    }
}

# 校验词条数与索引（真实库校验用例默认跳过，这里显式指定路径运行）
$env:FLINT_STARDICT_DB = $database
Push-Location (Join-Path $PSScriptRoot "..")
try {
    dotnet test tests\Flint3.Data.Tests\Flint3.Data.Tests.csproj --filter "FullyQualifiedName~RealDatabaseTests" --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "词典库校验失败，请检查 stardict.db 是否完整。"
    }
}
finally {
    Remove-Item Env:\FLINT_STARDICT_DB -ErrorAction SilentlyContinue
    Pop-Location
}

Write-Host "数据准备完成。"
