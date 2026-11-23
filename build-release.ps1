param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$rootDir = $PSScriptRoot

$solutionPath = Join-Path `
    $rootDir `
    "WorkingTimeChecker.slnx"

$projectPath = Join-Path `
    $rootDir `
    "src\WorkingTimeChecker.Cli\WorkingTimeChecker.Cli.csproj"

$pythonScriptsDir = Join-Path `
    $rootDir `
    "resources\python-scripts"

$pythonScriptsZip = Join-Path `
    $rootDir `
    "resources\python-scripts.zip"

$publishDir = Join-Path `
    $rootDir `
    "publish"

Write-Host "===== リリース作成開始 ====="

# -------------------------------------
# 1. Pythonキャッシュ削除
# -------------------------------------
Write-Host ">> Pythonキャッシュを削除します"

Get-ChildItem `
    $pythonScriptsDir `
    -Directory `
    -Filter "__pycache__" `
    -Recurse `
    -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force

Get-ChildItem `
    $pythonScriptsDir `
    -File `
    -Filter "*.pyc" `
    -Recurse `
    -ErrorAction SilentlyContinue |
    Remove-Item -Force

# -------------------------------------
# 2. Pythonスクリプト構文確認
# -------------------------------------
if (-not $SkipTests) {
    Write-Host ">> Pythonスクリプトを確認します"

    python -m compileall -q $pythonScriptsDir

    if ($LASTEXITCODE -ne 0) {
        throw "Pythonスクリプトの構文確認に失敗しました"
    }

    # compileallで作られたキャッシュを再度削除
    Get-ChildItem `
        $pythonScriptsDir `
        -Directory `
        -Filter "__pycache__" `
        -Recurse `
        -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force

    Write-Host "[OK] Python構文確認完了"
}

# -------------------------------------
# 3. PythonスクリプトZIP作成
# -------------------------------------
Write-Host ">> PythonスクリプトZIPを作成します"

if (Test-Path $pythonScriptsZip) {
    Remove-Item $pythonScriptsZip -Force
}

Compress-Archive `
    -Path (Join-Path $pythonScriptsDir "*") `
    -DestinationPath $pythonScriptsZip `
    -CompressionLevel Optimal

Write-Host "[OK] ZIP作成: $pythonScriptsZip"

Write-Host ">> ソリューションをビルドします"

dotnet build `
    $solutionPath `
    --configuration Release

if ($LASTEXITCODE -ne 0) {
    throw "Releaseビルドに失敗しました"
}

# -------------------------------------
# 6. 発行先を初期化
# -------------------------------------
if (Test-Path $publishDir) {
    Remove-Item `
        $publishDir `
        -Recurse `
        -Force
}

New-Item `
    -ItemType Directory `
    -Force `
    -Path $publishDir |
    Out-Null

# -------------------------------------
# 7. 単一exeとして発行
# -------------------------------------
Write-Host ">> 単一exeを発行します"

dotnet publish `
    $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "単一exeの発行に失敗しました"
}

# -------------------------------------
# 8. 外部設定をコピー
# -------------------------------------
Write-Host ">> 設定ファイルをコピーします"

$configSourceDirectory = Join-Path `
    $rootDir `
    "config"

if (-not (Test-Path $configSourceDirectory)) {
    throw "configフォルダーが見つかりません: $configSourceDirectory"
}

Copy-Item `
    -Path $configSourceDirectory `
    -Destination $publishDir `
    -Recurse `
    -Force

# 現段階ではPowerShellも外部へコピー
$publishPowerShellDir = Join-Path `
    $publishDir `
    "resources\powershell"

New-Item `
    -ItemType Directory `
    -Force `
    -Path $publishPowerShellDir |
    Out-Null


# -------------------------------------
# 8.5. 埋め込み済みPowerShellを発行先から削除
# -------------------------------------
$publishedResourcesDirectory = Join-Path `
    $publishDir `
    "resources"

if (Test-Path $publishedResourcesDirectory) {
    Remove-Item `
        $publishedResourcesDirectory `
        -Recurse `
        -Force
}
# -------------------------------------
# 9. 出力確認
# -------------------------------------
$exePath = Join-Path `
    $publishDir `
    "WorkingTimeChecker.Cli.exe"

if (-not (Test-Path $exePath)) {
    throw "発行したexeが見つかりません: $exePath"
}

$exeInfo = Get-Item $exePath

$fileSizeMb = [math]::Round(
    $exeInfo.Length / 1MB,
    2
)

Write-Host ""
Write-Host "===== [OK] リリース作成完了 ====="
Write-Host "実行ファイル: $exePath"
Write-Host "ファイルサイズ: $fileSizeMb MB"
Write-Host ""
Write-Host "発行フォルダー:"
Write-Host $publishDir
