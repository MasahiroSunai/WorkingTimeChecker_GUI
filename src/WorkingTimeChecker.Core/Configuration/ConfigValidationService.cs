namespace WorkingTimeChecker.Core.Configuration;

/// <summary>
/// WorkingTimeCheckerの設定内容を検査します。
/// </summary>
public sealed class ConfigValidationService
{
    private readonly ConfigFileService
        _configFileService;

    public ConfigValidationService(
        ConfigFileService configFileService)
    {
        _configFileService =
            configFileService;
    }

    public ConfigValidationResult ValidateFile(
        string configFilePath)
    {
        var errors =
            new List<string>();

        if (!File.Exists(configFilePath))
        {
            errors.Add(
                "config.yamlが見つかりません。");

            return new ConfigValidationResult(
                errors);
        }

        ValidateRequiredValue(
            configFilePath,
            "system",
            "target_month",
            "対象年月",
            errors);

        ValidateBooleanValue(
            configFilePath,
            "system",
            "dry_run",
            "実行モード",
            errors);

        ValidateBooleanValue(
            configFilePath,
            "system",
            "send_mail",
            "メール送信設定",
            errors);

        ValidateAbsoluteUri(
            configFilePath,
            "apps",
            "base_url",
            "Web勤怠URL",
            errors);

        ValidateAbsoluteUri(
            configFilePath,
            "rocketchat",
            "url",
            "Rocket.Chat URL",
            errors);

        return new ConfigValidationResult(
            errors);
    }

    private void ValidateRequiredValue(
        string configFilePath,
        string sectionName,
        string keyName,
        string displayName,
        ICollection<string> errors)
    {
        var value =
            _configFileService.ReadScalar(
                configFilePath,
                sectionName,
                keyName);

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(
                $"{displayName}が入力されていません。");
        }
    }

    private void ValidateBooleanValue(
        string configFilePath,
        string sectionName,
        string keyName,
        string displayName,
        ICollection<string> errors)
    {
        var value =
            _configFileService.ReadScalar(
                configFilePath,
                sectionName,
                keyName);

        if (!bool.TryParse(
                value,
                out _))
        {
            errors.Add(
                $"{displayName}はtrueまたはfalseで指定してください。");
        }
    }

    private void ValidateAbsoluteUri(
        string configFilePath,
        string sectionName,
        string keyName,
        string displayName,
        ICollection<string> errors)
    {
        var value =
            _configFileService.ReadScalar(
                configFilePath,
                sectionName,
                keyName);

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(
                $"{displayName}が入力されていません。");

            return;
        }

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(
                $"{displayName}の形式が正しくありません。");
        }
    }
}
