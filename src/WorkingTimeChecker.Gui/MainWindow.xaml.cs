using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using WorkingTimeChecker.Core.Application;
namespace WorkingTimeChecker.Gui;

public partial class MainWindow : Window
{
    private static readonly string[] StepOrder =
    [
        "ConfluenceDownload",
        "ExcelMacro",
        "WebAttendanceDownload",
        "WorkingTimeChecker",
        "CopyConfluence",
        "PostRocketChat",
    ];

    private readonly bool _configCopiedOnStartup;
    private readonly RuntimeInstallResult _runtimeInstallResult;
    private readonly IReadOnlyList<string> _startupMessages;
    private CancellationTokenSource? _cancellationTokenSource;
    private Process? _runningProcess;
    private string _publishDirectory = string.Empty;
    private string _executablePath = string.Empty;
    private string _configPath = string.Empty;
    private string _logDirectory = string.Empty;
    private string _envFilePath = string.Empty;

    public MainWindow(
        bool configCopied,
        RuntimeInstallResult runtimeInstallResult,
        IReadOnlyList<string> startupMessages)
    {
        InitializeComponent();

        ArgumentNullException.ThrowIfNull(
            runtimeInstallResult);

        ArgumentNullException.ThrowIfNull(
            startupMessages);

        _configCopiedOnStartup =
            configCopied;

        _runtimeInstallResult =
            runtimeInstallResult;

        _startupMessages =
            startupMessages;

        var runtimeDirectory =
            Path.GetDirectoryName(
                runtimeInstallResult.CliExecutablePath)
            ?? throw new InvalidOperationException(
                "RuntimeのCLIフォルダーを取得できません。");

        SetApplicationPaths(
            runtimeDirectory,
            runtimeInstallResult.CliExecutablePath);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        ReloadSettings();

        foreach (var message in
                 _startupMessages)
        {
            AppendLog(message);
        }

        if (_configCopiedOnStartup)
        {
            AppendLog(
                "[OK] 初期設定をLocalAppDataへコピーしました。");
        }
        else
        {
            AppendLog(
                "[OK] LocalAppDataの既存設定を使用します。");
        }

        var runtimeStatusText =
            _runtimeInstallResult.Status switch
            {
                RuntimeInstallStatus.Installed =>
                    "初回インストール",

                RuntimeInstallStatus.Updated =>
                    "更新",

                RuntimeInstallStatus.AlreadyInstalled =>
                    "導入済みRuntimeを使用",

                _ =>
                    _runtimeInstallResult.Status.ToString(),
            };

        AppendLog(
            "[OK] Runtimeバージョン: " +
            _runtimeInstallResult.RuntimeVersion);

        AppendLog(
            "[OK] Runtime状態: " +
            runtimeStatusText);

        AppendLog(
            "[OK] CLI: " +
            _runtimeInstallResult.CliExecutablePath);

        AppendLog(
            "[OK] 設定ファイル: " +
            ApplicationPaths.ConfigFilePath);

        AppendLog(
            "[OK] ログフォルダー: " +
            ApplicationPaths.LogDirectory);
    }


    private void ResolveApplicationPaths()
    {
        var startDirectory =
            AppContext.BaseDirectory;

        var currentDirectory =
            new DirectoryInfo(startDirectory);

        while (currentDirectory is not null)
        {
            var publishCandidate =
                Path.Combine(
                    currentDirectory.FullName,
                    "publish"
                );

            var executableCandidate =
                Path.Combine(
                    publishCandidate,
                    "WorkingTimeChecker.Cli.exe"
                );

            if (File.Exists(executableCandidate))
            {
                SetApplicationPaths(
                    publishCandidate,
                    executableCandidate
                );

                return;
            }

            currentDirectory =
                currentDirectory.Parent;
        }
        throw new FileNotFoundException(
            "WorkingTimeChecker.Cli.exeが見つかりません。" +
            " Runtimeを再インストールしてください。"
        );
    }

    private void SetApplicationPaths(
        string publishDirectory,
        string executablePath)
    {
        _publishDirectory =
            publishDirectory;

        _executablePath =
            executablePath;

        _configPath =
            ApplicationPaths.ConfigFilePath;

        _logDirectory =
            ApplicationPaths.LogDirectory;

        _envFilePath =
            ApplicationPaths.EnvFilePath;

        ExecutablePathText.Text =
            _executablePath;
    }

    private void ReloadSettings()
    {
        try
        {
            if (!File.Exists(_executablePath))
            {
                throw new FileNotFoundException(
                    "Release版CLIが見つかりません。",
                    _executablePath
                );
            }

            if (!File.Exists(_configPath))
            {
                throw new FileNotFoundException(
                    "config.yamlが見つかりません。",
                    _configPath
                );
            }

            var configText =
                File.ReadAllText(
                    _configPath,
                    Encoding.UTF8
                );

            var dryRun =
                ReadYamlScalar(
                    configText,
                    "dry_run"
                );

            var targetMonth =
                ReadYamlScalar(
                    configText,
                    "target_month"
                );

            TargetMonthText.Text =
                FormatTargetMonth(
                    targetMonth
                );

            UpdateDryRunBadge(dryRun);

            AppendLog(
                "[OK] 設定を読み込みました。"
            );
        }
        catch (Exception exception)
        {
            TargetMonthText.Text =
                "取得失敗";

            DryRunText.Text =
                "設定エラー";

            DryRunBadge.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        253,
                        226,
                        226
                    )
                );

            DryRunText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        145,
                        20,
                        20
                    )
                );

            AppendLog(
                "[NG] " + exception.Message
            );

            MessageBox.Show(
                this,
                exception.Message,
                "設定読み込みエラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private static string? ReadYamlScalar(
        string yaml,
        string key)
    {
        var match =
            Regex.Match(
                yaml,
                "^\\s*" +
                Regex.Escape(key) +
                "\\s*:\\s*([^#\\r\\n]+)",
                RegexOptions.Multiline
            );

        if (!match.Success)
        {
            return null;
        }

        return match
            .Groups[1]
            .Value
            .Trim()
            .Trim(
                '"',
                '\''
            );
    }

    private static string FormatTargetMonth(
        string? targetMonth)
    {
        if (string.IsNullOrWhiteSpace(targetMonth) ||
            string.Equals(
                targetMonth,
                "auto",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return
                $"{DateTime.Now.Year}年" +
                $"{DateTime.Now.Month:00}月 " +
                "（自動）";
        }

        if (DateTime.TryParseExact(
                targetMonth,
                "yyyy-MM",
                System.Globalization
                    .CultureInfo
                    .InvariantCulture,
                System.Globalization
                    .DateTimeStyles
                    .None,
                out var parsedMonth))
        {
            return
                $"{parsedMonth.Year}年" +
                $"{parsedMonth.Month:00}月";
        }

        return targetMonth;
    }

    private void UpdateDryRunBadge(
        string? value)
    {
        var isDryRun =
            string.Equals(
                value,
                "true",
                StringComparison.OrdinalIgnoreCase
            );

        if (isDryRun)
        {
            DryRunText.Text =
                "テスト実行";

            DryRunBadge.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        255,
                        243,
                        205
                    )
                );

            DryRunText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        121,
                        92,
                        0
                    )
                );

            return;
        }

        DryRunText.Text =
            "本番実行";

        DryRunBadge.Background =
            new SolidColorBrush(
                Color.FromRgb(
                    217,
                    237,
                    224
                )
            );

        DryRunText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    10,
                    92,
                    45
                )
            );
    }

    private async void ValidateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunCliAsync(
            arguments:
                ["--only", "ExcelMacro", "--validate"],
            displayName:
                "設定検証"
        );
    }

    private async void RunSelectedButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var selectedSteps =
            GetSelectedSteps();

        if (selectedSteps.Count == 0)
        {
            MessageBox.Show(
                this,
                "実行する処理を1つ以上選択してください。",
                "ステップ未選択",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            return;
        }

        var result =
            MessageBox.Show(
                this,
                BuildExecutionConfirmation(
                    selectedSteps
                ),
                "選択処理の実行確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        SetRunningState(true);

        _cancellationTokenSource =
            new CancellationTokenSource();

        try
        {
            foreach (var step in selectedSteps)
            {
                _cancellationTokenSource
                    .Token
                    .ThrowIfCancellationRequested();

                var exitCode =
                    await RunSingleProcessAsync(
                        ["--only", step],
                        step,
                        _cancellationTokenSource.Token
                    );

                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"{step}が失敗しました。" +
                        $"終了コード: {exitCode}"
                    );
                }
            }

            CurrentStepText.Text =
                "正常終了";

            AppendLog(
                "===== [OK] 選択した処理が完了しました ====="
            );

            MessageBox.Show(
                this,
                "選択した処理が正常に完了しました。",
                "実行完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (OperationCanceledException)
        {
            CurrentStepText.Text =
                "キャンセル";

            AppendLog(
                "[WARN] 実行をキャンセルしました。"
            );
        }
        catch (Exception exception)
        {
            CurrentStepText.Text =
                "失敗";

            AppendLog(
                "[NG] " + exception.Message
            );

            MessageBox.Show(
                this,
                exception.Message,
                "実行エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            SetRunningState(false);
        }
    }

    private async void RunAllButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            MessageBox.Show(
                this,
                BuildExecutionConfirmation(
                    StepOrder
                ),
                "全処理の実行確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RunCliAsync(
            arguments:
                ["--all"],
            displayName:
                "すべての処理"
        );
    }

    private async Task RunCliAsync(
        IReadOnlyList<string> arguments,
        string displayName)
    {
        SetRunningState(true);

        _cancellationTokenSource =
            new CancellationTokenSource();

        try
        {
            var exitCode =
                await RunSingleProcessAsync(
                    arguments,
                    displayName,
                    _cancellationTokenSource.Token
                );

            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{displayName}が失敗しました。" +
                    $"終了コード: {exitCode}"
                );
            }

            CurrentStepText.Text =
                "正常終了";

            AppendLog(
                "===== [OK] 実行が完了しました ====="
            );

            MessageBox.Show(
                this,
                $"{displayName}が正常に完了しました。",
                "実行完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (OperationCanceledException)
        {
            CurrentStepText.Text =
                "キャンセル";

            AppendLog(
                "[WARN] 実行をキャンセルしました。"
            );
        }
        catch (Exception exception)
        {
            CurrentStepText.Text =
                "失敗";

            AppendLog(
                "[NG] " + exception.Message
            );

            MessageBox.Show(
                this,
                exception.Message,
                "実行エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            SetRunningState(false);
        }
    }

    private async Task<int> RunSingleProcessAsync(
        IReadOnlyList<string> arguments,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_executablePath))
        {
            throw new FileNotFoundException(
                "Release版CLIが見つかりません。",
                _executablePath
            );
        }

        CurrentStepText.Text =
            $"実行中: {displayName}";

        AppendLog("");
        AppendLog(
            "===== 実行開始: " +
            displayName +
            " ====="
        );

        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    _executablePath,

                WorkingDirectory =
                    _publishDirectory,

                UseShellExecute =
                    false,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true,

                CreateNoWindow =
                    true,

                StandardOutputEncoding =
                    Encoding.UTF8,

                StandardErrorEncoding =
                    Encoding.UTF8,
            };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(
                argument
            );
        }

        
        startInfo.ArgumentList.Add(
            "--config");

        startInfo.ArgumentList.Add(
            _configPath);

        startInfo.ArgumentList.Add(
            "--log-directory");

        startInfo.ArgumentList.Add(
            _logDirectory);

        startInfo.ArgumentList.Add(
            "--env");

        startInfo.ArgumentList.Add(
            _envFilePath);

        using var process =
            new Process
            {
                StartInfo =
                    startInfo,

                EnableRaisingEvents =
                    true,
            };

        _runningProcess =
            process;

        process.OutputDataReceived +=
            (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                {
                    Dispatcher.Invoke(
                        () => AppendLog(
                            eventArgs.Data
                        )
                    );
                }
            };

        process.ErrorDataReceived +=
            (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                {
                    Dispatcher.Invoke(
                        () => AppendLog(
                            eventArgs.Data
                        )
                    );
                }
            };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "CLIを起動できませんでした。"
            );
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }
        finally
        {
            _runningProcess = null;
        }

        return process.ExitCode;
    }

    private List<string> GetSelectedSteps()
    {
        var checkBoxes =
            new[]
            {
                ConfluenceDownloadCheckBox,
                ExcelMacroCheckBox,
                WebAttendanceDownloadCheckBox,
                WorkingTimeCheckerCheckBox,
                CopyConfluenceCheckBox,
                PostRocketChatCheckBox,
            };

        return checkBoxes
            .Where(
                checkBox =>
                    checkBox.IsChecked == true
            )
            .Select(
                checkBox =>
                    checkBox.Tag?.ToString()
                    ?? string.Empty
            )
            .Where(
                step =>
                    !string.IsNullOrWhiteSpace(
                        step
                    )
            )
            .OrderBy(
                step =>
                    Array.IndexOf(
                        StepOrder,
                        step
                    )
            )
            .ToList();
    }

    private string BuildExecutionConfirmation(
        IEnumerable<string> steps)
    {
        var stepText =
            string.Join(
                Environment.NewLine,
                steps.Select(
                    step =>
                        "・" + GetStepDisplayName(step)
                )
            );

        var mode =
            DryRunText.Text == "テスト実行"
                ? "テスト実行"
                : "本番実行";

        return
            "次の処理を実行します。" +
            Environment.NewLine +
            Environment.NewLine +
            stepText +
            Environment.NewLine +
            Environment.NewLine +
            $"実行モード: {mode}" +
            Environment.NewLine +
            Environment.NewLine +
            "よろしいですか？";
    }

    private static string GetStepDisplayName(
        string step)
    {
        return step switch
        {
            "ConfluenceDownload" =>
                "Confluence添付ファイル取得",

            "ExcelMacro" =>
                "工数表集計（C#）",

            "WebAttendanceDownload" =>
                "Web勤怠取得",

            "WorkingTimeChecker" =>
                "工数・勤怠比較",

            "CopyConfluence" =>
                "Confluenceコピー",

            "PostRocketChat" =>
                "Rocket.Chat通知",

            _ =>
                step,
        };
    }

    private void SetRunningState(
        bool isRunning)
    {
        ValidateButton.IsEnabled =
            !isRunning;

        RunSelectedButton.IsEnabled =
            !isRunning;

        RunAllButton.IsEnabled =
            !isRunning;

        ReloadSettingsButton.IsEnabled =
            !isRunning;

        CancelButton.IsEnabled =
            isRunning;

        ConfluenceDownloadCheckBox.IsEnabled =
            !isRunning;

        ExcelMacroCheckBox.IsEnabled =
            !isRunning;

        WebAttendanceDownloadCheckBox.IsEnabled =
            !isRunning;

        WorkingTimeCheckerCheckBox.IsEnabled =
            !isRunning;

        CopyConfluenceCheckBox.IsEnabled =
            !isRunning;

        PostRocketChatCheckBox.IsEnabled =
            !isRunning;

        if (!isRunning &&
            CurrentStepText.Text.StartsWith(
                "実行中:",
                StringComparison.Ordinal
            ))
        {
            CurrentStepText.Text =
                "待機中";
        }
    }

    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_cancellationTokenSource is null)
        {
            return;
        }

        var result =
            MessageBox.Show(
                this,
                "実行中の処理をキャンセルしますか？",
                "キャンセル確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        AppendLog(
            "[WARN] キャンセルを要求しました。"
        );

        _cancellationTokenSource.Cancel();

        if (_runningProcess is not null)
        {
            TryKillProcess(
                _runningProcess
            );
        }
    }

    private static void TryKillProcess(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true
                );
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private void ReloadSettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ReloadSettings();
    }

    private void ClearLogButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    private void OpenLogFolderButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(
                _logDirectory
            );

            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        "explorer.exe",

                    Arguments =
                        $"\"{_logDirectory}\"",

                    UseShellExecute =
                        true,
                }
            );
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "ログフォルダーを開けません",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void AppendLog(
        string message)
    {
        var timestamp =
            DateTime.Now.ToString(
                "HH:mm:ss"
            );

        LogTextBox.AppendText(
            $"[{timestamp}] {message}" +
            Environment.NewLine
        );

        LogTextBox.ScrollToEnd();
    }

    private void MainWindow_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (_runningProcess is null ||
            _runningProcess.HasExited)
        {
            return;
        }

        var result =
            MessageBox.Show(
                this,
                "処理を実行中です。" +
                Environment.NewLine +
                "処理を中止して画面を閉じますか？",
                "終了確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        _cancellationTokenSource?.Cancel();

        TryKillProcess(
            _runningProcess
        );
    }

    private void SettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_runningProcess is not null &&
            !_runningProcess.HasExited)
        {
            MessageBox.Show(
                this,
                "処理の実行中は設定を変更できません。",
                "実行中",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var window =
            new SettingsWindow(
                _configPath)
            {
                Owner =
                    this,
            };

        window.ShowDialog();

        if (window.SettingsSaved)
        {
            ReloadSettings();

            AppendLog(
                "[OK] アプリ設定を保存して再読み込みしました。");
        }
    }

    private void CredentialSettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_runningProcess is not null &&
            !_runningProcess.HasExited)
        {
            MessageBox.Show(
                this,
                "処理の実行中は認証情報を変更できません。",
                "実行中",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var window =
            new CredentialSettingsWindow
            {
                Owner =
                    this,
            };

        window.ShowDialog();

        AppendLog(
            "[INFO] 認証情報の登録状態を更新しました。");
    }
}