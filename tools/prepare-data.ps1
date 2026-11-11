# 准备内置词典：从 csf 分卷解出 stardict.csf（运行时默认使用），可选从源库重建
# 用法：powershell -ExecutionPolicy Bypass -File tools/prepare-data.ps1 [-Force] [-Rebuild] [-SkipVerify]
param(
    [string]$DataDir = (Join-Path $PSScriptRoot "..\Flint3\Data"),
    [switch]$Force,
    [switch]$Rebuild,
    [switch]$SkipVerify
)

$ErrorActionPreference = "Stop"
$DataDir = (Resolve-Path $DataDir).Path
$database = Join-Path $DataDir "stardict.db"
$csf = Join-Path $DataDir "stardict.csf"
$archive = Join-Path $DataDir "stardict.csf.7z.001"
$zhIndex = Join-Path $DataDir "stardict.zhx"
$zhArchive = Join-Path $DataDir "stardict.zhx.7z"
$expectedSha256 = "E3E22A59CA185AA507478C4332A9EBB0132B6024D5053AFFE7CA9ED42FA6D335"
$expectedZhSha256 = "74C40D4D6E95AEE4727EF3A22C8D12C92506022645C6786FA7C2BFEA4F72B47F"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$cliProject = Join-Path $repoRoot "tools\Flint.Cli\Flint.Cli.csproj"

function Find-SevenZip {
    $candidate = @(
        (Join-Path $env:ProgramFiles "7-Zip\7z.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "7-Zip\7z.exe")
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $candidate) {
        $found = Get-Command 7z -ErrorAction SilentlyContinue
        if ($found) { $candidate = $found.Source }
    }

    if (-not $candidate) {
        throw "未找到 7-Zip，请先安装（https://www.7-zip.org/）或把 7z 加入 PATH。"
    }

    return $candidate
}

if ($Rebuild) {
    # 重建需要上游 ECDICT 源库；仓库只分发 csf，不再携带 db 分卷
    if (-not (Test-Path $database)) {
        throw "重建需要源库 $database。仓库不再包含 db 分卷，请先从 ECDICT 上游获取 stardict.db 放入 Flint3/Data。"
    }

    Write-Host "从源库重建 stardict.csf ..."
    dotnet run --project $cliProject -c Release -- $database build $csf
    if ($LASTEXITCODE -ne 0) {
        throw "CSF 构建失败，退出码 $LASTEXITCODE"
    }

    # 中文反查索引是纯派生物，格式变更时与 csf 一起重建
    Write-Host "从源库重建中文反查索引 ..."
    dotnet run --project $cliProject -c Release -- $database zhbuild $zhIndex
    if ($LASTEXITCODE -ne 0) {
        throw "中文反查索引构建失败，退出码 $LASTEXITCODE"
    }
}
else {
    $needExtract = $true
    if ((Test-Path $csf) -and -not $Force) {
        $current = (Get-FileHash $csf -Algorithm SHA256).Hash
        if ($current -eq $expectedSha256) {
            Write-Host "已存在 stardict.csf（$([math]::Round((Get-Item $csf).Length / 1MB, 2)) MB），跳过解压。需要重新解压请加 -Force。"
            $needExtract = $false
        }
        else {
            Write-Host "现有 stardict.csf 与分卷内容不一致，重新解压。"
        }
    }

    if ($needExtract) {
        if (-not (Test-Path $archive)) {
            throw "未找到分卷压缩包：$archive"
        }

        $sevenZip = Find-SevenZip
        Write-Host "使用 7-Zip：$sevenZip"
        Write-Host "解压 $archive 到 $DataDir ..."
        & $sevenZip x $archive "-o$DataDir" -y | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "7-Zip 解压失败，退出码 $LASTEXITCODE"
        }

        $extracted = (Get-FileHash $csf -Algorithm SHA256).Hash
        if ($extracted -ne $expectedSha256) {
            throw "解压得到的 stardict.csf 校验失败：期望 $expectedSha256，实际 $extracted"
        }
        Write-Host "stardict.csf 校验通过（SHA256 $extracted）。"
    }

    $needZhExtract = $true
    if ((Test-Path $zhIndex) -and -not $Force) {
        $current = (Get-FileHash $zhIndex -Algorithm SHA256).Hash
        if ($current -eq $expectedZhSha256) {
            Write-Host "已存在中文反查索引 stardict.zhx（$([math]::Round((Get-Item $zhIndex).Length / 1MB, 2)) MB），跳过解压。需要重新解压请加 -Force。"
            $needZhExtract = $false
        }
        else {
            Write-Host "现有 stardict.zhx 与压缩包内容不一致，重新解压。"
        }
    }

    if ($needZhExtract) {
        if (Test-Path $zhArchive) {
            $sevenZip = Find-SevenZip
            Write-Host "解压中文反查索引 $zhArchive 到 $DataDir ..."
            & $sevenZip x $zhArchive "-o$DataDir" -y | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "7-Zip 解压中文反查索引失败，退出码 $LASTEXITCODE"
            }

            $extracted = (Get-FileHash $zhIndex -Algorithm SHA256).Hash
            if ($extracted -ne $expectedZhSha256) {
                throw "解压得到的 stardict.zhx 校验失败：期望 $expectedZhSha256，实际 $extracted"
            }
            Write-Host "stardict.zhx 校验通过（SHA256 $extracted）。"
        }
        elseif (Test-Path $zhIndex) {
            Write-Host "缺少压缩包 $zhArchive，继续使用现有的 stardict.zhx（中文反查结果可能不准）。"
        }
        else {
            Write-Host "未找到中文反查索引（stardict.zhx 或 stardict.zhx.7z），中文反查会降级为无结果；可用 -Rebuild 从源库生成。"
        }
    }
}

if (-not $SkipVerify) {
    if (Test-Path $database) {
        Write-Host "对拍 CSF 与 SQLite（精确/前缀/模式/tag 计数）..."
        dotnet run --project $cliProject -c Release -- $database verify $csf
        if ($LASTEXITCODE -ne 0) {
            throw "CSF 对拍失败，请勿发布该文件。"
        }

        # 真实库校验用例默认跳过，这里显式指定路径运行
        $env:FLINT_STARDICT_DB = $database
        Push-Location $repoRoot
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
    }
    else {
        Write-Host "未找到 stardict.db，跳过与 SQLite 的逐条对拍（仅完成分卷解压与 SHA256 校验）。"
    }
}

Write-Host "数据准备完成。"
