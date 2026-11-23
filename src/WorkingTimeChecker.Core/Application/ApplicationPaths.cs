using System.Reflection;

namespace WorkingTimeChecker.Core.Application;

/// <summary>
/// アプリケーションが使用する保存先を管理します。
/// </summary>
public static class ApplicationPaths
{
    /// <summary>
    /// Company属性を取得できない場合に使用する会社名です。
    /// </summary>
    private const string DefaultCompanyName =
        "PERSOL_CROSS_TECHNOLOGY";

    /// <summary>
    /// GUI、CLI、Runnerで共通して使用するアプリ名です。
    /// </summary>
    private const string ApplicationDirectoryName =
        "WorkingTimeChecker";

    /// <summary>
    /// 実行中のアプリケーションに登録された会社名です。
    /// </summary>
    public static string CompanyName =>
        GetCompanyName();

    /// <summary>
    /// LocalAppData内のアプリケーション保存先です。
    /// </summary>
    public static string ApplicationDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            CompanyName,
            ApplicationDirectoryName);

    /// <summary>
    /// 設定ファイルを保存するフォルダーです。
    /// </summary>
    public static string ConfigDirectory =>
        Path.Combine(
            ApplicationDataDirectory,
            "Config");

    /// <summary>
    /// アプリケーション設定ファイルです。
    /// </summary>
    public static string ConfigFilePath =>
        Path.Combine(
            ConfigDirectory,
            "config.yaml");

    /// <summary>
    /// 認証情報を保存した.envファイルです。
    /// 資格情報マネージャー移行までの暫定保存先です。
    /// </summary>
    public static string EnvFilePath =>
        Path.Combine(
            ConfigDirectory,
            ".env");

    /// <summary>
    /// 設定ファイルのバックアップ先です。
    /// </summary>
    public static string ConfigBackupDirectory =>
        Path.Combine(
            ConfigDirectory,
            "Backup");

    /// <summary>
    /// ログファイルを保存するフォルダーです。
    /// </summary>
    public static string LogDirectory =>
        Path.Combine(
            ApplicationDataDirectory,
            "Logs");

    /// <summary>
    /// CLIや内蔵Pythonを保存するフォルダーです。
    /// </summary>
    public static string RuntimeDirectory =>
        Path.Combine(
            ApplicationDataDirectory,
            "Runtime");

    /// <summary>
    /// 実行状態やバージョン情報を保存するフォルダーです。
    /// </summary>
    public static string StateDirectory =>
        Path.Combine(
            ApplicationDataDirectory,
            "State");

    /// <summary>
    /// 一時ファイルを保存するフォルダーです。
    /// </summary>
    public static string TemporaryDirectory =>
        Path.Combine(
            ApplicationDataDirectory,
            "Temp");

    /// <summary>
    /// 現在使用するRuntimeの情報ファイルです。
    /// </summary>
    public static string ActiveRuntimeFilePath =>
        Path.Combine(
            StateDirectory,
            "active-runtime.json");

    /// <summary>
    /// 最後の実行結果を保存するファイルです。
    /// </summary>
    public static string LastExecutionFilePath =>
        Path.Combine(
            StateDirectory,
            "last-execution.json");

    /// <summary>
    /// タスクスケジューラから起動するRunnerです。
    /// Runnerは後続の段階で配置します。
    /// </summary>
    public static string RunnerExecutablePath =>
        Path.Combine(
            ApplicationDataDirectory,
            "WorkingTimeChecker.Runner.exe");

    /// <summary>
    /// 実行中のアプリケーションに登録されたCompany属性を取得します。
    /// </summary>
    /// <returns>
    /// Windowsのフォルダー名として使用できる会社名です。
    /// </returns>
    private static string GetCompanyName()
    {
        var entryAssembly =
            Assembly.GetEntryAssembly();

        var companyName =
            entryAssembly?
                .GetCustomAttribute<AssemblyCompanyAttribute>()?
                .Company;

        if (string.IsNullOrWhiteSpace(companyName))
        {
            var executingAssembly =
                Assembly.GetExecutingAssembly();

            companyName =
                executingAssembly
                    .GetCustomAttribute<AssemblyCompanyAttribute>()?
                    .Company;
        }

        return GetSafeDirectoryName(
            companyName,
            DefaultCompanyName);
    }

    /// <summary>
    /// Windowsのフォルダー名として使用できる文字列へ変換します。
    /// </summary>
    /// <param name="value">
    /// フォルダー名として使用する文字列です。
    /// </param>
    /// <param name="fallbackValue">
    /// 元の文字列が空の場合に使用する代替名です。
    /// </param>
    /// <returns>
    /// 使用できない文字をアンダースコアへ置き換えた文字列です。
    /// </returns>
    private static string GetSafeDirectoryName(
        string? value,
        string fallbackValue)
    {
        var directoryName =
            string.IsNullOrWhiteSpace(value)
                ? fallbackValue
                : value.Trim();

        foreach (var invalidCharacter in
                 Path.GetInvalidFileNameChars())
        {
            directoryName =
                directoryName.Replace(
                    invalidCharacter,
                    '_');
        }

        return directoryName;
    }
}