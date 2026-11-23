namespace WorkingTimeChecker.Core.Application;

/// <summary>
/// GUIへ同梱するRuntimeの情報を表します。
/// </summary>
/// <param name="SchemaVersion">
/// マニフェスト形式のバージョンです。
/// </param>
/// <param name="RuntimeVersion">
/// Runtimeのバージョンです。
/// </param>
/// <param name="EntryPoint">
/// 起動するCLIのファイル名です。
/// </param>
/// <param name="ArchiveFileName">
/// Runtime ZIPのファイル名です。
/// </param>
/// <param name="ArchiveSha256">
/// Runtime ZIPのSHA-256です。
/// </param>
/// <param name="CreatedAtUtc">
/// Runtimeを作成した日時です。
/// </param>
public sealed record RuntimeManifest(
    int SchemaVersion,
    string RuntimeVersion,
    string EntryPoint,
    string ArchiveFileName,
    string ArchiveSha256,
    DateTimeOffset CreatedAtUtc
);