namespace WorkingTimeChecker.Core.Security;

/// <summary>
/// Windows資格情報マネージャーから認証情報を読み込み、
/// 現在のプロセスの環境変数へ設定します。
/// </summary>
public sealed class EnvironmentCredentialProvider
{
    private const string CredentialUserName =
        "environment-variable";

    private readonly WindowsCredentialService
        _credentialService;

    /// <summary>
    /// 認証情報プロバイダーを作成します。
    /// </summary>
    public EnvironmentCredentialProvider()
        : this(
            new WindowsCredentialService())
    {
    }

    internal EnvironmentCredentialProvider(
        WindowsCredentialService credentialService)
    {
        ArgumentNullException.ThrowIfNull(
            credentialService);

        _credentialService =
            credentialService;
    }

    /// <summary>
    /// Windows資格情報マネージャーに
    /// 9項目すべて登録されているか確認します。
    /// </summary>
    public bool IsComplete()
    {
        return
            GetMissingNames().Count == 0;
    }

    /// <summary>
    /// 登録されていない環境変数名を取得します。
    /// </summary>
    public IReadOnlyList<string> GetMissingNames()
    {
        var missingNames =
            new List<string>();

        foreach (var environmentVariableName in
                 CredentialNames
                     .RequiredEnvironmentVariableNames)
        {
            var targetName =
                CredentialNames.GetTargetName(
                    environmentVariableName);

            var credential =
                _credentialService.Get(
                    targetName);

            if (credential is null ||
                string.IsNullOrEmpty(
                    credential.Secret))
            {
                missingNames.Add(
                    environmentVariableName);
            }
        }

        return missingNames;
    }

    /// <summary>
    /// 資格情報を現在のプロセスの環境変数へ設定します。
    /// </summary>
    /// <returns>
    /// 設定した環境変数の件数です。
    /// </returns>
    public int LoadIntoCurrentProcess()
    {
        var missingNames =
            GetMissingNames();

        if (missingNames.Count > 0)
        {
            throw new InvalidOperationException(
                "Windows資格情報マネージャーに" +
                "必要な認証情報が登録されていません。" +
                Environment.NewLine +
                "不足項目: " +
                string.Join(
                    ", ",
                    missingNames));
        }

        var loadedCount = 0;

        foreach (var environmentVariableName in
                 CredentialNames
                     .RequiredEnvironmentVariableNames)
        {
            var targetName =
                CredentialNames.GetTargetName(
                    environmentVariableName);

            var credential =
                _credentialService.Get(
                    targetName)
                ?? throw new InvalidOperationException(
                    "資格情報を取得できません: " +
                    environmentVariableName);

            Environment.SetEnvironmentVariable(
                environmentVariableName,
                credential.Secret,
                EnvironmentVariableTarget.Process);

            loadedCount++;
        }

        return loadedCount;
    }

    /// <summary>
    /// 資格情報保存時に使用する固定ユーザー名です。
    /// </summary>
    internal static string GetCredentialUserName()
    {
        return CredentialUserName;
    }
}