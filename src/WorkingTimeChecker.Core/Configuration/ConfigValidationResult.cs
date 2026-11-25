namespace WorkingTimeChecker.Core.Configuration;

/// <summary>
/// 設定ファイルの検証結果です。
/// </summary>
/// <param name="Errors">
/// 検出されたエラーメッセージです。
/// </param>
public sealed record ConfigValidationResult(
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// エラーがなく、有効な設定かどうかを取得します。
    /// </summary>
    public bool IsValid =>
        Errors.Count == 0;
}
