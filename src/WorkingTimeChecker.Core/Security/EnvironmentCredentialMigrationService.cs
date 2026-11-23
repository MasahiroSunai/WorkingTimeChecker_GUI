using System.Security.Cryptography;
using System.Text;

namespace WorkingTimeChecker.Core.Security;

/// <summary>
/// .envの認証情報をWindows資格情報マネージャーへ
/// 安全に移行します。
/// </summary>
public sealed class EnvironmentCredentialMigrationService
{
    private readonly WindowsCredentialService
        _credentialService;

    /// <summary>
    /// 移行サービスを作成します。
    /// </summary>
    public EnvironmentCredentialMigrationService()
        : this(
            new WindowsCredentialService())
    {
    }

    internal EnvironmentCredentialMigrationService(
        WindowsCredentialService credentialService)
    {
        ArgumentNullException.ThrowIfNull(
            credentialService);

        _credentialService =
            credentialService;
    }

    /// <summary>
    /// .envから不足している資格情報を移行します。
    /// 既に登録されている項目は上書きしません。
    /// </summary>
    /// <param name="envFilePath">
    /// 移行元.envのパスです。
    /// </param>
    /// <returns>
    /// 移行結果です。
    /// </returns>
    public CredentialMigrationResult MigrateIfNeeded(
        string envFilePath)
    {
        ValidateEnvFile(
            envFilePath);

        var envValues =
            ReadRequiredValues(
                envFilePath);

        ValidateRequiredValues(
            envValues);

        var migratedNames =
            new List<string>();

        var alreadyRegisteredNames =
            new List<string>();

        foreach (var environmentVariableName in
                 CredentialNames
                     .RequiredEnvironmentVariableNames)
        {
            var targetName =
                CredentialNames.GetTargetName(
                    environmentVariableName);

            var existingCredential =
                _credentialService.Get(
                    targetName);

            if (existingCredential is not null &&
                !string.IsNullOrEmpty(
                    existingCredential.Secret))
            {
                alreadyRegisteredNames.Add(
                    environmentVariableName);

                continue;
            }

            var value =
                envValues[
                    environmentVariableName];

            _credentialService.Save(
                targetName,
                EnvironmentCredentialProvider
                    .GetCredentialUserName(),
                value);

            VerifySavedCredential(
                targetName,
                environmentVariableName,
                value);

            migratedNames.Add(
                environmentVariableName);
        }

        var provider =
            new EnvironmentCredentialProvider(
                _credentialService);

        var missingNames =
            provider.GetMissingNames();

        if (missingNames.Count > 0)
        {
            throw new InvalidOperationException(
                "資格情報の移行後も不足項目があります。" +
                Environment.NewLine +
                "不足項目: " +
                string.Join(
                    ", ",
                    missingNames));
        }

        return new CredentialMigrationResult(
            SourceFilePath:
                Path.GetFullPath(
                    envFilePath),
            MigratedNames:
                migratedNames,
            AlreadyRegisteredNames:
                alreadyRegisteredNames);
    }

    private static Dictionary<string, string>
        ReadRequiredValues(
            string envFilePath)
    {
        var values =
            new Dictionary<string, string>(
                StringComparer.Ordinal);

        foreach (var originalLine in
                 File.ReadLines(
                     envFilePath,
                     Encoding.UTF8))
        {
            var line =
                originalLine.Trim();

            if (string.IsNullOrWhiteSpace(
                    line) ||
                line.StartsWith(
                    "#",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var separatorIndex =
                line.IndexOf(
                    '=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key =
                line[..separatorIndex]
                    .Trim();

            if (!CredentialNames
                    .IsRequiredEnvironmentVariable(
                        key))
            {
                continue;
            }

            var value =
                line[(separatorIndex + 1)..]
                    .Trim();

            value =
                RemoveMatchingQuotes(
                    value);

            values[key] =
                value;
        }

        return values;
    }

    private static string RemoveMatchingQuotes(
        string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var firstCharacter =
            value[0];

        var lastCharacter =
            value[^1];

        if ((firstCharacter == '"' &&
             lastCharacter == '"') ||
            (firstCharacter == '\'' &&
             lastCharacter == '\''))
        {
            return value[1..^1];
        }

        return value;
    }

    private static void ValidateRequiredValues(
        IReadOnlyDictionary<string, string> values)
    {
        var missingNames =
            CredentialNames
                .RequiredEnvironmentVariableNames
                .Where(
                    name =>
                        !values.TryGetValue(
                            name,
                            out var value) ||
                        string.IsNullOrEmpty(
                            value))
                .ToArray();

        if (missingNames.Length > 0)
        {
            throw new InvalidDataException(
                ".envに必要な項目がありません。" +
                Environment.NewLine +
                "不足項目: " +
                string.Join(
                    ", ",
                    missingNames));
        }
    }

    private void VerifySavedCredential(
        string targetName,
        string environmentVariableName,
        string expectedValue)
    {
        var storedCredential =
            _credentialService.Get(
                targetName);

        if (storedCredential is null)
        {
            throw new InvalidOperationException(
                "保存した資格情報を取得できません: " +
                environmentVariableName);
        }

        if (!FixedTimeEquals(
                storedCredential.Secret,
                expectedValue))
        {
            _credentialService.Remove(
                targetName);

            throw new InvalidOperationException(
                "保存した資格情報の照合に失敗しました: " +
                environmentVariableName);
        }
    }

    private static bool FixedTimeEquals(
        string actual,
        string expected)
    {
        var actualBytes =
            Encoding.UTF8.GetBytes(
                actual);

        var expectedBytes =
            Encoding.UTF8.GetBytes(
                expected);

        try
        {
            return
                actualBytes.Length ==
                    expectedBytes.Length &&
                CryptographicOperations
                    .FixedTimeEquals(
                        actualBytes,
                        expectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                actualBytes);

            CryptographicOperations.ZeroMemory(
                expectedBytes);
        }
    }

    private static void ValidateEnvFile(
        string envFilePath)
    {
        if (string.IsNullOrWhiteSpace(
                envFilePath))
        {
            throw new ArgumentException(
                ".envのパスが空です。",
                nameof(envFilePath));
        }

        if (!File.Exists(
                envFilePath))
        {
            throw new FileNotFoundException(
                ".envが見つかりません。",
                envFilePath);
        }

        if (new FileInfo(
                envFilePath).Length == 0)
        {
            throw new InvalidDataException(
                ".envが空です。");
        }
    }
}