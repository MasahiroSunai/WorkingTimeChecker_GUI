using System.Text.Json;

namespace WorkingTimeChecker.Core.Application;

/// <summary>
/// 現在有効なRuntimeを解決します。
/// </summary>
public sealed class RuntimeResolver
{
    private static readonly JsonSerializerOptions
        JsonOptions =
            new()
            {
                PropertyNameCaseInsensitive = true,
            };

    /// <summary>
    /// 現在有効なRuntimeの状態を読み取ります。
    /// </summary>
    /// <returns>
    /// 現在有効なRuntimeの状態です。
    /// </returns>
    public ActiveRuntimeState GetActiveState()
    {
        if (!File.Exists(
                ApplicationPaths.ActiveRuntimeFilePath))
        {
            throw new FileNotFoundException(
                "有効なRuntimeの状態ファイルがありません。",
                ApplicationPaths.ActiveRuntimeFilePath);
        }

        var json =
            File.ReadAllText(
                ApplicationPaths.ActiveRuntimeFilePath);

        var state =
            JsonSerializer.Deserialize<ActiveRuntimeState>(
                json,
                JsonOptions);

        if (state is null)
        {
            throw new InvalidDataException(
                "有効なRuntimeの状態を読み取れません。");
        }

        ValidateState(state);

        return state;
    }

    /// <summary>
    /// 現在有効なCLIの絶対パスを取得します。
    /// </summary>
    /// <returns>
    /// CLIの絶対パスです。
    /// </returns>
    public string GetActiveCliPath()
    {
        var state =
            GetActiveState();

        var runtimeDirectory =
            GetRuntimeDirectory(
                state.ActiveVersion);

        var cliPath =
            Path.GetFullPath(
                Path.Combine(
                    runtimeDirectory,
                    state.EntryPoint));

        var runtimePrefix =
            runtimeDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        if (!cliPath.StartsWith(
                runtimePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "RuntimeのCLIパスが不正です。");
        }

        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException(
                "有効なCLIが見つかりません。",
                cliPath);
        }

        return cliPath;
    }

    /// <summary>
    /// 指定バージョンのRuntimeフォルダーを取得します。
    /// </summary>
    /// <param name="runtimeVersion">
    /// Runtimeのバージョンです。
    /// </param>
    /// <returns>
    /// Runtimeフォルダーの絶対パスです。
    /// </returns>
    public static string GetRuntimeDirectory(
        string runtimeVersion)
    {
        ValidateRuntimeVersion(
            runtimeVersion);

        return Path.Combine(
            ApplicationPaths.RuntimeDirectory,
            runtimeVersion);
    }

    /// <summary>
    /// Runtimeバージョンの形式を検証します。
    /// </summary>
    /// <param name="runtimeVersion">
    /// 検証するRuntimeバージョンです。
    /// </param>
    public static void ValidateRuntimeVersion(
        string runtimeVersion)
    {
        if (string.IsNullOrWhiteSpace(
                runtimeVersion))
        {
            throw new InvalidDataException(
                "Runtimeバージョンが空です。");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                runtimeVersion,
                @"^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$"))
        {
            throw new InvalidDataException(
                "Runtimeバージョンの形式が不正です: " +
                runtimeVersion);
        }
    }

    private static void ValidateState(
        ActiveRuntimeState state)
    {
        if (state.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                "未対応のRuntime状態ファイルです。" +
                $" SchemaVersion={state.SchemaVersion}");
        }

        ValidateRuntimeVersion(
            state.ActiveVersion);

        if (!string.IsNullOrWhiteSpace(
                state.PreviousVersion))
        {
            ValidateRuntimeVersion(
                state.PreviousVersion);
        }

        if (string.IsNullOrWhiteSpace(
                state.EntryPoint))
        {
            throw new InvalidDataException(
                "RuntimeのEntryPointが空です。");
        }

        if (!string.Equals(
                Path.GetFileName(state.EntryPoint),
                state.EntryPoint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "RuntimeのEntryPointが不正です。");
        }
    }
}