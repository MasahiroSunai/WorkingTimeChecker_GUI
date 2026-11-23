namespace WorkingTimeChecker.Core.Application;

/// <summary>
/// Runtimeの導入結果です。
/// </summary>
public enum RuntimeInstallStatus
{
    /// <summary>
    /// Runtimeを初めてインストールしました。
    /// </summary>
    Installed,

    /// <summary>
    /// 新しいRuntimeへ更新しました。
    /// </summary>
    Updated,

    /// <summary>
    /// 導入済みのRuntimeを再利用しました。
    /// </summary>
    AlreadyInstalled,
}

/// <summary>
/// Runtimeの導入結果を表します。
/// </summary>
/// <param name="RuntimeVersion">
/// 有効になったRuntimeのバージョンです。
/// </param>
/// <param name="RuntimeDirectory">
/// Runtimeの保存先です。
/// </param>
/// <param name="CliExecutablePath">
/// CLI実行ファイルのパスです。
/// </param>
/// <param name="Status">
/// 導入結果です。
/// </param>
public sealed record RuntimeInstallResult(
    string RuntimeVersion,
    string RuntimeDirectory,
    string CliExecutablePath,
    RuntimeInstallStatus Status
);