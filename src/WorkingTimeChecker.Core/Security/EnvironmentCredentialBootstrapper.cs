namespace WorkingTimeChecker.Core.Security;

/// <summary>
/// CLI起動時の資格情報読込と
/// .envからの初回移行を管理します。
/// </summary>
public sealed class EnvironmentCredentialBootstrapper
{
    private readonly EnvironmentCredentialProvider
        _provider;

    private readonly EnvironmentCredentialMigrationService
        _migrationService;

    /// <summary>
    /// 資格情報初期化処理を作成します。
    /// </summary>
    public EnvironmentCredentialBootstrapper()
        : this(
            new EnvironmentCredentialProvider(),
            new EnvironmentCredentialMigrationService())
    {
    }

    internal EnvironmentCredentialBootstrapper(
        EnvironmentCredentialProvider provider,
        EnvironmentCredentialMigrationService migrationService)
    {
        ArgumentNullException.ThrowIfNull(
            provider);

        ArgumentNullException.ThrowIfNull(
            migrationService);

        _provider =
            provider;

        _migrationService =
            migrationService;
    }

    /// <summary>
    /// 資格情報をプロセス環境変数へ設定します。
    /// 未移行の場合は.envから初回移行します。
    /// </summary>
    /// <param name="envFilePath">
    /// 移行元として使用できる.envのパスです。
    /// </param>
    /// <param name="writeLog">
    /// 状態を出力する処理です。
    /// </param>
    /// <returns>
    /// 環境変数へ設定した件数です。
    /// </returns>
    public int LoadOrMigrate(
        string? envFilePath,
        Action<string>? writeLog = null)
    {
        writeLog ??=
            _ => { };

        if (_provider.IsComplete())
        {
            var loadedCount =
                _provider.LoadIntoCurrentProcess();

            writeLog(
                "[OK] Windows資格情報マネージャーから" +
                $"{loadedCount}項目を読み込みました。");

            return loadedCount;
        }

        var missingNames =
            _provider.GetMissingNames();

        writeLog(
            "[INFO] Windows資格情報マネージャーの" +
            "不足項目を.envから移行します。");

        writeLog(
            "[INFO] 移行対象: " +
            string.Join(
                ", ",
                missingNames));

        if (string.IsNullOrWhiteSpace(
                envFilePath) ||
            !File.Exists(
                envFilePath))
        {
            throw new InvalidOperationException(
                "Windows資格情報マネージャーに" +
                "不足項目があり、移行元.envもありません。" +
                Environment.NewLine +
                "不足項目: " +
                string.Join(
                    ", ",
                    missingNames));
        }

        var migrationResult =
            _migrationService.MigrateIfNeeded(
                envFilePath);

        writeLog(
            "[OK] .envからWindows資格情報マネージャーへ" +
            $"{migrationResult.MigratedCount}項目を移行しました。");

        if (migrationResult.AlreadyRegisteredCount > 0)
        {
            writeLog(
                "[OK] 既に登録済み: " +
                migrationResult.AlreadyRegisteredCount +
                "項目");
        }

        var loadedAfterMigration =
            _provider.LoadIntoCurrentProcess();

        writeLog(
            "[OK] Windows資格情報マネージャーから" +
            $"{loadedAfterMigration}項目を読み込みました。");

        writeLog(
            "[INFO] 移行元.envは自動削除していません。");

        return loadedAfterMigration;
    }
}