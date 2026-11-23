namespace WorkingTimeChecker.Core.Security;

/// <summary>
/// Windows資格情報マネージャーへ登録する
/// 資格情報名を管理します。
/// </summary>
public static class CredentialNames
{
    /// <summary>
    /// WorkingTimeChecker用資格情報の接頭辞です。
    /// </summary>
    public const string Prefix =
        "PERSOL_CROSS_TECHNOLOGY/WorkingTimeChecker";

    /// <summary>
    /// 資格情報として管理する環境変数名です。
    /// </summary>
    public static IReadOnlyList<string>
        RequiredEnvironmentVariableNames { get; } =
        [
            "DNDEV_AWS_ID",
            "DNDEV_AWS_PASSWORD",
            "GENIIE_ID",
            "GENIIE_PASSWORD",
            "APPS_ID",
            "APPS_PASSWORD",
            "APPS_TOTP_SECRET",
            "ROCKETCHAT_USER_ID",
            "ROCKETCHAT_TOKEN",
        ];

    /// <summary>
    /// 環境変数名からWindows資格情報の登録名を作成します。
    /// </summary>
    /// <param name="environmentVariableName">
    /// 環境変数名です。
    /// </param>
    /// <returns>
    /// Windows資格情報の登録名です。
    /// </returns>
    public static string GetTargetName(
        string environmentVariableName)
    {
        ValidateEnvironmentVariableName(
            environmentVariableName);

        return
            Prefix +
            "/" +
            environmentVariableName;
    }

    /// <summary>
    /// 対象の環境変数名か確認します。
    /// </summary>
    public static bool IsRequiredEnvironmentVariable(
        string environmentVariableName)
    {
        return
            RequiredEnvironmentVariableNames.Contains(
                environmentVariableName,
                StringComparer.Ordinal);
    }

    private static void ValidateEnvironmentVariableName(
        string environmentVariableName)
    {
        if (string.IsNullOrWhiteSpace(
                environmentVariableName))
        {
            throw new ArgumentException(
                "環境変数名が空です。",
                nameof(environmentVariableName));
        }

        if (!IsRequiredEnvironmentVariable(
                environmentVariableName))
        {
            throw new ArgumentException(
                "WorkingTimeCheckerで管理しない" +
                "環境変数名です: " +
                environmentVariableName,
                nameof(environmentVariableName));
        }
    }
}