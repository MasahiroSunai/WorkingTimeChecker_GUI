        $zipError = $ErrorActionPreference = "Stop"

$rootDir = $PSScriptRoot

$runtimeDirectory = Join-Path `
    $rootDir `
    "resources\python-runtime"

$runtimeZipPath = Join-Path `
    $rootDir `
    "resources\python-runtime.zip"

$requirementsPath = Join-Path `
    $rootDir `
    "resources\requirements-runtime.lock.txt"

Write-Host "===== Python実行環境の作成開始 ====="

# -------------------------------------
# 1. Python情報の確認
# -------------------------------------
$pythonCommand = Get-Command `
    python `
    -ErrorAction Stop

Write-Host ">> 使用するPython:"
Write-Host $pythonCommand.Source

$pythonVersion = & python -c `
    "import sys; print(sys.version)"

if ($LASTEXITCODE -ne 0) {
    throw "Pythonのバージョン確認に失敗しました"
}

Write-Host ">> Pythonバージョン:"
Write-Host $pythonVersion

$pythonBaseDirectory = & python -c `
    "import sys; print(sys.base_prefix)"

if ($LASTEXITCODE -ne 0) {
    throw "Pythonのインストール先取得に失敗しました"
}

$pythonBaseDirectory =
    $pythonBaseDirectory.Trim()

if (-not (Test-Path $pythonBaseDirectory)) {
    throw "Pythonのインストール先が見つかりません: $pythonBaseDirectory"
}

Write-Host ">> Pythonインストール先:"
Write-Host $pythonBaseDirectory

$pythonArchitecture = & python -c `
    "import struct; print(struct.calcsize('P') * 8)"

if ($pythonArchitecture.Trim() -ne "64") {
    throw "64bit版Pythonではありません: ${pythonArchitecture}bit"
}

Write-Host "[OK] 64bit版Pythonを確認しました"

# -------------------------------------
# 2. パッケージバージョンの固定
# -------------------------------------
Write-Host ">> 必要パッケージのバージョンを取得します"

$requiredPackages = @(
    "PyYAML",
    "atlassian-python-api",
    "rocketchat-API",
    "pandas",
    "openpyxl",
    "pywin32",
    "selenium",
    "requests",
    "urllib3"
)

$lockedRequirements =
    New-Object System.Collections.Generic.List[string]

foreach ($packageName in $requiredPackages) {
    $version = & python -c `
        "import importlib.metadata as m; print(m.version('$packageName'))"

    if ($LASTEXITCODE -ne 0) {
        throw "必要なPythonパッケージがありません: $packageName"
    }

    $version = $version.Trim()

    $lockedRequirements.Add(
        "$packageName==$version"
    )

    Write-Host "[OK] $packageName==$version"
}

[System.IO.File]::WriteAllLines(
    $requirementsPath,
    $lockedRequirements,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Host "[OK] バージョン固定ファイル:"
Write-Host $requirementsPath

# -------------------------------------
# 3. 出力先の初期化
# -------------------------------------
if (Test-Path $runtimeDirectory) {
    Write-Host ">> 古いPython実行環境を削除します"

    Remove-Item `
        $runtimeDirectory `
        -Recurse `
        -Force
}

New-Item `
    -ItemType Directory `
    -Force `
    -Path $runtimeDirectory |
    Out-Null

# -------------------------------------
# 4. Python本体のコピー
# -------------------------------------
Write-Host ">> Python本体をコピーします"

$runtimeFiles = @(
    "python.exe",
    "pythonw.exe",
    "python3.dll",
    "LICENSE.txt"
)

foreach ($fileName in $runtimeFiles) {
    $sourcePath = Join-Path `
        $pythonBaseDirectory `
        $fileName

    if (Test-Path $sourcePath) {
        Copy-Item `
            -Path $sourcePath `
            -Destination $runtimeDirectory `
            -Force
    }
}

Get-ChildItem `
    -Path $pythonBaseDirectory `
    -File `
    -Filter "python3*.dll" |
    ForEach-Object {
        Copy-Item `
            -Path $_.FullName `
            -Destination $runtimeDirectory `
            -Force
    }

Get-ChildItem `
    -Path $pythonBaseDirectory `
    -File `
    -Filter "vcruntime*.dll" |
    ForEach-Object {
        Copy-Item `
            -Path $_.FullName `
            -Destination $runtimeDirectory `
            -Force
    }

# -------------------------------------
# 5. DLLsフォルダーのコピー
# -------------------------------------
$sourceDllDirectory = Join-Path `
    $pythonBaseDirectory `
    "DLLs"

if (-not (Test-Path $sourceDllDirectory)) {
    throw "Python DLLsフォルダーが見つかりません: $sourceDllDirectory"
}

Write-Host ">> DLLsフォルダーをコピーします"

Copy-Item `
    -Path $sourceDllDirectory `
    -Destination $runtimeDirectory `
    -Recurse `
    -Force

# -------------------------------------
# 6. 標準ライブラリのコピー
# -------------------------------------
$sourceLibDirectory = Join-Path `
    $pythonBaseDirectory `
    "Lib"

$destinationLibDirectory = Join-Path `
    $runtimeDirectory `
    "Lib"

if (-not (Test-Path $sourceLibDirectory)) {
    throw "Python Libフォルダーが見つかりません: $sourceLibDirectory"
}

Write-Host ">> Python標準ライブラリをコピーします"

New-Item `
    -ItemType Directory `
    -Force `
    -Path $destinationLibDirectory |
    Out-Null

$robocopyArguments = @(
    $sourceLibDirectory,
    $destinationLibDirectory,
    "/E",
    "/XD",
    (Join-Path $sourceLibDirectory "site-packages"),
    "__pycache__",
    "/XF",
    "*.pyc",
    "/NFL",
    "/NDL",
    "/NJH",
    "/NJS",
    "/NP"
)

& robocopy @robocopyArguments |
    Out-Null

$robocopyExitCode = $LASTEXITCODE

if ($robocopyExitCode -ge 8) {
    throw "標準ライブラリのコピーに失敗しました。robocopy終了コード: $robocopyExitCode"
}

# -------------------------------------
# 7. 必要ライブラリの配置
# -------------------------------------
$sitePackagesDirectory = Join-Path `
    $destinationLibDirectory `
    "site-packages"

New-Item `
    -ItemType Directory `
    -Force `
    -Path $sitePackagesDirectory |
    Out-Null

Write-Host ">> 必要なPythonライブラリを配置します"

& python -m pip install `
    --disable-pip-version-check `
    --no-compile `
    --upgrade `
    --target $sitePackagesDirectory `
    --requirement $requirementsPath

if ($LASTEXITCODE -ne 0) {
    throw "Pythonライブラリの配置に失敗しました"
}

# -------------------------------------
# 8. キャッシュの削除
# -------------------------------------
Write-Host ">> Pythonキャッシュを削除します"

Get-ChildItem `
    $runtimeDirectory `
    -Directory `
    -Filter "__pycache__" `
    -Recurse `
    -ErrorAction SilentlyContinue |
    Remove-Item `
        -Recurse `
        -Force

Get-ChildItem `
    $runtimeDirectory `
    -File `
    -Filter "*.pyc" `
    -Recurse `
    -ErrorAction SilentlyContinue |
    Remove-Item `
        -Force

# -------------------------------------
# 9. 内蔵Pythonの起動確認
# -------------------------------------
$embeddedPythonPath = Join-Path `
    $runtimeDirectory `
    "python.exe"

if (-not (Test-Path $embeddedPythonPath)) {
    throw "内蔵Pythonが見つかりません: $embeddedPythonPath"
}

Write-Host ">> 内蔵Pythonを確認します"

& $embeddedPythonPath -c `
    "import sys; print(sys.executable); print(sys.version)"

if ($LASTEXITCODE -ne 0) {
    throw "内蔵Pythonの起動確認に失敗しました"
}

# -------------------------------------
# 10. 必要モジュールの確認
# -------------------------------------
Write-Host ">> 必要モジュールを確認します"

$importTest = "import yaml; import pandas; import openpyxl; import win32com.client; import selenium; import requests; import urllib3; import atlassian; from rocketchat_API.rocketchat import RocketChat; print('All required modules imported successfully.')"

& $embeddedPythonPath `
    -c $importTest

if ($LASTEXITCODE -ne 0) {
    throw "内蔵Pythonのモジュール確認に失敗しました"
}

Write-Host "[OK] 必要モジュールのimport確認完了"

# -------------------------------------
# 11. ZIPの作成
# -------------------------------------
Write-Host ">> Python実行環境をZIP化します"

# Python実行直後はDLLなどが一時的に使用中になることがあるため、
# 少し待ってから最大5回ZIP作成を試します。
Start-Sleep -Seconds 5

Add-Type `
    -AssemblyName System.IO.Compression.FileSystem

$zipCreated =
    $false

$lastZipError =
    $null

for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        if (Test-Path -LiteralPath $runtimeZipPath) {
            Remove-Item `
                -LiteralPath $runtimeZipPath `
                -Force
        }

        Write-Host "ZIP作成試行: $attempt / 5"

        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $runtimeDirectory,
            $runtimeZipPath,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false
        )

        if (-not (Test-Path -LiteralPath $runtimeZipPath)) {
            throw "ZIPファイルが作成されませんでした"
        }

        $zipCreated =
            $true

        break
    }
    catch {
        $lastZipError =
            $_

        Write-Warning (
            "ZIP作成に失敗しました。" +
            "5秒後に再試行します: " +
            $_.Exception.Message
        )

        Remove-Item `
            -LiteralPath $runtimeZipPath `
            -Force `
            -ErrorAction SilentlyContinue

        Start-Sleep -Seconds 5
    }
}

if (-not $zipCreated) {
    $lastErrorMessage =
        "詳細不明"

    if ($null -ne $lastZipError) {
        $lastErrorMessage =
            $lastZipError.Exception.Message
    }

    throw (
        "Python Runtime ZIPを作成できませんでした。" +
        "`n最後のエラー: " +
        $lastErrorMessage
    )
}

$runtimeDirectorySize = (
    Get-ChildItem `
        $runtimeDirectory `
        -File `
        -Recurse |
    Measure-Object `
        -Property Length `
        -Sum
).Sum

$runtimeZipInfo =
    Get-Item $runtimeZipPath

$runtimeDirectorySizeMb =
    [math]::Round(
        $runtimeDirectorySize / 1MB,
        2
    )

$runtimeZipSizeMb =
    [math]::Round(
        $runtimeZipInfo.Length / 1MB,
        2
    )

Write-Host ""
Write-Host "===== [OK] Python実行環境の作成完了 ====="
Write-Host "Python実行環境: $runtimeDirectory"
Write-Host "展開サイズ: $runtimeDirectorySizeMb MB"
Write-Host "ZIPファイル: $runtimeZipPath"
Write-Host "ZIPサイズ: $runtimeZipSizeMb MB"

