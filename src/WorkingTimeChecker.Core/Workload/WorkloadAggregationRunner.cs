namespace WorkingTimeChecker.Core.Workload;

/// <summary>
/// 個人工数表を読み取り、集計ブックへ出力する
/// 一連の処理を管理します。
/// </summary>
public sealed class WorkloadAggregationRunner
{
    private readonly AggregationSettingsReader
        _settingsReader;

    private readonly MemberWorkbookReader
        _memberReader;

    private readonly AggregationWorkbookWriter
        _workbookWriter;

    public WorkloadAggregationRunner()
        : this(
            new AggregationSettingsReader(),
            new MemberWorkbookReader(),
            new AggregationWorkbookWriter()
        )
    {
    }

    internal WorkloadAggregationRunner(
        AggregationSettingsReader settingsReader,
        MemberWorkbookReader memberReader,
        AggregationWorkbookWriter workbookWriter)
    {
        _settingsReader =
            settingsReader;

        _memberReader =
            memberReader;

        _workbookWriter =
            workbookWriter;
    }

    public WorkloadAggregationResult Run(
        string sourceWorkbookPath,
        string destinationWorkbookPath,
        Action<string>? writeLog = null)
    {
        writeLog ??=
            _ => { };

        writeLog(
            "C#工数集計処理を開始します。"
        );

        var settings =
            _settingsReader.Read(
                sourceWorkbookPath
            );

        writeLog(
            $"対象年月: " +
            $"{settings.Year}/" +
            $"{settings.Month:00}"
        );

        writeLog(
            $"設定メンバー数: " +
            $"{settings.Members.Count}"
        );

        var workRows =
            new List<AggregatedWorkRow>();

        var overtimeRows =
            new List<AggregatedOvertimeRow>();

        var errors =
            new List<WorkloadAggregationError>();

        var missingFileCount = 0;
        var successMemberCount = 0;

        foreach (var member in settings.Members)
        {
            if (!File.Exists(
                    member.WorkbookPath))
            {
                missingFileCount++;

                errors.Add(
                    new WorkloadAggregationError(
                        MemberConfigRow:
                            member.RowNumber,
                        EmployeeNumber:
                            member.EmployeeNumber,
                        FullName:
                            member.FullName,
                        WorkbookPath:
                            member.WorkbookPath,
                        ErrorType:
                            "MissingFile",
                        Message:
                            "個人工数表が見つかりません。"
                    )
                );

                writeLog(
                    $"[SKIP] " +
                    $"{member.EmployeeNumber} " +
                    $"{member.FullName}: " +
                    "ファイルなし"
                );

                continue;
            }

            try
            {
                var memberData =
                    ReadMemberWithRetry(
                        member,
                        settings.MonthlySheetName,
                        writeLog
                    );

                foreach (var sourceRow
                         in memberData.WorkRows)
                {
                    workRows.Add(
                        new AggregatedWorkRow(
                            MemberConfigRow:
                                member.RowNumber,
                            EmployeeNumber:
                                member.EmployeeNumber,
                            FullName:
                                member.FullName,
                            SourceWorkbookPath:
                                member.WorkbookPath,
                            SourceRowNumber:
                                sourceRow
                                    .SourceRowNumber,
                            SourceValues:
                                sourceRow.SourceValues,
                            TypedSourceValues:
                                sourceRow
                                    .TypedSourceValues,
                            ProjectName:
                                sourceRow.ProjectName,
                            TaskName:
                                sourceRow.TaskName,
                            Note:
                                sourceRow.Note,
                            TotalHours:
                                sourceRow.TotalHours
                        )
                    );
                }

                overtimeRows.Add(
                    new AggregatedOvertimeRow(
                        MemberConfigRow:
                            member.RowNumber,
                        EmployeeNumber:
                            member.EmployeeNumber,
                        FullName:
                            member.FullName,
                        OvertimeHours:
                            memberData.OvertimeHours,
                        InternalWorkHours:
                            memberData.InternalWorkHours,
                        TravelCost:
                            memberData.TravelCost,
                        PrimaryExternalWork:
                            memberData.PrimaryExternalWork
                    )
                );

                successMemberCount++;

                writeLog(
                    $"[OK] " +
                    $"{member.EmployeeNumber} " +
                    $"{member.FullName}: " +
                    $"工数行={memberData.WorkRows.Count}"
                );
            }
            catch (Exception exception)
            {
                errors.Add(
                    new WorkloadAggregationError(
                        MemberConfigRow:
                            member.RowNumber,
                        EmployeeNumber:
                            member.EmployeeNumber,
                        FullName:
                            member.FullName,
                        WorkbookPath:
                            member.WorkbookPath,
                        ErrorType:
                            exception.GetType().Name,
                        Message:
                            exception.ToString()
                    )
                );

                writeLog(
                    $"[NG] " +
                    $"{member.EmployeeNumber} " +
                    $"{member.FullName}: " +
                    exception.Message
                );
            }
        }

        var data =
            new WorkloadAggregationData(
                Year:
                    settings.Year,
                Month:
                    settings.Month,
                MonthlySheetName:
                    settings.MonthlySheetName,
                WorkRows:
                    workRows,
                OvertimeRows:
                    overtimeRows
            );

        if (errors.Count > 0)
        {
            throw new WorkloadAggregationException(
                message:
                    "個人工数表の読み取りでエラーが発生しました。",
                errors:
                    errors
            );
        }

        writeLog(
            $"工数一覧出力行数: " +
            $"{workRows.Count}"
        );

        writeLog(
            $"残業集計出力行数: " +
            $"{overtimeRows.Count}"
        );

        _workbookWriter.Write(
            sourceWorkbookPath,
            destinationWorkbookPath,
            data
        );

        writeLog(
            "[OK] C#工数集計ブックを作成しました: " +
            destinationWorkbookPath
        );

        return new WorkloadAggregationResult(
            SourceWorkbookPath:
                sourceWorkbookPath,
            DestinationWorkbookPath:
                destinationWorkbookPath,
            Year:
                settings.Year,
            Month:
                settings.Month,
            ConfiguredMemberCount:
                settings.Members.Count,
            SuccessMemberCount:
                successMemberCount,
            MissingFileCount:
                missingFileCount,
            ErrorCount:
                errors.Count,
            WorkRowCount:
                workRows.Count,
            OvertimeRowCount:
                overtimeRows.Count
        );
    }

    private MemberWorkbookData ReadMemberWithRetry(
        AggregationMember member,
        string monthlySheetName,
        Action<string> writeLog)
    {
        const int maximumAttempts = 3;
        const int retryDelayMilliseconds = 2000;

        Exception? lastException =
            null;

        for (var attempt = 1;
             attempt <= maximumAttempts;
             attempt++)
        {
            try
            {
                return _memberReader.Read(
                    member.WorkbookPath,
                    monthlySheetName
                );
            }
            catch (IOException exception)
            {
                lastException =
                    exception;
            }
            catch (InvalidDataException exception)
            {
                lastException =
                    exception;
            }

            if (attempt >= maximumAttempts)
            {
                break;
            }

            writeLog(
                $"[WARN] 再試行します: " +
                $"{member.FullName}, " +
                $"{attempt}/{maximumAttempts}"
            );

            Thread.Sleep(
                retryDelayMilliseconds
            );
        }

        throw new IOException(
            "個人工数表を3回試行しても" +
            "読み取れませんでした: " +
            member.WorkbookPath,
            lastException
        );
    }
}

public sealed record WorkloadAggregationData(
    int Year,
    int Month,
    string MonthlySheetName,
    IReadOnlyList<AggregatedWorkRow> WorkRows,
    IReadOnlyList<AggregatedOvertimeRow> OvertimeRows
);

public sealed record AggregatedWorkRow(
    int MemberConfigRow,
    string EmployeeNumber,
    string FullName,
    string SourceWorkbookPath,
    int SourceRowNumber,
    IReadOnlyList<string> SourceValues,
    IReadOnlyList<ExcelCellValue> TypedSourceValues,
    string ProjectName,
    string TaskName,
    string Note,
    double TotalHours
);

public sealed record AggregatedOvertimeRow(
    int MemberConfigRow,
    string EmployeeNumber,
    string FullName,
    double OvertimeHours,
    double InternalWorkHours,
    double TravelCost,
    string PrimaryExternalWork
);

public sealed record WorkloadAggregationResult(
    string SourceWorkbookPath,
    string DestinationWorkbookPath,
    int Year,
    int Month,
    int ConfiguredMemberCount,
    int SuccessMemberCount,
    int MissingFileCount,
    int ErrorCount,
    int WorkRowCount,
    int OvertimeRowCount
);

public sealed record WorkloadAggregationError(
    int MemberConfigRow,
    string EmployeeNumber,
    string FullName,
    string WorkbookPath,
    string ErrorType,
    string Message
);

public sealed class WorkloadAggregationException
    : Exception
{
    public WorkloadAggregationException(
        string message,
        IReadOnlyList<WorkloadAggregationError> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyList<WorkloadAggregationError>
        Errors { get; }
}