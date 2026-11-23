namespace WorkingTimeChecker.Core.Application;

/// <summary>
/// 現在使用するRuntimeの状態を表します。
/// </summary>
/// <param name="SchemaVersion">
/// 状態ファイル形式のバージョンです。
/// </param>
/// <param name="ActiveVersion">
/// 現在使用するRuntimeのバージョンです。
/// </param>
/// <param name="PreviousVersion">
/// 直前に使用していたRuntimeのバージョンです。
/// </param>
/// <param name="EntryPoint">
/// 起動するCLIのファイル名です。
/// </param>
/// <param name="ActivatedAtUtc">
/// Runtimeを有効化した日時です。
/// </param>
public sealed record ActiveRuntimeState(
    int SchemaVersion,
    string ActiveVersion,
    string? PreviousVersion,
    string EntryPoint,
    DateTimeOffset ActivatedAtUtc
);