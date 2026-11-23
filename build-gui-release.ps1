[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern(
        '^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$')]
    [string]$GuiVersion = "1.0.1",

    [Parameter()]
    [ValidatePattern(
        '^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$')]
    [string]$RuntimeVersion = "1.0.1",

    [Parameter()]
    [ValidateSet(
        "win-x64",
        "win-arm64"
    )]
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root =
    Split-Path `
        -Parent `
        $MyInvocation.MyCommand.Path

$guiProjectPath = Join-Path `
    $root `
    "src\WorkingTimeChecker.Gui\WorkingTimeChecker.Gui.csproj"

$runnerProjectPath = Join-Path `
    $root `
    "src\WorkingTimeChecker.Runner\WorkingTimeChecker.Runner.csproj"

$buildRuntimePath = Join-Path `
    $root `
    "build-runtime.ps1"

$payloadSourceDirectory = Join-Path `
    $root `
    "src\WorkingTimeChecker.Gui\Payload"

$defaultsSourceDirectory = Join-Path `
    $root `
    "src\WorkingTimeChecker.Gui\Defaults"

$artifactsDirectory = Join-Path `
    $root `
    "artifacts"

$guiPublishDirectory = Join-Path `
    $artifactsDirectory `
    "gui-release-publish"

$runnerPublishDirectory = Join-Path `
    $artifactsDirectory `
    "runner-release-publish"

$releaseName =
    "WorkingTimeChecker_" +
    $GuiVersion +
    "_" +
    $RuntimeIdentifier

$releaseDirectory = Join-Path `
    $artifactsDirectory `
    $releaseName

$releaseZipPath =
    $releaseDirectory + ".zip"

$releaseInfoPath = Join-Path `
    $releaseDirectory `
    "release-info.json"

$checksumPath = Join-Path `
    $releaseDirectory `
    "SHA256SUMS.txt"

$utf8NoBom =
    [System.Text.UTF8Encoding]::new($false)

function Assert-LastExitCode {
    param(
        [Parameter(Mandatory)]
        [string]$OperationName
    )

    if ($LASTEXITCODE -ne 0) {
        throw (
            $OperationName +
            "に失敗しました。" +
            "終了コード: $LASTEXITCODE"
        )
    }
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$DisplayName
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw (
            $DisplayName +
            "が見つかりません: " +
            $Path
        )
    }

    $fileInfo =
        Get-Item `
            -LiteralPath $Path

    if ($fileInfo.Length -eq 0) {
        throw (
            $DisplayName +
            "が空です: " +
            $Path
        )
    }
}

function Remove-DirectoryIfExists {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item `
            -LiteralPath $Path `
            -Recurse `
            -Force
    }
}

function Get-RelativeReleasePath {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath
    )

    $releaseRoot =
        [System.IO.Path]::GetFullPath(
            $releaseDirectory
        ).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar
        )

    $fullFilePath =
        [System.IO.Path]::GetFullPath(
            $FilePath
        )

    $releaseRootPrefix =
        $releaseRoot +
        [System.IO.Path]::DirectorySeparatorChar

    if (-not $fullFilePath.StartsWith(
            $releaseRootPrefix,
            [System.StringComparison]::OrdinalIgnoreCase
        )) {
        throw (
            "Releaseフォルダー外のファイルです: " +
            $fullFilePath
        )
    }

    return $fullFilePath.Substring(
        $releaseRootPrefix.Length
    ).Replace(
        "\",
        "/"
    )
}

Write-Host "=========================================="
Write-Host " WorkingTimeChecker Release作成"
Write-Host "=========================================="
Write-Host "GUI Version: $GuiVersion"
Write-Host "Runtime Version: $RuntimeVersion"
Write-Host "Runtime Identifier: $RuntimeIdentifier"
Write-Host ""

# =====================================
# 1. 必要なファイルを確認
# =====================================
foreach ($requiredPath in @(
    $guiProjectPath
    $runnerProjectPath
    $buildRuntimePath
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "必要なファイルが見つかりません: $requiredPath"
    }
}

# =====================================
# 2. 以前の生成物を削除
# =====================================
Write-Host ">> 以前のRelease生成物を削除します"

foreach ($directory in @(
    $guiPublishDirectory
    $runnerPublishDirectory
    $releaseDirectory
)) {
    Remove-DirectoryIfExists `
        -Path $directory
}

if (Test-Path -LiteralPath $releaseZipPath) {
    Remove-Item `
        -LiteralPath $releaseZipPath `
        -Force
}

New-Item `
    -Path $artifactsDirectory `
    -ItemType Directory `
    -Force |
Out-Null

Write-Host "[OK] 生成先を初期化しました"

# =====================================
# 3. Runtimeを作成
# =====================================
Write-Host ""
Write-Host ">> Runtime $RuntimeVersion を作成します"

& $buildRuntimePath `
    -RuntimeVersion $RuntimeVersion

if (-not $?) {
    throw "build-runtime.ps1の実行に失敗しました"
}

$runtimeArchivePath = Join-Path `
    $payloadSourceDirectory `
    "runtime.zip"

$runtimeManifestPath = Join-Path `
    $payloadSourceDirectory `
    "runtime-manifest.json"

Assert-FileExists `
    -Path $runtimeArchivePath `
    -DisplayName "runtime.zip"

Assert-FileExists `
    -Path $runtimeManifestPath `
    -DisplayName "runtime-manifest.json"

$runtimeManifest =
    Get-Content `
        -LiteralPath $runtimeManifestPath `
        -Raw `
        -Encoding UTF8 |
    ConvertFrom-Json

if (
    $runtimeManifest.runtimeVersion -ne
    $RuntimeVersion
) {
    throw (
        "Runtimeバージョンが一致しません。" +
        "`n期待値: $RuntimeVersion" +
        "`n実際値: $($runtimeManifest.runtimeVersion)"
    )
}

$actualRuntimeHash =
    (
        Get-FileHash `
            -LiteralPath $runtimeArchivePath `
            -Algorithm SHA256
    ).Hash.ToUpperInvariant()

if (
    $runtimeManifest.archiveSha256 -ne
    $actualRuntimeHash
) {
    throw (
        "runtime.zipのSHA-256が一致しません。" +
        "`n期待値: $($runtimeManifest.archiveSha256)" +
        "`n実際値: $actualRuntimeHash"
    )
}

Write-Host "[OK] Runtimeを作成・検証しました"
Write-Host "SHA-256: $actualRuntimeHash"

# =====================================
# 4. GUIをRelease発行
# =====================================
Write-Host ""
Write-Host ">> GUIをRelease発行します"

dotnet publish `
    $guiProjectPath `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $guiPublishDirectory `
    -p:Version=$GuiVersion `
    -p:AssemblyVersion="$GuiVersion.0" `
    -p:FileVersion="$GuiVersion.0" `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

Assert-LastExitCode `
    -OperationName "GUIのRelease発行"

$guiExePath = Join-Path `
    $guiPublishDirectory `
    "WorkingTimeChecker.Gui.exe"

$publishedRuntimeArchivePath = Join-Path `
    $guiPublishDirectory `
    "Payload\runtime.zip"

$publishedRuntimeManifestPath = Join-Path `
    $guiPublishDirectory `
    "Payload\runtime-manifest.json"

$publishedDefaultConfigPath = Join-Path `
    $guiPublishDirectory `
    "Defaults\config.example.yaml"

Assert-FileExists `
    -Path $guiExePath `
    -DisplayName "WorkingTimeChecker.Gui.exe"

Assert-FileExists `
    -Path $publishedRuntimeArchivePath `
    -DisplayName "発行先のruntime.zip"

Assert-FileExists `
    -Path $publishedRuntimeManifestPath `
    -DisplayName "発行先のruntime-manifest.json"

Assert-FileExists `
    -Path $publishedDefaultConfigPath `
    -DisplayName "Defaults\config.example.yaml"

Write-Host "[OK] GUIをRelease発行しました"

# =====================================
# 5. RunnerをRelease発行
# =====================================
Write-Host ""
Write-Host ">> RunnerをRelease発行します"

dotnet publish `
    $runnerProjectPath `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $runnerPublishDirectory `
    -p:Version=$GuiVersion `
    -p:AssemblyVersion="$GuiVersion.0" `
    -p:FileVersion="$GuiVersion.0" `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

Assert-LastExitCode `
    -OperationName "RunnerのRelease発行"

$publishedRunnerPath = Join-Path `
    $runnerPublishDirectory `
    "WorkingTimeChecker.Runner.exe"

Assert-FileExists `
    -Path $publishedRunnerPath `
    -DisplayName "WorkingTimeChecker.Runner.exe"

Write-Host ">> Runnerの動作確認を行います"

& $publishedRunnerPath `
    --runner-check

Assert-LastExitCode `
    -OperationName "Runnerの動作確認"

Write-Host "[OK] RunnerをRelease発行しました"

# =====================================
# 6. 配布フォルダーを作成
# =====================================
Write-Host ""
Write-Host ">> 配布フォルダーを作成します"

New-Item `
    -Path $releaseDirectory `
    -ItemType Directory `
    -Force |
Out-Null

$releaseGuiDirectory = Join-Path `
    $releaseDirectory `
    "Application"

$releaseRunnerDirectory = Join-Path `
    $releaseDirectory `
    "Runner"

New-Item `
    -Path $releaseGuiDirectory `
    -ItemType Directory `
    -Force |
Out-Null

New-Item `
    -Path $releaseRunnerDirectory `
    -ItemType Directory `
    -Force |
Out-Null

Copy-Item `
    -Path (
        Join-Path `
            $guiPublishDirectory `
            "*"
    ) `
    -Destination $releaseGuiDirectory `
    -Recurse `
    -Force

Copy-Item `
    -LiteralPath $publishedRunnerPath `
    -Destination (
        Join-Path `
            $releaseRunnerDirectory `
            "WorkingTimeChecker.Runner.exe"
    ) `
    -Force

Write-Host "[OK] 配布フォルダーを作成しました"

$installScriptSourcePath = Join-Path `
    $root `
    "packaging\Install.ps1"

$uninstallScriptSourcePath = Join-Path `
    $root `
    "packaging\Uninstall.ps1"

foreach ($requiredScriptPath in @(
    $installScriptSourcePath
    $uninstallScriptSourcePath
)) {
    if (-not (Test-Path -LiteralPath $requiredScriptPath)) {
        throw (
            "インストール用スクリプトが見つかりません: " +
            $requiredScriptPath
        )
    }
}

Copy-Item `
    -LiteralPath $installScriptSourcePath `
    -Destination (
        Join-Path `
            $releaseDirectory `
            "Install.ps1"
    ) `
    -Force

Copy-Item `
    -LiteralPath $uninstallScriptSourcePath `
    -Destination (
        Join-Path `
            $releaseDirectory `
            "Uninstall.ps1"
    ) `
    -Force

Write-Host "[OK] Install.ps1を配布物へ追加しました"
Write-Host "[OK] Uninstall.ps1を配布物へ追加しました"

# =====================================
# 7. 秘密情報の混入を検査
# =====================================
Write-Host ""
Write-Host ">> 秘密情報の混入を検査します"

$forbiddenFilePatterns = @(
    ".env"
    ".env.*"
    "*.pfx"
    "*.p12"
    "*.pem"
    "*.key"
)

$forbiddenFiles =
    @(
        foreach ($pattern in $forbiddenFilePatterns) {
            Get-ChildItem `
                -LiteralPath $releaseDirectory `
                -File `
                -Recurse `
                -Force `
                -Filter $pattern `
                -ErrorAction SilentlyContinue
        }
    ) |
    Sort-Object `
        FullName `
        -Unique

if (@($forbiddenFiles).Count -gt 0) {
    $forbiddenFileList =
        $forbiddenFiles.FullName -join [Environment]::NewLine

    throw (
        "配布物に秘密情報の可能性がある" +
        "ファイルが含まれています。" +
        [Environment]::NewLine +
        $forbiddenFileList
    )
}

$forbiddenDirectoryNames = @(
    "Logs"
    "TaskBackup"
)

$forbiddenDirectories =
    Get-ChildItem `
        -LiteralPath $releaseDirectory `
        -Directory `
        -Recurse `
        -Force |
    Where-Object {
        $_.Name -in $forbiddenDirectoryNames
    }

if (@($forbiddenDirectories).Count -gt 0) {
    throw (
        "配布物に含めないフォルダーがあります: " +
        (
            $forbiddenDirectories.FullName -join
            ", "
        )
    )
}

Write-Host "[OK] .env、秘密鍵、ログの混入はありません"

# =====================================
# 8. runtime.zipを再検証
# =====================================
$releaseRuntimeArchivePath = Join-Path `
    $releaseGuiDirectory `
    "Payload\runtime.zip"

$releaseRuntimeManifestPath = Join-Path `
    $releaseGuiDirectory `
    "Payload\runtime-manifest.json"

$releaseRuntimeManifest =
    Get-Content `
        -LiteralPath $releaseRuntimeManifestPath `
        -Raw `
        -Encoding UTF8 |
    ConvertFrom-Json

$releaseRuntimeHash =
    (
        Get-FileHash `
            -LiteralPath $releaseRuntimeArchivePath `
            -Algorithm SHA256
    ).Hash.ToUpperInvariant()

if (
    $releaseRuntimeManifest.archiveSha256 -ne
    $releaseRuntimeHash
) {
    throw (
        "配布フォルダー内のruntime.zipが" +
        "マニフェストと一致しません"
    )
}

Write-Host "[OK] 配布用runtime.zipを再検証しました"

# =====================================
# 9. Release情報を作成
# =====================================
Write-Host ""
Write-Host ">> Release情報を作成します"

$guiFileInfo =
    Get-Item `
        -LiteralPath (
            Join-Path `
                $releaseGuiDirectory `
                "WorkingTimeChecker.Gui.exe"
        )

$runnerFileInfo =
    Get-Item `
        -LiteralPath (
            Join-Path `
                $releaseRunnerDirectory `
                "WorkingTimeChecker.Runner.exe"
        )

$runtimeArchiveInfo =
    Get-Item `
        -LiteralPath $releaseRuntimeArchivePath

$releaseInfo =
    [ordered]@{
        schemaVersion = 1
        productName = "WorkingTimeChecker"
        guiVersion = $GuiVersion
        runtimeVersion = $RuntimeVersion
        runtimeIdentifier = $RuntimeIdentifier
        createdAtUtc = [DateTime]::UtcNow.ToString("O")
        contents = [ordered]@{
            gui = "Application/WorkingTimeChecker.Gui.exe"
            runner = "Runner/WorkingTimeChecker.Runner.exe"
            runtimeArchive = "Application/Payload/runtime.zip"
            runtimeManifest =
                "Application/Payload/runtime-manifest.json"
            defaultConfig =
                "Application/Defaults/config.example.yaml"
        }
        sizes = [ordered]@{
            guiBytes = $guiFileInfo.Length
            runnerBytes = $runnerFileInfo.Length
            runtimeArchiveBytes =
                $runtimeArchiveInfo.Length
        }
    }

$releaseInfoJson =
    $releaseInfo |
    ConvertTo-Json `
        -Depth 6

[System.IO.File]::WriteAllText(
    $releaseInfoPath,
    $releaseInfoJson,
    $utf8NoBom
)

Write-Host "[OK] release-info.jsonを作成しました"

# =====================================
# 10. SHA-256一覧を作成
# =====================================
Write-Host ""
Write-Host ">> SHA-256一覧を作成します"

$filesForChecksum =
    Get-ChildItem `
        -LiteralPath $releaseDirectory `
        -File `
        -Recurse `
        -Force |
    Where-Object {
        $_.FullName -ne $checksumPath
    } |
    Sort-Object `
        FullName

$checksumLines =
    foreach ($file in $filesForChecksum) {
        $hash =
            (
                Get-FileHash `
                    -LiteralPath $file.FullName `
                    -Algorithm SHA256
            ).Hash.ToUpperInvariant()

        $relativePath =
            Get-RelativeReleasePath `
                -FilePath $file.FullName

        $hash + "  " + $relativePath
    }

[System.IO.File]::WriteAllLines(
    $checksumPath,
    $checksumLines,
    $utf8NoBom
)

Assert-FileExists `
    -Path $checksumPath `
    -DisplayName "SHA256SUMS.txt"

Write-Host (
    "[OK] SHA-256一覧を作成しました: " +
    @($checksumLines).Count +
    "ファイル"
)

# =====================================
# 11. 配布用ZIPを作成
# =====================================
Write-Host ""
Write-Host ">> 配布用ZIPを作成します"

$maximumZipAttempts = 10
$zipCreated = $false

for (
    $attempt = 1;
    $attempt -le $maximumZipAttempts;
    $attempt++
) {
    try {
        if (Test-Path -LiteralPath $releaseZipPath) {
            Remove-Item `
                -LiteralPath $releaseZipPath `
                -Force
        }

        Compress-Archive `
            -Path (
                Join-Path `
                    $releaseDirectory `
                    "*"
            ) `
            -DestinationPath $releaseZipPath `
            -CompressionLevel Optimal `
            -ErrorAction Stop

        $zipCreated = $true

        Write-Host (
            "[OK] 配布用ZIPを作成しました。" +
            "試行回数: $attempt"
        )

        break
    }
    catch [System.IO.IOException] {
        if ($attempt -ge $maximumZipAttempts) {
            throw
        }

        Write-Host (
            "[WARN] ZIP作成時にファイルが使用中です。" +
            " 1秒後に再試行します。" +
            " 試行: $attempt/$maximumZipAttempts"
        )

        Start-Sleep `
            -Seconds 1
    }
    catch [System.UnauthorizedAccessException] {
        if ($attempt -ge $maximumZipAttempts) {
            throw
        }

        Write-Host (
            "[WARN] ZIP作成時にファイルへアクセスできません。" +
            " 1秒後に再試行します。" +
            " 試行: $attempt/$maximumZipAttempts"
        )

        Start-Sleep `
            -Seconds 1
    }
}

if (-not $zipCreated) {
    throw "配布用ZIPを作成できませんでした"
}

Assert-FileExists `
    -Path $releaseZipPath `
    -DisplayName "配布用ZIP"

$releaseZipHash =
    (
        Get-FileHash `
            -LiteralPath $releaseZipPath `
            -Algorithm SHA256
    ).Hash.ToUpperInvariant()

# =====================================
# 12. 最終結果
# =====================================
$releaseDirectoryInfo =
    Get-ChildItem `
        -LiteralPath $releaseDirectory `
        -File `
        -Recurse |
    Measure-Object `
        -Property Length `
        -Sum

$releaseZipInfo =
    Get-Item `
        -LiteralPath $releaseZipPath

$releaseSizeMb =
    [System.Math]::Round(
        [double]$releaseDirectoryInfo.Sum / 1MB,
        2
    )

$releaseZipSizeMb =
    [System.Math]::Round(
        [double]$releaseZipInfo.Length / 1MB,
        2
    )

Write-Host ""
Write-Host "=========================================="
Write-Host " [OK] Release配布物の作成完了"
Write-Host "=========================================="
Write-Host "GUI Version: $GuiVersion"
Write-Host "Runtime Version: $RuntimeVersion"
Write-Host "Release Directory: $releaseDirectory"
Write-Host "Release ZIP: $releaseZipPath"
Write-Host "Directory Size: $releaseSizeMb MB"
Write-Host "ZIP Size: $releaseZipSizeMb MB"
Write-Host "ZIP SHA-256: $releaseZipHash"
