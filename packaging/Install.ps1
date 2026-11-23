[CmdletBinding()]
param(
    [Parameter()]
    [switch]$DoNotLaunch
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$packageDirectory =
    Split-Path `
        -Parent `
        $MyInvocation.MyCommand.Path

$sourceApplicationDirectory = Join-Path `
    $packageDirectory `
    "Application"

$sourceRunnerDirectory = Join-Path `
    $packageDirectory `
    "Runner"

$sourceGuiPath = Join-Path `
    $sourceApplicationDirectory `
    "WorkingTimeChecker.Gui.exe"

$sourceRunnerPath = Join-Path `
    $sourceRunnerDirectory `
    "WorkingTimeChecker.Runner.exe"

$sourceDefaultConfigPath = Join-Path `
    $sourceApplicationDirectory `
    "Defaults\config.example.yaml"

$applicationDataDirectory = Join-Path `
    $env:LOCALAPPDATA `
    "PERSOL_CROSS_TECHNOLOGY\WorkingTimeChecker"

$applicationDirectory = Join-Path `
    $applicationDataDirectory `
    "Application"

$runnerDirectory = Join-Path `
    $applicationDataDirectory `
    "Runner"

$configDirectory = Join-Path `
    $applicationDataDirectory `
    "Config"

$logsDirectory = Join-Path `
    $applicationDataDirectory `
    "Logs"

$runtimeDirectory = Join-Path `
    $applicationDataDirectory `
    "Runtime"

$stateDirectory = Join-Path `
    $applicationDataDirectory `
    "State"

$tempDirectory = Join-Path `
    $applicationDataDirectory `
    "Temp"

$installedGuiPath = Join-Path `
    $applicationDirectory `
    "WorkingTimeChecker.Gui.exe"

$installedRunnerPath = Join-Path `
    $runnerDirectory `
    "WorkingTimeChecker.Runner.exe"

$installedConfigPath = Join-Path `
    $configDirectory `
    "config.yaml"

$desktopDirectory =
    [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::DesktopDirectory
    )

$companyStartMenuDirectory = Join-Path `
    (
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::Programs
        )
    ) `
    "PERSOL CROSS TECHNOLOGY"

$startMenuDirectory = Join-Path `
    $companyStartMenuDirectory `
    "WorkingTimeChecker"

$desktopShortcutPath = Join-Path `
    $desktopDirectory `
    "WorkingTimeChecker.lnk"

$startMenuShortcutPath = Join-Path `
    $startMenuDirectory `
    "WorkingTimeChecker.lnk"

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

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)]
        [string]$SourceDirectory,

        [Parameter(Mandatory)]
        [string]$DestinationDirectory
    )

    New-Item `
        -Path $DestinationDirectory `
        -ItemType Directory `
        -Force |
    Out-Null

    Get-ChildItem `
        -LiteralPath $SourceDirectory `
        -Force |
    ForEach-Object {
        Copy-Item `
            -LiteralPath $_.FullName `
            -Destination $DestinationDirectory `
            -Recurse `
            -Force
    }
}

function New-ApplicationShortcut {
    param(
        [Parameter(Mandatory)]
        [string]$ShortcutPath
    )

    $shortcutDirectory =
        Split-Path `
            -Parent `
            $ShortcutPath

    New-Item `
        -Path $shortcutDirectory `
        -ItemType Directory `
        -Force |
    Out-Null

    $shell =
        New-Object `
            -ComObject WScript.Shell

    try {
        $shortcut =
            $shell.CreateShortcut(
                $ShortcutPath
            )

        $shortcut.TargetPath =
            $installedGuiPath

        $shortcut.WorkingDirectory =
            $applicationDirectory

        $shortcut.IconLocation =
            $installedGuiPath + ",0"

        $shortcut.Description =
            "WorkingTimeChecker"

        $shortcut.Save()
    }
    finally {
        if ($null -ne $shell) {
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject(
                $shell
            )
        }
    }
}

Write-Host "=========================================="
Write-Host " WorkingTimeChecker インストール"
Write-Host "=========================================="
Write-Host ""

Assert-FileExists `
    -Path $sourceGuiPath `
    -DisplayName "配布用GUI"

Assert-FileExists `
    -Path $sourceRunnerPath `
    -DisplayName "配布用Runner"

Assert-FileExists `
    -Path $sourceDefaultConfigPath `
    -DisplayName "config.example.yaml"

Get-Process `
    -Name "WorkingTimeChecker.Gui" `
    -ErrorAction SilentlyContinue |
Stop-Process -Force

Write-Host ">> インストール先を作成します"

foreach ($directory in @(
    $applicationDirectory
    $runnerDirectory
    $configDirectory
    $logsDirectory
    $runtimeDirectory
    $stateDirectory
    $tempDirectory
)) {
    New-Item `
        -Path $directory `
        -ItemType Directory `
        -Force |
    Out-Null
}

Write-Host ">> GUIを配置します"

Copy-DirectoryContents `
    -SourceDirectory $sourceApplicationDirectory `
    -DestinationDirectory $applicationDirectory

Write-Host ">> Runnerを固定パスへ配置します"

Copy-Item `
    -LiteralPath $sourceRunnerPath `
    -Destination $installedRunnerPath `
    -Force

if (-not (Test-Path -LiteralPath $installedConfigPath)) {
    Copy-Item `
        -LiteralPath $sourceDefaultConfigPath `
        -Destination $installedConfigPath

    Write-Host "[OK] 初期config.yamlを配置しました"
}
else {
    Write-Host "[OK] 既存のconfig.yamlを維持しました"
}

Assert-FileExists `
    -Path $installedGuiPath `
    -DisplayName "配置したGUI"

Assert-FileExists `
    -Path $installedRunnerPath `
    -DisplayName "配置したRunner"

Write-Host ">> ショートカットを作成します"

New-ApplicationShortcut `
    -ShortcutPath $desktopShortcutPath

New-ApplicationShortcut `
    -ShortcutPath $startMenuShortcutPath

Write-Host "[OK] デスクトップショートカットを作成しました"
Write-Host "[OK] スタートメニューへ登録しました"

Write-Host ""
Write-Host "=========================================="
Write-Host " [OK] インストール完了"
Write-Host "=========================================="
Write-Host "GUI: $installedGuiPath"
Write-Host "Runner: $installedRunnerPath"
Write-Host "Config: $installedConfigPath"

# =====================================
# Windowsの「インストールされているアプリ」へ登録
# =====================================
$installedUninstallScriptPath = Join-Path `
    $applicationDataDirectory `
    "Uninstall.ps1"

$sourceUninstallScriptPath = Join-Path `
    $packageDirectory `
    "Uninstall.ps1"

if (-not (Test-Path -LiteralPath $sourceUninstallScriptPath)) {
    throw (
        "Uninstall.ps1が見つかりません: " +
        $sourceUninstallScriptPath
    )
}

Copy-Item `
    -LiteralPath $sourceUninstallScriptPath `
    -Destination $installedUninstallScriptPath `
    -Force

$releaseInfoPath = Join-Path `
    $packageDirectory `
    "release-info.json"

$displayVersion =
    "1.0.1"

if (Test-Path -LiteralPath $releaseInfoPath) {
    $releaseInfo =
        Get-Content `
            -LiteralPath $releaseInfoPath `
            -Raw `
            -Encoding UTF8 |
        ConvertFrom-Json

    if (-not [string]::IsNullOrWhiteSpace(
            $releaseInfo.guiVersion)) {
        $displayVersion =
            [string]$releaseInfo.guiVersion
    }
}

$uninstallRegistryPath =
    "HKCU:\Software\Microsoft\Windows\" +
    "CurrentVersion\Uninstall\WorkingTimeChecker"

New-Item `
    -Path $uninstallRegistryPath `
    -Force |
Out-Null

$uninstallCommand =
    '"' +
    "$env:SystemRoot\System32\WindowsPowerShell\v1.0\" +
    'powershell.exe" ' +
    '-NoProfile -ExecutionPolicy Bypass -File "' +
    $installedUninstallScriptPath +
    '"'

$estimatedSizeKb =
    [int](
        (
            Get-ChildItem `
                -LiteralPath $applicationDataDirectory `
                -File `
                -Recurse `
                -ErrorAction SilentlyContinue |
            Measure-Object `
                -Property Length `
                -Sum
        ).Sum / 1KB
    )

$registryValues = [ordered]@{
    DisplayName =
        "WorkingTimeChecker"

    DisplayVersion =
        $displayVersion

    Publisher =
        "PERSOL CROSS TECHNOLOGY"

    InstallLocation =
        $applicationDataDirectory

    DisplayIcon =
        $installedGuiPath + ",0"

    UninstallString =
        $uninstallCommand

    QuietUninstallString =
        $uninstallCommand

    InstallDate =
        (Get-Date).ToString("yyyyMMdd")

    NoModify =
        1

    NoRepair =
        1

    EstimatedSize =
        $estimatedSizeKb
}

foreach ($entry in $registryValues.GetEnumerator()) {
    $propertyType =
        if ($entry.Value -is [int]) {
            "DWord"
        }
        else {
            "String"
        }

    New-ItemProperty `
        -Path $uninstallRegistryPath `
        -Name $entry.Key `
        -Value $entry.Value `
        -PropertyType $propertyType `
        -Force |
    Out-Null
}

Write-Host (
    "[OK] Windowsのインストール済みアプリへ" +
    "登録しました"
)
if (-not $DoNotLaunch) {
    Write-Host ""
    Write-Host ">> WorkingTimeCheckerを起動します"

    Start-Process `
        -FilePath $installedGuiPath `
        -WorkingDirectory $applicationDirectory
}