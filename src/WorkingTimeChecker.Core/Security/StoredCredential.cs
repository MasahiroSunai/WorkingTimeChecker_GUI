namespace WorkingTimeChecker.Core.Security;

/// <summary>
/// Windows資格情報マネージャーから取得した資格情報です。
/// </summary>
/// <param name="TargetName">
/// 資格情報の識別名です。
/// </param>
/// <param name="UserName">
/// ユーザー名です。
/// </param>
/// <param name="Secret">
/// パスワードまたはトークンです。
/// </param>
public sealed record StoredCredential(
    string TargetName,
    string UserName,
    string Secret
);