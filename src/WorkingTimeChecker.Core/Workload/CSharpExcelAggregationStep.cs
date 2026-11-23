using System.Text.RegularExpressions;

namespace WorkingTimeChecker.Core.Workload;

/// <summary>
/// ExcelやVBAを起動せず、Open XML SDKで工数を集計します。
/// </summary>
public sealed class CSharpExcelAggregationStep
{
    private const string AggregatePathSetting =
        "workload_aggregate_file";

    private const string TargetMonthSetting =
        "target_month";

    private readonly WorkloadAggregationRunner
        _aggregationRunner;

    private readonly AggregationReadBackValidator
        _validator;

    public CSharpExcelAggregationStep()
        : this(
            new WorkloadAggregationRunner(),
            new AggregationReadBackValidator()
        )
    {
    }

    internal CSharpExcelAggregationStep(
        WorkloadAggregationRunner aggregationRunner,
        AggregationReadBackValidator validator)
    {
        _aggregationRunner =
            aggregationRunner;

        _validator =
            validator;
    }

    public Task<int> RunAsync(
        string configPath,
        Action<string> writeLog,
        CancellationToken cancellationToken,
        bool validateOnly)
    {
        ArgumentNullException.ThrowIfNull(writeLog);

        cancellationToken.ThrowIfCancellationRequested();

        var excelPath =
            ResolveAggregateWorkbookPath(
                configPath
            );

        writeLog(
            "[OK] 対象 Excel: " +
            excelPath
        );

        if (!File.Exists(excelPath))
        {
            throw new FileNotFoundException(
                "工数集計ブックが見つかりません。",
                excelPath
            );
        }

        // 設定シートと名前付きの月度シートを
        // 読み取れることまで確認します。
        var settings =
            new AggregationSettingsReader()
                .Read(excelPath);

        writeLog(
            $"[OK] 対象年月: " +
            $"{settings.Year}/" +
            $"{settings.Month:00}"
        );

        writeLog(
            $"[OK] 月度シート: " +
            settings.MonthlySheetName
        );

        writeLog(
            $"[OK] 設定メンバー数: " +
            settings.Members.Count
        );

        if (validateOnly)
        {
            writeLog(
                "[OK] C#工数集計の設定検証が完了しました"
            );

            writeLog(
                "[OK] ConfigPath: " +
                configPath
            );

            writeLog(
                "[OK] ExcelPath: " +
                excelPath
            );

            return Task.FromResult(0);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var excelDirectory =
            Path.GetDirectoryName(excelPath)
            ?? throw new InvalidOperationException(
                "工数集計ブックのフォルダーを取得できません。"
            );

        var excelFileNameWithoutExtension =
            Path.GetFileNameWithoutExtension(
                excelPath
            );

        var excelExtension =
            Path.GetExtension(excelPath);

        var temporaryPath = Path.Combine(
            excelDirectory,
            excelFileNameWithoutExtension +
            ".csharp-temp-" +
            Guid.NewGuid().ToString("N") +
            excelExtension
        );

        try
        {
            writeLog(
                ">> C#で工数集計を開始します"
            );

            var aggregationResult =
                _aggregationRunner.Run(
                    excelPath,
                    temporaryPath,
                    writeLog
                );

            cancellationToken.ThrowIfCancellationRequested();

            writeLog(
                ">> 集計結果を読み戻して検証します"
            );

            var validationResult =
                _validator.Validate(
                    excelPath,
                    temporaryPath,
                    writeLog
                );

            if (!validationResult.IsSuccess)
            {
                throw new InvalidDataException(
                    "C#集計結果の読み戻し検証に失敗しました。" +
                    $" 工数行=" +
                    $"{validationResult.ActualWorkRowCount}/" +
                    $"{validationResult.ExpectedWorkRowCount}," +
                    $" 残業行=" +
                    $"{validationResult.ActualOvertimeRowCount}/" +
                    $"{validationResult.ExpectedOvertimeRowCount}," +
                    $" 不一致セル=" +
                    validationResult.DifferenceCount
                );
            }

            if (!validationResult.HasVbaProject)
            {
                throw new InvalidDataException(
                    "一時ブックからVBAプロジェクトが失われています。"
                );
            }

            cancellationToken.ThrowIfCancellationRequested();

            writeLog(
                "[OK] 読み戻し検証完了: " +
                $"工数={validationResult.ActualWorkRowCount}行, " +
                $"残業={validationResult.ActualOvertimeRowCount}行, " +
                "不一致=0"
            );

            writeLog(
                ">> 検証済みファイルで本番ブックを置換します"
            );

            // 一時ファイルは本番ブックと同じフォルダーに
            // 作成しています。同一ドライブ内で置換します。
            File.Move(
                temporaryPath,
                excelPath,
                overwrite: true
            );

            writeLog(
                "[OK] C#工数集計が完了しました"
            );

            writeLog(
                $"[OK] 処理メンバー数: " +
                aggregationResult.SuccessMemberCount
            );

            writeLog(
                $"[OK] 工数一覧表: " +
                aggregationResult.WorkRowCount +
                "行"
            );

            writeLog(
                $"[OK] 残業・社内工数時間: " +
                aggregationResult.OvertimeRowCount +
                "行"
            );

            return Task.FromResult(0);
        }
        finally
        {
            // 失敗した場合、本番ブックは変更されません。
            // 残った一時ファイルだけを削除します。
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(
                        temporaryPath
                    );
                }
                catch (IOException exception)
                {
                    writeLog(
                        "[WARN] 一時ファイルを削除できませんでした: " +
                        exception.Message
                    );
                }
                catch (UnauthorizedAccessException exception)
                {
                    writeLog(
                        "[WARN] 一時ファイルを削除できませんでした: " +
                        exception.Message
                    );
                }
            }
        }
    }

    private static string ResolveAggregateWorkbookPath(
        string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException(
                "config.yamlのパスが未指定です。",
                nameof(configPath)
            );
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                "config.yamlが見つかりません。",
                configPath
            );
        }

        var lines =
            File.ReadAllLines(configPath);

        var targetMonthValue =
            FindYamlValue(
                lines,
                TargetMonthSetting
            );

        var aggregatePathTemplate =
            FindYamlValue(
                lines,
                AggregatePathSetting
            );

        if (string.IsNullOrWhiteSpace(
                aggregatePathTemplate))
        {
            throw new InvalidDataException(
                "config.yamlにpaths." +
                AggregatePathSetting +
                "がありません。"
            );
        }

        var targetMonth =
            ResolveTargetMonth(
                targetMonthValue
            );

        var expandedPath =
            aggregatePathTemplate
                .Replace(
                    "{YYYY}",
                    targetMonth.Year.ToString("0000"),
                    StringComparison.Ordinal
                )
                .Replace(
                    "{YY}",
                    (targetMonth.Year % 100)
                        .ToString("00"),
                    StringComparison.Ordinal
                )
                .Replace(
                    "{MM}",
                    targetMonth.Month.ToString("00"),
                    StringComparison.Ordinal
                );

        // config.yamlでは//server/share形式を使用しています。
        // WindowsのUNC形式へ統一します。
        if (expandedPath.StartsWith(
                "//",
                StringComparison.Ordinal))
        {
            expandedPath =
                @"\\" +
                expandedPath[2..]
                    .Replace('/', '\\');
        }
        else
        {
            expandedPath =
                expandedPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar
                );
        }

        return expandedPath;
    }

    private static DateTime ResolveTargetMonth(
        string? settingValue)
    {
        if (string.IsNullOrWhiteSpace(settingValue) ||
            string.Equals(
                settingValue,
                "auto",
                StringComparison.OrdinalIgnoreCase))
        {
            var now =
                DateTime.Now;

            return new DateTime(
                now.Year,
                now.Month,
                1
            );
        }

        if (DateTime.TryParseExact(
                settingValue,
                "yyyy-MM",
                System.Globalization
                    .CultureInfo
                    .InvariantCulture,
                System.Globalization
                    .DateTimeStyles
                    .None,
                out var parsedMonth))
        {
            return parsedMonth;
        }

        throw new InvalidDataException(
            "system.target_monthは" +
            "YYYY-MMまたはautoで指定してください: " +
            settingValue
        );
    }

    private static string? FindYamlValue(
        IReadOnlyList<string> lines,
        string key)
    {
        var pattern =
            "^\\s*" +
            Regex.Escape(key) +
            "\\s*:\\s*(.*?)\\s*$";

        foreach (var sourceLine in lines)
        {
            var line =
                RemoveYamlComment(
                    sourceLine
                );

            var match =
                Regex.Match(
                    line,
                    pattern
                );

            if (!match.Success)
            {
                continue;
            }

            return UnquoteYamlValue(
                match.Groups[1].Value.Trim()
            );
        }

        return null;
    }

    private static string RemoveYamlComment(
        string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = 0;
             index < line.Length;
             index++)
        {
            var character =
                line[index];

            if (character == '\'' &&
                !inDoubleQuote)
            {
                inSingleQuote =
                    !inSingleQuote;

                continue;
            }

            if (character == '"' &&
                !inSingleQuote)
            {
                inDoubleQuote =
                    !inDoubleQuote;

                continue;
            }

            if (character == '#' &&
                !inSingleQuote &&
                !inDoubleQuote)
            {
                return line[..index];
            }
        }

        return line;
    }

    private static string UnquoteYamlValue(
        string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' &&
                 value[^1] == '"') ||
                (value[0] == '\'' &&
                 value[^1] == '\''))
            {
                return value[1..^1];
            }
        }

        return value;
    }
}