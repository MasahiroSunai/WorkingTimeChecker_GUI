[CmdletBinding()]
param(
    [Parameter()]
    [switch]$RemoveUserData,

    [Parameter()]
    [switch]$RemoveScheduledTasks
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$applicationDataDirectory = Join-Path `
    $env:LOCALAPPDATA `
    "PERSOL_CROSS_TECHNOLOGY\WorkingTimeChecker"

$applicationDirectory = Join-Path `
    $applicationDataDirectory `
    "Application"

$runnerDirectory = Join-Path `
    $applicationDataDirectory `
    "Runner"

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

$runnerPath = Join-Path `
    $runnerDirectory `
    "WorkingTimeChecker.Runner.exe"

Write-Host "=========================================="
Write-Host " WorkingTimeChecker アンインストール"
Write-Host "=========================================="
Write-Host ""

Get-Process `
    -Name "WorkingTimeChecker.Gui" `
    -ErrorAction SilentlyContinue |
Stop-Process -Force

Get-Process `
    -Name "WorkingTimeChecker.Runner" `
    -ErrorAction SilentlyContinue |
Stop-Process -Force

Get-Process `
    -Name "WorkingTimeChecker.Cli" `
    -ErrorAction SilentlyContinue |
Stop-Process -Force

if ($RemoveScheduledTasks) {
    Write-Host ">> Runnerを使用するタスクを確認します"

    $tasks =
        @(
            Get-ScheduledTask `
                -ErrorAction SilentlyContinue |
            Where-Object {
                $task = $_

                @(
                    $task.Actions |
                    Where-Object {
                        [string]::Equals(
                            $_.Execute,
                            $runnerPath,
                            [System.StringComparison]::OrdinalIgnoreCase
                        )
                    }
                ).Count -gt 0
            }
        )

    foreach ($task in $tasks) {
        Unregister-ScheduledTask `
            -TaskName $task.TaskName `
            -TaskPath $task.TaskPath `
            -Confirm:$false

        Write-Host (
            "[OK] タスクを削除しました: " +
            $task.TaskPath +
            $task.TaskName
        )
    }

    if ($tasks.Count -eq 0) {
        Write-Host "[OK] 削除対象のタスクはありません"
    }
}
else {
    Write-Host (
        "[INFO] タスクスケジューラの設定は維持します。" +
        "必要なら-RemoveScheduledTasksを指定してください。"
    )
}

foreach ($shortcutPath in @(
    $desktopShortcutPath
)) {
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item `
            -LiteralPath $shortcutPath `
            -Force
    }
}

if (Test-Path -LiteralPath $startMenuDirectory) {
    Remove-Item `
        -LiteralPath $startMenuDirectory `
        -Recurse `
        -Force
}

if ($RemoveUserData) {
    if (Test-Path -LiteralPath $applicationDataDirectory) {
        Remove-Item `
            -LiteralPath $applicationDataDirectory `
            -Recurse `
            -Force
    }

    Write-Host "[OK] アプリとLocalAppDataのユーザーデータを削除しました"
}
else {
    foreach ($directory in @(
        $applicationDirectory
        $runnerDirectory
    )) {
        if (Test-Path -LiteralPath $directory) {
            Remove-Item `
                -LiteralPath $directory `
                -Recurse `
                -Force
        }
    }

    Write-Host "[OK] GUIとRunnerを削除しました"
    Write-Host ""
    Write-Host "次のデータは維持しています:"
    Write-Host "  Config"
    Write-Host "  Logs"
    Write-Host "  Runtime"
    Write-Host "  State"
    Write-Host "  Windows資格情報マネージャーの9項目"
}

Write-Host ""
Write-Host "=========================================="

# Windowsの「インストールされているアプリ」から削除します。
$uninstallRegistryPath =
    "HKCU:\Software\Microsoft\Windows\" +
    "CurrentVersion\Uninstall\WorkingTimeChecker"

if (Test-Path -LiteralPath $uninstallRegistryPath) {
    Remove-Item `
        -LiteralPath $uninstallRegistryPath `
        -Recurse `
        -Force

    Write-Host (
        "[OK] Windowsのインストール済みアプリから" +
        "登録を削除しました"
    )
}
Write-Host " [OK] アンインストール完了"
Write-Host "=========================================="