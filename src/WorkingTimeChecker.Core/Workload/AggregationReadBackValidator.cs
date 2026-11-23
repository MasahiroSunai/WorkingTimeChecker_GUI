using System.Globalization;
using DocumentFormat.OpenXml.Packaging;

namespace WorkingTimeChecker.Core.Workload;

/// <summary>
/// C#で書き込んだ集計ブックを再度読み取り、
/// 個人工数表から計算した期待値と比較します。
/// </summary>
public sealed class AggregationReadBackValidator
{
    private const string WorkSheetName =
        "工数一覧表";

    private const string OvertimeSheetName =
        "残業・社内工数時間";

    private const uint WorkStartRow = 7;
    private const uint OvertimeStartRow = 3;

    private readonly AggregationSettingsReader
        _settingsReader;

    private readonly MemberWorkbookReader
        _memberReader;

    public AggregationReadBackValidator()
        : this(
            new AggregationSettingsReader(),
            new MemberWorkbookReader()
        )
    {
    }

    internal AggregationReadBackValidator(
        AggregationSettingsReader settingsReader,
        MemberWorkbookReader memberReader)
    {
        _settingsReader =
            settingsReader;

        _memberReader =
            memberReader;
    }

    public AggregationReadBackResult Validate(
        string sourceWorkbookPath,
        string outputWorkbookPath,
        Action<string>? writeLog = null)
    {
        writeLog ??=
            _ => { };

        if (!File.Exists(sourceWorkbookPath))
        {
            throw new FileNotFoundException(
                "コピー元の集計ブックが見つかりません。",
                sourceWorkbookPath
            );
        }

        if (!File.Exists(outputWorkbookPath))
        {
            throw new FileNotFoundException(
                "検証対象の集計ブックが見つかりません。",
                outputWorkbookPath
            );
        }

        writeLog(
            "読み戻し用の期待値を作成します。"
        );

        var settings =
            _settingsReader.Read(
                sourceWorkbookPath
            );

        var expectedWorkRows =
            new List<ExpectedAggregationWorkRow>();

        var expectedOvertimeRows =
            new List<ExpectedAggregationOvertimeRow>();

        var readErrors =
            new List<AggregationReadBackError>();

        foreach (var member in settings.Members)
        {
            if (!File.Exists(member.WorkbookPath))
            {
                readErrors.Add(
                    new AggregationReadBackError(
                        EmployeeNumber:
                            member.EmployeeNumber,
                        FullName:
                            member.FullName,
                        WorkbookPath:
                            member.WorkbookPath,
                        Message:
                            "個人工数表が見つかりません。"
                    )
                );

                continue;
            }

            try
            {
                var memberData =
                    ReadMemberWithRetry(
                        member,
                        settings.MonthlySheetName
                    );

                foreach (var sourceRow
                         in memberData.WorkRows)
                {
                    expectedWorkRows.Add(
                        new ExpectedAggregationWorkRow(
                            EmployeeNumber:
                                member.EmployeeNumber,
                            FullName:
                                member.FullName,
                            SourceWorkbookPath:
                                member.WorkbookPath,
                            SourceRowNumber:
                                sourceRow.SourceRowNumber,
                            TypedSourceValues:
                                sourceRow.TypedSourceValues,
                            TaskName:
                                sourceRow.TaskName,
                            Note:
                                sourceRow.Note
                        )
                    );
                }

                expectedOvertimeRows.Add(
                    new ExpectedAggregationOvertimeRow(
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
            }
            catch (Exception exception)
            {
                readErrors.Add(
                    new AggregationReadBackError(
                        EmployeeNumber:
                            member.EmployeeNumber,
                        FullName:
                            member.FullName,
                        WorkbookPath:
                            member.WorkbookPath,
                        Message:
                            exception.ToString()
                    )
                );
            }
        }

        if (readErrors.Count > 0)
        {
            return new AggregationReadBackResult(
                IsSuccess:
                    false,
                HasVbaProject:
                    false,
                ExpectedWorkRowCount:
                    expectedWorkRows.Count,
                ActualWorkRowCount:
                    0,
                ExpectedOvertimeRowCount:
                    expectedOvertimeRows.Count,
                ActualOvertimeRowCount:
                    0,
                DifferenceCount:
                    0,
                ReadErrors:
                    readErrors,
                Differences:
                    Array.Empty<AggregationCellDifference>()
            );
        }

        writeLog(
            $"期待する工数行数: {expectedWorkRows.Count}"
        );

        writeLog(
            $"期待する残業集計行数: {expectedOvertimeRows.Count}"
        );

        using var stream =
            new FileStream(
                outputWorkbookPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite |
                FileShare.Delete
            );

        using var document =
            SpreadsheetDocument.Open(
                stream,
                isEditable: false
            );

        var workbookPart =
            document.WorkbookPart
            ?? throw new InvalidOperationException(
                "出力ブックのWorkbookPartがありません。"
            );

        var hasVbaProject =
            workbookPart.VbaProjectPart is not null;

        var workWorksheetPart =
            ExcelCellReader.GetWorksheetPart(
                workbookPart,
                WorkSheetName
            );

        var overtimeWorksheetPart =
            ExcelCellReader.GetWorksheetPart(
                workbookPart,
                OvertimeSheetName
            );

        var actualWorkRowCount =
            CountContinuousRows(
                workbookPart,
                workWorksheetPart,
                WorkStartRow,
                keyColumn: 2
            );

        var actualOvertimeRowCount =
            CountContinuousRows(
                workbookPart,
                overtimeWorksheetPart,
                OvertimeStartRow,
                keyColumn: 2
            );

        var differences =
            new List<AggregationCellDifference>();

        CompareWorkRows(
            workbookPart,
            workWorksheetPart,
            expectedWorkRows,
            actualWorkRowCount,
            differences
        );

        CompareOvertimeRows(
            workbookPart,
            overtimeWorksheetPart,
            expectedOvertimeRows,
            actualOvertimeRowCount,
            differences
        );

        if (!hasVbaProject)
        {
            differences.Add(
                new AggregationCellDifference(
                    SheetName:
                        "Workbook",
                    RowNumber:
                        0,
                    ColumnName:
                        string.Empty,
                    EmployeeNumber:
                        string.Empty,
                    FullName:
                        string.Empty,
                    Expected:
                        "VBA project exists",
                    Actual:
                        "VBA project missing"
                )
            );
        }

        var isSuccess =
            hasVbaProject &&
            readErrors.Count == 0 &&
            expectedWorkRows.Count ==
                actualWorkRowCount &&
            expectedOvertimeRows.Count ==
                actualOvertimeRowCount &&
            differences.Count == 0;

        return new AggregationReadBackResult(
            IsSuccess:
                isSuccess,
            HasVbaProject:
                hasVbaProject,
            ExpectedWorkRowCount:
                expectedWorkRows.Count,
            ActualWorkRowCount:
                actualWorkRowCount,
            ExpectedOvertimeRowCount:
                expectedOvertimeRows.Count,
            ActualOvertimeRowCount:
                actualOvertimeRowCount,
            DifferenceCount:
                differences.Count,
            ReadErrors:
                readErrors,
            Differences:
                differences
        );
    }

    private static void CompareWorkRows(
        WorkbookPart workbookPart,
        WorksheetPart worksheetPart,
        IReadOnlyList<ExpectedAggregationWorkRow>
            expectedRows,
        int actualRowCount,
        List<AggregationCellDifference>
            differences)
    {
        var sharedRowCount =
            Math.Min(
                expectedRows.Count,
                actualRowCount
            );

        for (var index = 0;
             index < sharedRowCount;
             index++)
        {
            var rowNumber =
                WorkStartRow +
                checked((uint)index);

            var expectedRow =
                expectedRows[index];

            CompareCell(
                workbookPart,
                worksheetPart,
                WorkSheetName,
                rowNumber,
                2,
                ExcelCellValue.FromText(
                    expectedRow.EmployeeNumber
                ),
                expectedRow,
                differences
            );

            CompareCell(
                workbookPart,
                worksheetPart,
                WorkSheetName,
                rowNumber,
                3,
                ExcelCellValue.FromText(
                    expectedRow.FullName
                ),
                expectedRow,
                differences
            );

            for (var sourceIndex = 0;
                 sourceIndex <
                     expectedRow.TypedSourceValues.Count;
                 sourceIndex++)
            {
                var destinationColumn =
                    sourceIndex + 4;

                // J列とK列は、VBAと同じく
                // 次行のタスクと備考で上書きするため、
                // 元行のB～APとの比較から除外します。
                if (destinationColumn is 10 or 11)
                {
                    continue;
                }

                CompareCell(
                    workbookPart,
                    worksheetPart,
                    WorkSheetName,
                    rowNumber,
                    destinationColumn,
                    expectedRow.TypedSourceValues[
                        sourceIndex
                    ],
                    expectedRow,
                    differences
                );
            }

            CompareCell(
                workbookPart,
                worksheetPart,
                WorkSheetName,
                rowNumber,
                10,
                ExcelCellValue.FromText(
                    expectedRow.TaskName
                ),
                expectedRow,
                differences
            );

            CompareCell(
                workbookPart,
                worksheetPart,
                WorkSheetName,
                rowNumber,
                11,
                ExcelCellValue.FromText(
                    expectedRow.Note
                ),
                expectedRow,
                differences
            );
        }
    }

    private static void CompareOvertimeRows(
        WorkbookPart workbookPart,
        WorksheetPart worksheetPart,
        IReadOnlyList<
            ExpectedAggregationOvertimeRow
        > expectedRows,
        int actualRowCount,
        List<AggregationCellDifference>
            differences)
    {
        var sharedRowCount =
            Math.Min(
                expectedRows.Count,
                actualRowCount
            );

        for (var index = 0;
             index < sharedRowCount;
             index++)
        {
            var rowNumber =
                OvertimeStartRow +
                checked((uint)index);

            var expectedRow =
                expectedRows[index];

            CompareOvertimeCell(
                workbookPart,
                worksheetPart,
                rowNumber,
                2,
                ExcelCellValue.FromText(
                    expectedRow.FullName
                ),
                expectedRow,
                differences
            );

            CompareOvertimeCell(
                workbookPart,
                worksheetPart,
                rowNumber,
                3,
                ExcelCellValue.FromNumber(
                    expectedRow.OvertimeHours
                ),
                expectedRow,
                differences
            );

            CompareOvertimeCell(
                workbookPart,
                worksheetPart,
                rowNumber,
                4,
                ExcelCellValue.FromNumber(
                    expectedRow.InternalWorkHours
                ),
                expectedRow,
                differences
            );

            CompareOvertimeCell(
                workbookPart,
                worksheetPart,
                rowNumber,
                5,
                ExcelCellValue.FromNumber(
                    expectedRow.TravelCost
                ),
                expectedRow,
                differences
            );

            CompareOvertimeCell(
                workbookPart,
                worksheetPart,
                rowNumber,
                6,
                ExcelCellValue.FromText(
                    expectedRow.PrimaryExternalWork
                ),
                expectedRow,
                differences
            );
        }
    }

    private static void CompareCell(
        WorkbookPart workbookPart,
        WorksheetPart worksheetPart,
        string sheetName,
        uint rowNumber,
        int columnNumber,
        ExcelCellValue expected,
        ExpectedAggregationWorkRow expectedRow,
        List<AggregationCellDifference>
            differences)
    {
        var actual =
            ExcelCellValue.FromCell(
                workbookPart,
                ExcelCellReader.GetCell(
                    worksheetPart,
                    rowNumber,
                    columnNumber
                )
            );

        if (ValuesEqual(expected, actual))
        {
            return;
        }

        differences.Add(
            new AggregationCellDifference(
                SheetName:
                    sheetName,
                RowNumber:
                    checked((int)rowNumber),
                ColumnName:
                    ExcelCellReader.GetColumnName(
                        columnNumber
                    ),
                EmployeeNumber:
                    expectedRow.EmployeeNumber,
                FullName:
                    expectedRow.FullName,
                Expected:
                    expected.Text,
                Actual:
                    actual.Text
            )
        );
    }

    private static void CompareOvertimeCell(
        WorkbookPart workbookPart,
        WorksheetPart worksheetPart,
        uint rowNumber,
        int columnNumber,
        ExcelCellValue expected,
        ExpectedAggregationOvertimeRow
            expectedRow,
        List<AggregationCellDifference>
            differences)
    {
        var actual =
            ExcelCellValue.FromCell(
                workbookPart,
                ExcelCellReader.GetCell(
                    worksheetPart,
                    rowNumber,
                    columnNumber
                )
            );

        if (ValuesEqual(expected, actual))
        {
            return;
        }

        differences.Add(
            new AggregationCellDifference(
                SheetName:
                    OvertimeSheetName,
                RowNumber:
                    checked((int)rowNumber),
                ColumnName:
                    ExcelCellReader.GetColumnName(
                        columnNumber
                    ),
                EmployeeNumber:
                    expectedRow.EmployeeNumber,
                FullName:
                    expectedRow.FullName,
                Expected:
                    expected.Text,
                Actual:
                    actual.Text
            )
        );
    }

    private static bool ValuesEqual(
        ExcelCellValue expected,
        ExcelCellValue actual)
    {
        var expectedText =
            NormalizeText(expected.Text);

        var actualText =
            NormalizeText(actual.Text);

        if (string.Equals(
                expectedText,
                actualText,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (double.TryParse(
                expectedText,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var expectedNumber) &&
            double.TryParse(
                actualText,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var actualNumber))
        {
            return Math.Abs(
                expectedNumber - actualNumber
            ) <= 0.000001;
        }

        return false;
    }

    private static string NormalizeText(
        string? value)
    {
        return (value ?? string.Empty)
            .Replace(
                "\r\n",
                "\n"
            )
            .Trim();
    }

    private static int CountContinuousRows(
        WorkbookPart workbookPart,
        WorksheetPart worksheetPart,
        uint firstRow,
        int keyColumn)
    {
        var count = 0;

        for (var rowNumber = firstRow;
             rowNumber <= 10000;
             rowNumber++)
        {
            var value =
                ExcelCellReader.GetCellText(
                    workbookPart,
                    ExcelCellReader.GetCell(
                        worksheetPart,
                        rowNumber,
                        keyColumn
                    )
                );

            if (string.IsNullOrWhiteSpace(
                    value))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private MemberWorkbookData ReadMemberWithRetry(
        AggregationMember member,
        string monthlySheetName)
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

            if (attempt <
                maximumAttempts)
            {
                Thread.Sleep(
                    retryDelayMilliseconds
                );
            }
        }

        throw new IOException(
            "個人工数表を読み取れませんでした: " +
            member.WorkbookPath,
            lastException
        );
    }
}

public sealed record AggregationReadBackResult(
    bool IsSuccess,
    bool HasVbaProject,
    int ExpectedWorkRowCount,
    int ActualWorkRowCount,
    int ExpectedOvertimeRowCount,
    int ActualOvertimeRowCount,
    int DifferenceCount,
    IReadOnlyList<AggregationReadBackError> ReadErrors,
    IReadOnlyList<AggregationCellDifference> Differences
);

public sealed record AggregationReadBackError(
    string EmployeeNumber,
    string FullName,
    string WorkbookPath,
    string Message
);

public sealed record AggregationCellDifference(
    string SheetName,
    int RowNumber,
    string ColumnName,
    string EmployeeNumber,
    string FullName,
    string Expected,
    string Actual
);

internal sealed record ExpectedAggregationWorkRow(
    string EmployeeNumber,
    string FullName,
    string SourceWorkbookPath,
    int SourceRowNumber,
    IReadOnlyList<ExcelCellValue> TypedSourceValues,
    string TaskName,
    string Note
);

internal sealed record ExpectedAggregationOvertimeRow(
    string EmployeeNumber,
    string FullName,
    double OvertimeHours,
    double InternalWorkHours,
    double TravelCost,
    string PrimaryExternalWork
);