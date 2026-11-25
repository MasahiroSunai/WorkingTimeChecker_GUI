namespace WorkingTimeChecker.Core.Configuration;

/// <summary>
/// 設定ファイルのバックアップを管理します。
/// </summary>
public sealed class ConfigBackupService
{
    private const int DefaultMaximumBackupCount =
        30;

    private readonly int _maximumBackupCount;

    /// <summary>
    /// バックアップ管理サービスを作成します。
    /// </summary>
    /// <param name="maximumBackupCount">
    /// 保持するバックアップの最大件数です。
    /// </param>
    public ConfigBackupService(
        int maximumBackupCount =
            DefaultMaximumBackupCount)
    {
        if (maximumBackupCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBackupCount),
                "バックアップ保持件数は1以上にしてください。");
        }

        _maximumBackupCount =
            maximumBackupCount;
    }

    /// <summary>
    /// 設定ファイルのバックアップを作成します。
    /// </summary>
    /// <param name="configFilePath">
    /// バックアップする設定ファイルです。
    /// </param>
    /// <param name="backupDirectory">
    /// バックアップ先フォルダーです。
    /// </param>
    /// <returns>
    /// 作成したバックアップファイルの絶対パスです。
    /// 設定ファイルが存在しない場合はnullです。
    /// </returns>
    public string? CreateBackup(
        string configFilePath,
        string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            configFilePath);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            backupDirectory);

        if (!File.Exists(configFilePath))
        {
            return null;
        }

        Directory.CreateDirectory(
            backupDirectory);

        var timestamp =
            DateTime.Now.ToString(
                "yyyyMMdd_HHmmss_fff");

        var backupFileName =
            $"config_{timestamp}.yaml";

        var backupFilePath =
            Path.Combine(
                backupDirectory,
                backupFileName);

        File.Copy(
            configFilePath,
            backupFilePath,
            overwrite: false);

        DeleteObsoleteBackups(
            backupDirectory);

        return backupFilePath;
    }

    /// <summary>
    /// 保持件数を超えた古いバックアップを削除します。
    /// </summary>
    private void DeleteObsoleteBackups(
        string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }

        var obsoleteBackupPaths =
            Directory
                .EnumerateFiles(
                    backupDirectory,
                    "config_*.yaml",
                    SearchOption.TopDirectoryOnly)
                .Select(
                    path => new FileInfo(path))
                .OrderByDescending(
                    file => file.CreationTimeUtc)
                .ThenByDescending(
                    file => file.Name)
                .Skip(_maximumBackupCount)
                .Select(
                    file => file.FullName)
                .ToArray();

        foreach (var obsoleteBackupPath in
                 obsoleteBackupPaths)
        {
            File.Delete(
                obsoleteBackupPath);
        }
    }
}
