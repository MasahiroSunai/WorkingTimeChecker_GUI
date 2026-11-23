namespace WorkingTimeChecker.Core;

/// <summary>
/// CLIが使用する実行時パスを管理します。
/// </summary>
public static class ApplicationPaths
{
    private static string? _configPathOverride;
    private static string? _logDirectoryOverride;
    private static string? _envFilePathOverride;

    /// <summary>
    /// CLI本体と従来設定が配置されている基準フォルダーです。
    /// </summary>
    public static string ApplicationDirectory =>
        FindApplicationDirectory();

    /// <summary>
    /// 従来の設定フォルダーです。
    /// </summary>
    public static string ConfigDirectory =>
        Path.Combine(
            ApplicationDirectory,
            "config");

    /// <summary>
    /// 実行時に使用するconfig.yamlです。
    /// --configが指定された場合は指定値を使用します。
    /// </summary>
    public static string ConfigPath =>
        _configPathOverride
        ?? Path.Combine(
            ConfigDirectory,
            "config.yaml");

    /// <summary>
    /// 実行時に使用する.envです。
    /// --envが指定された場合は指定値を使用します。
    /// </summary>
    public static string EnvFilePath =>
        _envFilePathOverride
        ?? Path.Combine(
            ConfigDirectory,
            ".env");

    /// <summary>
    /// 実行時に使用するログフォルダーです。
    /// --log-directoryが指定された場合は指定値を使用します。
    /// </summary>
    public static string LogDirectory =>
        _logDirectoryOverride
        ?? Path.Combine(
            ApplicationDirectory,
            "logs");

    /// <summary>
    /// コマンドライン引数で指定された実行時パスを反映します。
    /// </summary>
    /// <param name="configPath">
    /// 使用するconfig.yamlです。
    /// nullの場合は従来の配置を使用します。
    /// </param>
    /// <param name="logDirectory">
    /// ログ出力先です。
    /// nullの場合は従来の配置を使用します。
    /// </param>
    /// <param name="envFilePath">
    /// 使用する.envです。
    /// nullの場合は従来の配置を使用します。
    /// </param>
    public static void Configure(
        string? configPath,
        string? logDirectory,
        string? envFilePath)
    {
        _configPathOverride =
            NormalizeFilePath(
                configPath);

        _logDirectoryOverride =
            NormalizeDirectoryPath(
                logDirectory);

        _envFilePathOverride =
            NormalizeFilePath(
                envFilePath);
    }

    private static string? NormalizeFilePath(
        string? filePath)
    {
        if (string.IsNullOrWhiteSpace(
                filePath))
        {
            return null;
        }

        return Path.GetFullPath(
            filePath.Trim());
    }

    private static string? NormalizeDirectoryPath(
        string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(
                directoryPath))
        {
            return null;
        }

        return Path.GetFullPath(
            directoryPath.Trim());
    }

    private static string FindApplicationDirectory()
    {
        var executableDirectory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        var foundDirectory =
            FindConfigDirectory(
                executableDirectory);

        if (foundDirectory is not null)
        {
            return foundDirectory;
        }

        var currentDirectory =
            new DirectoryInfo(
                Directory.GetCurrentDirectory());

        foundDirectory =
            FindConfigDirectory(
                currentDirectory);

        if (foundDirectory is not null)
        {
            return foundDirectory;
        }

        throw new DirectoryNotFoundException(
            "WorkingTimeCheckerの設定フォルダーが" +
            "見つかりません。" +
            Environment.NewLine +
            "config.yamlを--configで指定するか、" +
            "exeと同じフォルダーに" +
            "config\\config.yamlを配置してください。");
    }

    private static string? FindConfigDirectory(
        DirectoryInfo? startDirectory)
    {
        var directory =
            startDirectory;

        while (directory is not null)
        {
            var configPath =
                Path.Combine(
                    directory.FullName,
                    "config",
                    "config.yaml");

            if (File.Exists(configPath))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        return null;
    }
}