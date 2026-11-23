namespace WorkingTimeChecker.Core.Security;

/// <summary>
/// .envからWindows資格情報マネージャーへの
/// 移行結果を表します。
/// </summary>
/// <param name="SourceFilePath">
/// 移行元.envのパスです。
/// </param>
/// <param name="MigratedNames">
/// 移行した環境変数名です。
/// </param>
/// <param name="AlreadyRegisteredNames">
/// 既に登録されていた環境変数名です。
/// </param>
public sealed record CredentialMigrationResult(
    string SourceFilePath,
    IReadOnlyList<string> MigratedNames,
    IReadOnlyList<string> AlreadyRegisteredNames)
{
    /// <summary>
    /// 新しく移行した件数です。
    /// </summary>
    public int MigratedCount =>
        MigratedNames.Count;

    /// <summary>
    /// 既に登録されていた件数です。
    /// </summary>
    public int AlreadyRegisteredCount =>
        AlreadyRegisteredNames.Count;
}