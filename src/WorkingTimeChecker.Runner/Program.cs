using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

Console.OutputEncoding =
    Encoding.UTF8;

try
{
    var paths =
        ResolvePaths();

    var state =
        ReadAndValidateState(
            paths.ActiveRuntimeStatePath);

    var cliPath =
        ResolveCliPath(
            paths,
            state);

    if (args.Length == 1 &&
        string.Equals(
            args[0],
            "--runner-check",
            StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine(
            "[OK] Runner確認完了");

        Console.WriteLine(
            "[OK] ActiveVersion: " +
            state.ActiveVersion);

        Console.WriteLine(
            "[OK] CLI: " +
            cliPath);

        return 0;
    }

    var runtimeDirectory =
        Path.GetDirectoryName(
            cliPath)
        ?? throw new InvalidOperationException(
            "Runtimeフォルダーを取得できません。");

    var startInfo =
        new ProcessStartInfo
        {
            FileName =
                cliPath,

            WorkingDirectory =
                runtimeDirectory,

            UseShellExecute =
                false,

            CreateNoWindow =
                false,
        };

    foreach (var argument in args)
    {
        startInfo.ArgumentList.Add(
            argument);
    }

    using var process =
        Process.Start(
            startInfo)
        ?? throw new InvalidOperationException(
            "有効なCLIを起動できません。");

    await process.WaitForExitAsync();

    return process.ExitCode;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "[NG] WorkingTimeChecker.Runner");

    Console.Error.WriteLine(
        exception.Message);

    return 1;
}

static RunnerPaths ResolvePaths()
{
    var localApplicationData =
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

    if (string.IsNullOrWhiteSpace(
            localApplicationData))
    {
        throw new DirectoryNotFoundException(
            "LocalAppDataを取得できません。");
    }

    var applicationDataDirectory =
        Path.Combine(
            localApplicationData,
            "PERSOL_CROSS_TECHNOLOGY",
            "WorkingTimeChecker");

    return new RunnerPaths(
        ApplicationDataDirectory:
            applicationDataDirectory,

        RuntimeRootDirectory:
            Path.Combine(
                applicationDataDirectory,
                "Runtime"),

        ActiveRuntimeStatePath:
            Path.Combine(
                applicationDataDirectory,
                "State",
                "active-runtime.json"));
}

static ActiveRuntimeState ReadAndValidateState(
    string stateFilePath)
{
    if (!File.Exists(
            stateFilePath))
    {
        throw new FileNotFoundException(
            "active-runtime.jsonが見つかりません。",
            stateFilePath);
    }

    var json =
        File.ReadAllText(
            stateFilePath,
            Encoding.UTF8);

    var options =
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive =
                true,
        };

    var state =
        JsonSerializer.Deserialize<ActiveRuntimeState>(
            json,
            options)
        ?? throw new InvalidDataException(
            "active-runtime.jsonを読み取れません。");

    if (state.SchemaVersion != 1)
    {
        throw new InvalidDataException(
            "未対応のRuntime状態ファイルです。" +
            $" SchemaVersion={state.SchemaVersion}");
    }

    if (!Regex.IsMatch(
            state.ActiveVersion,
            @"^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$"))
    {
        throw new InvalidDataException(
            "ActiveVersionの形式が不正です: " +
            state.ActiveVersion);
    }

    if (string.IsNullOrWhiteSpace(
            state.EntryPoint))
    {
        throw new InvalidDataException(
            "EntryPointが空です。");
    }

    if (!string.Equals(
            Path.GetFileName(
                state.EntryPoint),
            state.EntryPoint,
            StringComparison.Ordinal))
    {
        throw new InvalidDataException(
            "EntryPointにフォルダーを指定できません。");
    }

    return state;
}

static string ResolveCliPath(
    RunnerPaths paths,
    ActiveRuntimeState state)
{
    var runtimeDirectory =
        Path.GetFullPath(
            Path.Combine(
                paths.RuntimeRootDirectory,
                state.ActiveVersion));

    var runtimeRootPrefix =
        Path.GetFullPath(
            paths.RuntimeRootDirectory)
        .TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) +
        Path.DirectorySeparatorChar;

    var runtimeDirectoryWithSeparator =
        runtimeDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) +
        Path.DirectorySeparatorChar;

    if (!runtimeDirectoryWithSeparator.StartsWith(
            runtimeRootPrefix,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            "Runtimeフォルダーが不正です。");
    }

    var cliPath =
        Path.GetFullPath(
            Path.Combine(
                runtimeDirectory,
                state.EntryPoint));

    if (!cliPath.StartsWith(
            runtimeDirectoryWithSeparator,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            "CLIパスがRuntimeフォルダー外を指しています。");
    }

    if (!File.Exists(
            cliPath))
    {
        throw new FileNotFoundException(
            "有効なCLIが見つかりません。",
            cliPath);
    }

    return cliPath;
}

internal sealed record RunnerPaths(
    string ApplicationDataDirectory,
    string RuntimeRootDirectory,
    string ActiveRuntimeStatePath
);

internal sealed record ActiveRuntimeState(
    int SchemaVersion,
    string ActiveVersion,
    string? PreviousVersion,
    string EntryPoint,
    DateTimeOffset ActivatedAtUtc
);