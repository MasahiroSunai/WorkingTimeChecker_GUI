namespace WorkingTimeChecker.Core.Application;

/// <summary>
/// LocalAppData内にアプリケーションの保存先を準備します。
/// </summary>
public static class ApplicationDataInitializer
{
    /// <summary>
    /// アプリケーションが使用するフォルダーを作成します。
    /// </summary>
    public static void CreateDirectories()
    {
        var directories =
            new[]
            {
                ApplicationPaths.ApplicationDataDirectory,
                ApplicationPaths.ConfigDirectory,
                ApplicationPaths.ConfigBackupDirectory,
                ApplicationPaths.LogDirectory,
                ApplicationPaths.RuntimeDirectory,
                ApplicationPaths.StateDirectory,
                ApplicationPaths.TemporaryDirectory,
            };

        foreach (var directory in directories)
        {
            Directory.CreateDirectory(
                directory);
        }
    }

    /// <summary>
    /// アプリケーション保存先を初期化し、
    /// 設定ファイルがない場合だけ初期設定をコピーします。
    /// </summary>
    /// <param name="defaultConfigFilePath">
    /// 初期設定としてコピーするconfig.yamlの絶対パスです。
    /// </param>
    /// <returns>
    /// 初期設定をコピーした場合はtrueです。
    /// 既存設定を使用した場合はfalseです。
    /// </returns>
    public static bool Initialize(
        string defaultConfigFilePath)
    {
        CreateDirectories();

        return CopyDefaultConfigIfMissing(
            defaultConfigFilePath);
    }

    /// <summary>
    /// LocalAppDataに設定ファイルがない場合だけ、
    /// 指定された初期設定をコピーします。
    /// </summary>
    /// <param name="sourceFilePath">
    /// コピー元となるconfig.yamlの絶対パスです。
    /// </param>
    /// <returns>
    /// 設定ファイルをコピーした場合はtrueです。
    /// 既存設定があった場合はfalseです。
    /// </returns>
    public static bool CopyDefaultConfigIfMissing(
        string sourceFilePath)
    {
        if (File.Exists(
                ApplicationPaths.ConfigFilePath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(
                sourceFilePath))
        {
            throw new ArgumentException(
                "初期設定ファイルが指定されていません。",
                nameof(sourceFilePath));
        }

        var normalizedSourceFilePath =
            Path.GetFullPath(
                sourceFilePath);

        if (!File.Exists(
                normalizedSourceFilePath))
        {
            throw new FileNotFoundException(
                "初期設定ファイルが見つかりません。",
                normalizedSourceFilePath);
        }

        Directory.CreateDirectory(
            ApplicationPaths.ConfigDirectory);

        var temporaryFilePath =
            ApplicationPaths.ConfigFilePath +
            ".tmp";

        try
        {
            File.Copy(
                normalizedSourceFilePath,
                temporaryFilePath,
                overwrite: true);

            ValidateCopiedFile(
                normalizedSourceFilePath,
                temporaryFilePath);

            File.Move(
                temporaryFilePath,
                ApplicationPaths.ConfigFilePath,
                overwrite: false);

            return true;
        }
        finally
        {
            if (File.Exists(
                    temporaryFilePath))
            {
                File.Delete(
                    temporaryFilePath);
            }
        }
    }

    /// <summary>
    /// コピー元とコピー後のファイルサイズが一致することを確認します。
    /// </summary>
    /// <param name="sourceFilePath">
    /// コピー元ファイルです。
    /// </param>
    /// <param name="copiedFilePath">
    /// コピー後の一時ファイルです。
    /// </param>
    private static void ValidateCopiedFile(
        string sourceFilePath,
        string copiedFilePath)
    {
        var sourceFileInfo =
            new FileInfo(
                sourceFilePath);

        var copiedFileInfo =
            new FileInfo(
                copiedFilePath);

        if (sourceFileInfo.Length == 0)
        {
            throw new InvalidOperationException(
                "初期設定ファイルが空です。" +
                Environment.NewLine +
                $"設定ファイル: {sourceFilePath}");
        }

        if (sourceFileInfo.Length !=
            copiedFileInfo.Length)
        {
            throw new IOException(
                "設定ファイルを正常にコピーできませんでした。" +
                Environment.NewLine +
                $"コピー元: {sourceFilePath}" +
                Environment.NewLine +
                $"コピー先: {copiedFilePath}");
        }
    }
}