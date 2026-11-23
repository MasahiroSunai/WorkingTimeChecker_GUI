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

Compress-Archive `
    -Path (
        Join-Path `
            $temporaryZipDirectory `
            "*"
    ) `
    -DestinationPath $runtimeArchivePath `
    -CompressionLevel Optimal

if (-not (Test-Path $runtimeArchivePath)) {
    throw "runtime.zipが作成されませんでした"
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