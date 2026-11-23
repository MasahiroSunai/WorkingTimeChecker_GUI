using System.Diagnostics;
using System.Text;
using WorkingTimeChecker.Core;
using WorkingTimeChecker.Core.Security;

Console.OutputEncoding =
    Encoding.UTF8;

CommandOptions options;

try
{
    options =
        ParseArguments(args);

    ApplicationPaths.Configure(
        options.ConfigPath,
        options.LogDirectory,
        options.EnvFilePath);

    Directory.CreateDirectory(
        ApplicationPaths.LogDirectory);
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "[NG] " + exception.Message);

    ShowUsageToConsole();

    return 2;
}

using var logger =
    new AppLogger(
        ApplicationPaths.LogDirectory);

using var cancellationTokenSource =
    new CancellationTokenSource();

Console.CancelKeyPress +=
    (_, eventArgs) =>
    {
        eventArgs.Cancel = true;

        logger.WriteLine(
            "中断要求を受け付けました。");

        cancellationTokenSource.Cancel();
    };

try
{
    logger.WriteLine(
        "===== WorkingTimeChecker.Cli 起動 =====");

    logger.WriteLine(
        $"ログファイル: {logger.LogFilePath}");

    logger.WriteLine(
        "[OK] LogDirectory: " +
        ApplicationPaths.LogDirectory);

    if (options.RuntimeCheck)
    {
        return await RunRuntimeCheckAsync(
            logger,
            cancellationTokenSource.Token);
    }

    if (!File.Exists(
            ApplicationPaths.ConfigPath))
    {
        logger.WriteError(
            "[NG] config.yamlが見つかりません。");

        logger.WriteError(
            ApplicationPaths.ConfigPath);

        return 2;
    }

    logger.WriteLine(
        "[OK] ConfigPath: " +
        ApplicationPaths.ConfigPath);
    if (File.Exists(
            ApplicationPaths.EnvFilePath))
    {
        logger.WriteLine(
            "[INFO] .env移行元: " +
            ApplicationPaths.EnvFilePath);
    }
    else
    {
        logger.WriteLine(
            "[INFO] .env移行元はありません。");
    }

    var embeddedPythonScriptsSize =
        EmbeddedResourceChecker
            .GetPythonScriptsSize();

    logger.WriteLine(
        "[OK] 組み込みPythonスクリプトを確認しました: " +
        $"{embeddedPythonScriptsSize:N0} bytes");

    var runtimeExtractor =
        new RuntimeExtractor();

    var pythonScriptsDirectory =
        runtimeExtractor
            .EnsurePythonScriptsExtracted(
                logger.WriteLine);

    var pythonRuntimeExtractor =
        new PythonRuntimeExtractor();

    var pythonExecutablePath =
        pythonRuntimeExtractor.EnsureExtracted(
            logger.WriteLine);

    var credentialBootstrapper =
        new EnvironmentCredentialBootstrapper();

    var loadedCredentialCount =
        credentialBootstrapper.LoadOrMigrate(
            ApplicationPaths.EnvFilePath,
            logger.WriteLine);

    logger.WriteLine(
        "[OK] 認証情報をプロセス環境変数へ設定しました: " +
        $"{loadedCredentialCount}項目");

    var pythonRunner =
        new PythonRunner();

    var jobRunner =
        new JobRunner(
            pythonRunner,
            pythonScriptsDirectory,
            pythonExecutablePath);

    int exitCode;

    if (!string.IsNullOrWhiteSpace(
            options.Only))
    {
        logger.WriteLine(
            $"実行対象: {options.Only}");

        if (options.Validate)
        {
            logger.WriteLine(
                "実行モード: 設定検証のみ");
        }

        exitCode =
            await jobRunner.RunOnlyAsync(
                options.Only,
                logger.WriteLine,
                cancellationTokenSource.Token,
                validateOnly: options.Validate);
    }
    else
    {
        WriteExecutionRange(
            options.From,
            options.To,
            logger);

        exitCode =
            await jobRunner.RunRangeAsync(
                options.From,
                options.To,
                logger.WriteLine,
                cancellationTokenSource.Token);
    }

    if (exitCode != 0)
    {
        logger.WriteError(
            "[NG] 処理に失敗しました。" +
            $"終了コード: {exitCode}");

        return exitCode;
    }

    logger.WriteLine(
        "===== [OK] WorkingTimeChecker.Cli 正常終了 =====");

    return 0;
}
catch (OperationCanceledException)
{
    logger.WriteError(
        "[NG] 処理を中断しました。");

    return 20;
}
catch (InvalidOperationException exception)
{
    logger.WriteError(
        $"[NG] {exception.Message}");

    return 3;
}
catch (Exception exception)
{
    logger.WriteError(
        $"[NG] {exception.Message}");

    ShowUsage(logger);

    return 1;
}

static async Task<int> RunRuntimeCheckAsync(
    AppLogger logger,
    CancellationToken cancellationToken)
{
    logger.WriteLine(
        "実行モード: Runtime確認");

    var embeddedPythonScriptsSize =
        EmbeddedResourceChecker
            .GetPythonScriptsSize();

    if (embeddedPythonScriptsSize <= 0)
    {
        logger.WriteError(
            "[NG] 組み込みPythonスクリプトが空です。");

        return 1;
    }

    logger.WriteLine(
        "[OK] 組み込みPythonスクリプト: " +
        $"{embeddedPythonScriptsSize:N0} bytes");

    var runtimeExtractor =
        new RuntimeExtractor();

    var pythonScriptsDirectory =
        runtimeExtractor
            .EnsurePythonScriptsExtracted(
                logger.WriteLine);

    if (!Directory.Exists(
            pythonScriptsDirectory))
    {
        logger.WriteError(
            "[NG] Pythonスクリプトの展開先がありません。");

        return 1;
    }

    logger.WriteLine(
        "[OK] Pythonスクリプト展開先: " +
        pythonScriptsDirectory);

    var pythonRuntimeExtractor =
        new PythonRuntimeExtractor();

    var pythonExecutablePath =
        pythonRuntimeExtractor.EnsureExtracted(
            logger.WriteLine);

    if (!File.Exists(
            pythonExecutablePath))
    {
        logger.WriteError(
            "[NG] 内蔵Pythonが見つかりません。");

        return 1;
    }

    logger.WriteLine(
        "[OK] Python実行ファイル: " +
        pythonExecutablePath);

    var startInfo =
        new ProcessStartInfo
        {
            FileName =
                pythonExecutablePath,

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

    startInfo.ArgumentList.Add(
        "--version");

    using var process =
        new Process
        {
            StartInfo =
                startInfo,
        };

    if (!process.Start())
    {
        logger.WriteError(
            "[NG] 内蔵Pythonを起動できませんでした。");

        return 1;
    }

    var standardOutputTask =
        process.StandardOutput
            .ReadToEndAsync(
                cancellationToken);

    var standardErrorTask =
        process.StandardError
            .ReadToEndAsync(
                cancellationToken);

    await process.WaitForExitAsync(
        cancellationToken);

    var standardOutput =
        (await standardOutputTask)
            .Trim();

    var standardError =
        (await standardErrorTask)
            .Trim();

    var versionText =
        !string.IsNullOrWhiteSpace(
            standardOutput)
            ? standardOutput
            : standardError;

    if (!string.IsNullOrWhiteSpace(
            versionText))
    {
        logger.WriteLine(
            "[OK] " + versionText);
    }

    if (process.ExitCode != 0)
    {
        logger.WriteError(
            "[NG] 内蔵Pythonの確認に失敗しました。" +
            $"終了コード: {process.ExitCode}");

        return process.ExitCode;
    }

    logger.WriteLine(
        "===== [OK] Runtime確認完了 =====");

    return 0;
}

static CommandOptions ParseArguments(
    string[] commandLineArguments)
{
    if (commandLineArguments.Length == 0)
    {
        throw new ArgumentException(
            "実行範囲を指定してください。");
    }

    string? only = null;
    string? from = null;
    string? to = null;
    string? configPath = null;
    string? logDirectory = null;
    string? envFilePath = null;

    var runAll = false;
    var validate = false;
    var runtimeCheck = false;

    for (var index = 0;
         index < commandLineArguments.Length;
         index++)
    {
        var argument =
            commandLineArguments[index];

        switch (argument.ToLowerInvariant())
        {
            case "--all":
                runAll = true;
                break;

            case "--validate":
                validate = true;
                break;

            case "--runtime-check":
                runtimeCheck = true;
                break;

            case "--only":
                only =
                    ReadOptionValue(
                        commandLineArguments,
                        ref index,
                        "--only");
                break;

            case "--from":
                from =
                    ReadOptionValue(
                        commandLineArguments,
                        ref index,
                        "--from");
                break;

            case "--to":
                to =
                    ReadOptionValue(
                        commandLineArguments,
                        ref index,
                        "--to");
                break;

            case "--config":
                configPath =
                    ReadOptionValue(
                        commandLineArguments,
                        ref index,
                        "--config");
                break;

            case "--log-directory":
                logDirectory =
                    ReadOptionValue(
                        commandLineArguments,
                        ref index,
                        "--log-directory");
                break;

            case "--env":
                envFilePath =
                    ReadOptionValue(
                        commandLineArguments,
                        ref index,
                        "--env");
                break;

            default:
                throw new ArgumentException(
                    $"不明な引数です: {argument}");
        }
    }

    var hasExecutionRange =
        runAll ||
        only is not null ||
        from is not null ||
        to is not null;

    if (runtimeCheck &&
        (hasExecutionRange ||
         validate))
    {
        throw new ArgumentException(
            "--runtime-checkは実行範囲や" +
            "--validateと同時に使えません。");
    }

    if (!runtimeCheck &&
        !hasExecutionRange)
    {
        throw new ArgumentException(
            "実行範囲を指定してください。");
    }

    if (runAll &&
        (only is not null ||
         from is not null ||
         to is not null))
    {
        throw new ArgumentException(
            "--allは他の実行範囲指定と同時に使えません。");
    }

    if (only is not null &&
        (from is not null ||
         to is not null))
    {
        throw new ArgumentException(
            "--onlyは--fromまたは--toと同時に使えません。");
    }

    if (validate &&
        !string.Equals(
            only,
            "ExcelMacro",
            StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException(
            "--validateは--only ExcelMacroと" +
            "組み合わせてください。");
    }

    return new CommandOptions(
        Only: only,
        From: from,
        To: to,
        Validate: validate,
        RuntimeCheck: runtimeCheck,
        ConfigPath: configPath,
        LogDirectory: logDirectory,
        EnvFilePath: envFilePath);
}

static string ReadOptionValue(
    string[] commandLineArguments,
    ref int currentIndex,
    string optionName)
{
    var valueIndex =
        currentIndex + 1;

    if (valueIndex >=
        commandLineArguments.Length)
    {
        throw new ArgumentException(
            $"{optionName}の後に値を指定してください。");
    }

    var value =
        commandLineArguments[valueIndex];

    if (value.StartsWith(
            "--",
            StringComparison.Ordinal))
    {
        throw new ArgumentException(
            $"{optionName}の後に値を指定してください。");
    }

    currentIndex =
        valueIndex;

    return value;
}

static void WriteExecutionRange(
    string? from,
    string? to,
    AppLogger logger)
{
    if (from is null &&
        to is null)
    {
        logger.WriteLine(
            "実行対象: すべてのステップ");

        return;
    }

    if (from is not null &&
        to is not null)
    {
        logger.WriteLine(
            $"実行範囲: {from} → {to}");

        return;
    }

    if (from is not null)
    {
        logger.WriteLine(
            $"実行範囲: {from}以降");

        return;
    }

    logger.WriteLine(
        $"実行範囲: 最初から{to}まで");
}

static void ShowUsage(
    AppLogger logger)
{
    logger.WriteError("");

    logger.WriteError(
        "使用方法:");

    foreach (var line in
             GetUsageLines())
    {
        logger.WriteError(line);
    }
}

static void ShowUsageToConsole()
{
    Console.Error.WriteLine("");

    Console.Error.WriteLine(
        "使用方法:");

    foreach (var line in
             GetUsageLines())
    {
        Console.Error.WriteLine(line);
    }
}

static IReadOnlyList<string> GetUsageLines()
{
    var lines =
        new List<string>
        {
            "  WorkingTimeChecker.Cli.exe --runtime-check",
            "  WorkingTimeChecker.Cli.exe --only ステップ名",
            "  WorkingTimeChecker.Cli.exe " +
            "--only ExcelMacro --validate",
            "  WorkingTimeChecker.Cli.exe --from ステップ名",
            "  WorkingTimeChecker.Cli.exe --to ステップ名",
            "  WorkingTimeChecker.Cli.exe " +
            "--from ステップ名 --to ステップ名",
            "  WorkingTimeChecker.Cli.exe --all",
            "",
            "共通オプション:",
            "  --config config.yamlのパス",
            "  --log-directory ログフォルダー",
            "  --env .envのパス",
            "",
            "指定可能なステップ:",
        };

    foreach (var stepName in
             JobRunner.GetStepNames())
    {
        lines.Add(
            $"  {stepName}");
    }

    return lines;
}

internal sealed record CommandOptions(
    string? Only,
    string? From,
    string? To,
    bool Validate,
    bool RuntimeCheck,
    string? ConfigPath,
    string? LogDirectory,
    string? EnvFilePath);