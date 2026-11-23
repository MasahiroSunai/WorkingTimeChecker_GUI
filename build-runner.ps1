[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$root =
    Split-Path `
        -Parent `
        $MyInvocation.MyCommand.Path

$runnerProjectPath = Join-Path `
    $root `
    "src\WorkingTimeChecker.Runner\WorkingTimeChecker.Runner.csproj"

$publishDirectory = Join-Path `
    $root `
    "artifacts\runner-publish"

$publishedRunnerPath = Join-Path `
    $publishDirectory `
    "WorkingTimeChecker.Runner.exe"

$applicationDataDirectory = Join-Path `
    $env:LOCALAPPDATA `
    "PERSOL_CROSS_TECHNOLOGY\WorkingTimeChecker"

$runnerDirectory = Join-Path `
    $applicationDataDirectory `
    "Runner"

$installedRunnerPath = Join-Path `
    $runnerDirectory `
    "WorkingTimeChecker.Runner.exe"

$temporaryRunnerPath =
    $installedRunnerPath + ".new"

$backupRunnerPath =
    $installedRunnerPath + ".previous"

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item `
        -LiteralPath $publishDirectory `
        -Recurse `
        -Force
}

New-Item `
    -Path $publishDirectory `
    -ItemType Directory `
    -Force |
Out-Null

Write-Host "===== Runner発行開始 ====="

dotnet publish `
    $runnerProjectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw (
        "Runnerの発行に失敗しました。" +
        "終了コード: $LASTEXITCODE"
    )
}

if (-not (Test-Path -LiteralPath $publishedRunnerPath)) {
    throw "発行したRunnerが見つかりません"
}

Write-Host ">> 発行したRunnerを確認します"

& $publishedRunnerPath `
    --runner-check

if ($LASTEXITCODE -ne 0) {
    throw (
        "発行したRunnerの確認に失敗しました。" +
        "終了コード: $LASTEXITCODE"
    )
}

New-Item `
    -Path $runnerDirectory `
    -ItemType Directory `
    -Force |
Out-Null

if (Test-Path -LiteralPath $temporaryRunnerPath) {
    Remove-Item `
        -LiteralPath $temporaryRunnerPath `
        -Force
}

Copy-Item `
    -LiteralPath $publishedRunnerPath `
    -Destination $temporaryRunnerPath `
    -Force

if (Test-Path -LiteralPath $backupRunnerPath) {
    Remove-Item `
        -LiteralPath $backupRunnerPath `
        -Force
}

if (Test-Path -LiteralPath $installedRunnerPath) {
    Move-Item `
        -LiteralPath $installedRunnerPath `
        -Destination $backupRunnerPath `
        -Force
}

try {
    Move-Item `
        -LiteralPath $temporaryRunnerPath `
        -Destination $installedRunnerPath `
        -Force

    & $installedRunnerPath `
        --runner-check

    if ($LASTEXITCODE -ne 0) {
        throw (
            "配置したRunnerの確認に失敗しました。" +
            "終了コード: $LASTEXITCODE"
        )
    }
}
catch {
    if (Test-Path -LiteralPath $installedRunnerPath) {
        Remove-Item `
            -LiteralPath $installedRunnerPath `
            -Force `
            -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $backupRunnerPath) {
        Move-Item `
            -LiteralPath $backupRunnerPath `
            -Destination $installedRunnerPath `
            -Force
    }

    throw
}

Write-Host ""
Write-Host "===== [OK] Runner配置完了 ====="
Write-Host "Runner: $installedRunnerPath"