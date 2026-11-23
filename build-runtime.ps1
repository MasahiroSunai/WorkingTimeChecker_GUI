[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern(
        '^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$')]
    [string]$RuntimeVersion = "1.0.0"
)

$ErrorActionPreference = "Stop"

$root =
    Split-Path `
        -Parent `
        $MyInvocation.MyCommand.Path

$cliProjectPath = Join-Path `
    $root `
    "src\WorkingTimeChecker.Cli\WorkingTimeChecker.Cli.csproj"

$guiProjectPath = Join-Path `
    $root `
    "src\WorkingTimeChecker.Gui\WorkingTimeChecker.Gui.csproj"

$payloadDirectory = Join-Path `
    $root `
    "src\WorkingTimeChecker.Gui\Payload"

$temporaryPublishDirectory = Join-Path `
    $root `
    "artifacts\runtime-publish"

$temporaryZipDirectory = Join-Path `
    $root `
    "artifacts\runtime-zip"

$runtimeArchivePath = Join-Path `
    $payloadDirectory `
    "runtime.zip"

$runtimeManifestPath = Join-Path `
    $payloadDirectory `
    "runtime-manifest.json"

$cliExecutablePath = Join-Path `
    $temporaryPublishDirectory `
    "WorkingTimeChecker.Cli.exe"

$utf8NoBom =
    [System.Text.UTF8Encoding]::new($false)

Write-Host "===== Runtime作成開始 ====="
Write-Host "RuntimeVersion: $RuntimeVersion"

foreach ($directory in @(
    $temporaryPublishDirectory
    $temporaryZipDirectory
)) {
    if (Test-Path $directory) {
        Remove-Item `
            -Path $directory `
            -Recurse `
            -Force
    }

    New-Item `
        -Path $directory `
        -ItemType Directory `
        -Force |
    Out-Null
}

New-Item `
    -Path $payloadDirectory `
    -ItemType Directory `
    -Force |
Out-Null

# =====================================
# PythonスクリプトZIPを作成します
# =====================================
$pythonScriptsDirectory = Join-Path `
    $root `
    "resources\python-scripts"

$pythonScriptsZipPath = Join-Path `
    $root `
    "resources\python-scripts.zip"

if (-not (Test-Path -LiteralPath $pythonScriptsDirectory)) {
    throw (
        "Pythonスクリプトフォルダーがありません: " +
        $pythonScriptsDirectory
    )
}

Write-Host ">> Pythonスクリプトを構文確認します"

& python `
    -m `
    compileall `
    -q `
    $pythonScriptsDirectory

if ($LASTEXITCODE -ne 0) {
    throw (
        "Pythonスクリプトの構文確認に失敗しました。" +
        "終了コード: $LASTEXITCODE"
    )
}

# compileallが作成したキャッシュをZIPへ含めません。
Get-ChildItem `
    -LiteralPath $pythonScriptsDirectory `
    -Directory `
    -Filter "__pycache__" `
    -Recurse `
    -ErrorAction SilentlyContinue |
Remove-Item `
    -Recurse `
    -Force

Get-ChildItem `
    -LiteralPath $pythonScriptsDirectory `
    -File `
    -Filter "*.pyc" `
    -Recurse `
    -ErrorAction SilentlyContinue |
Remove-Item `
    -Force

Write-Host "[OK] Pythonスクリプトの構文確認完了"
Write-Host ">> PythonスクリプトZIPを作成します"

Remove-Item `
    -LiteralPath $pythonScriptsZipPath `
    -Force `
    -ErrorAction SilentlyContinue

Add-Type `
    -AssemblyName System.IO.Compression.FileSystem

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $pythonScriptsDirectory,
    $pythonScriptsZipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

if (-not (Test-Path -LiteralPath $pythonScriptsZipPath)) {
    throw (
        "PythonスクリプトZIPを作成できませんでした: " +
        $pythonScriptsZipPath
    )
}

$pythonScriptsZipInfo =
    Get-Item `
        -LiteralPath $pythonScriptsZipPath

if ($pythonScriptsZipInfo.Length -eq 0) {
    throw "PythonスクリプトZIPが空です"
}

Write-Host "[OK] PythonスクリプトZIPを作成しました"
Write-Host "ZIP: $pythonScriptsZipPath"
Write-Host (
    "ZIP Size: " +
    [System.Math]::Round(
        $pythonScriptsZipInfo.Length / 1KB,
        2
    ) +
    " KB"
)
Write-Host ">> CLIを単一exeとして発行します"

dotnet publish `
    $cliProjectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $temporaryPublishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw (
        "CLIの発行に失敗しました。" +
        "終了コード: $LASTEXITCODE"
    )
}

if (-not (Test-Path $cliExecutablePath)) {
    throw "CLIが作成されませんでした: $cliExecutablePath"
}

Write-Host ">> 発行したCLIのRuntime確認を実行します"

$checkLogDirectory = Join-Path `
    $temporaryPublishDirectory `
    "runtime-check-logs"

& $cliExecutablePath `
    --runtime-check `
    --log-directory $checkLogDirectory

if ($LASTEXITCODE -ne 0) {
    throw (
        "発行したCLIのRuntime確認に失敗しました。" +
        "終了コード: $LASTEXITCODE"
    )
}

Copy-Item `
    -Path $cliExecutablePath `
    -Destination (
        Join-Path `
            $temporaryZipDirectory `
            "WorkingTimeChecker.Cli.exe"
    ) `
    -Force

if (Test-Path $runtimeArchivePath) {
    Remove-Item `
        -Path $runtimeArchivePath `
        -Force
}

Write-Host ">> runtime.zipを作成します"

# Runtime確認で起動したCLIや、ウイルス対策ソフトによる
# 一時的なファイルロックが解除されるまで待ちます。
Get-Process `
    -Name "WorkingTimeChecker.Cli" `
    -ErrorAction SilentlyContinue |
Stop-Process `
    -Force `
    -ErrorAction SilentlyContinue

Start-Sleep -Seconds 3

Add-Type `
    -AssemblyName System.IO.Compression.FileSystem

$runtimeArchiveCreated =
    $false

$lastRuntimeArchiveError =
    $null

for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        Remove-Item `
            -LiteralPath $runtimeArchivePath `
            -Force `
            -ErrorAction SilentlyContinue

        Write-Host "runtime.zip作成試行: $attempt / 5"

        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $temporaryZipDirectory,
            $runtimeArchivePath,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false
        )

        if (-not (Test-Path -LiteralPath $runtimeArchivePath)) {
            throw "runtime.zipが作成されませんでした"
        }

        $runtimeArchiveInfo =
            Get-Item `
                -LiteralPath $runtimeArchivePath

        if ($runtimeArchiveInfo.Length -eq 0) {
            throw "作成されたruntime.zipが空です"
        }

        $runtimeArchiveCreated =
            $true

        break
    }
    catch {
        $lastRuntimeArchiveError =
            $_

        Write-Warning (
            "runtime.zipの作成に失敗しました。" +
            "5秒後に再試行します: " +
            $_.Exception.Message
        )

        Remove-Item `
            -LiteralPath $runtimeArchivePath `
            -Force `
            -ErrorAction SilentlyContinue

        Get-Process `
            -Name "WorkingTimeChecker.Cli" `
            -ErrorAction SilentlyContinue |
        Stop-Process `
            -Force `
            -ErrorAction SilentlyContinue

        Start-Sleep -Seconds 5
    }
}

if (-not $runtimeArchiveCreated) {
    $lastRuntimeArchiveErrorMessage =
        "詳細不明"

    if ($null -ne $lastRuntimeArchiveError) {
        $lastRuntimeArchiveErrorMessage =
            $lastRuntimeArchiveError.Exception.Message
    }

    throw (
        "runtime.zipを作成できませんでした。" +
        "`n最後のエラー: " +
        $lastRuntimeArchiveErrorMessage
    )
}

$archiveHash =
    (
        Get-FileHash `
            -Path $runtimeArchivePath `
            -Algorithm SHA256
    ).Hash.ToUpperInvariant()

$manifest = [ordered]@{
    schemaVersion   = 1
    runtimeVersion  = $RuntimeVersion
    entryPoint      = "WorkingTimeChecker.Cli.exe"
    archiveFileName = "runtime.zip"
    archiveSha256   = $archiveHash
    createdAtUtc    = (
        [DateTime]::UtcNow
    ).ToString("O")
}

$manifestJson =
    $manifest |
    ConvertTo-Json `
        -Depth 4

[System.IO.File]::WriteAllText(
    $runtimeManifestPath,
    $manifestJson,
    $utf8NoBom
)

Write-Host "[OK] runtime.zipを作成しました"
Write-Host "[OK] runtime-manifest.jsonを作成しました"
Write-Host "SHA-256: $archiveHash"

Write-Host ">> GUIプロジェクトをビルドします"

dotnet build `
    $guiProjectPath `
    --configuration Debug

if ($LASTEXITCODE -ne 0) {
    throw (
        "GUIのビルドに失敗しました。" +
        "終了コード: $LASTEXITCODE"
    )
}

Write-Host ""
Write-Host "===== [OK] Runtime作成完了 ====="
Write-Host "Archive: $runtimeArchivePath"
Write-Host "Manifest: $runtimeManifestPath"
