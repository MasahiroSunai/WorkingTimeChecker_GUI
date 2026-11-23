using DocumentFormat.OpenXml.Packaging;

namespace WorkingTimeChecker.Core.Workload;

public sealed class MemberWorkbookReader
{
    private const int FirstWorkRow = 13;
    private const int LastWorkRow = 100;
    private const int WorkRowInterval = 2;

    private const int FirstSourceColumn = 2;
    private const int LastSourceColumn = 42;

    private const int ProjectColumn = 7;
    private const int TaskColumn = 7;
    private const int NoteColumn = 9;
    private const int TotalColumn = 42;

    private const string OvertimeCell = "AQ2";
    private const string TravelCostCell = "G417";

    public MemberWorkbookData Read(
        string workbookPath,
        string monthlySheetName)
    {
        if (string.IsNullOrWhiteSpace(workbookPath))
        {
            throw new ArgumentException(
                "個人工数表のパスが未指定です。",
                nameof(workbookPath)
            );
        }

        if (string.IsNullOrWhiteSpace(monthlySheetName))
        {
            throw new ArgumentException(
                "月度シート名が未指定です。",
                nameof(monthlySheetName)
            );
        }

        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException(
                "個人工数表が見つかりません。",
                workbookPath
            );
        }

        using var workbookStream =
            new FileStream(
                workbookPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite |
                FileShare.Delete
            );

        using var document =
            SpreadsheetDocument.Open(
                workbookStream,
                isEditable: false
            );

        var workbookPart =
            document.WorkbookPart
            ?? throw new InvalidOperationException(
                "個人工数表のWorkbookPartがありません。"
            );

        var worksheetPart =
            ExcelCellReader.GetWorksheetPart(
                workbookPart,
                monthlySheetName
            );

        var overtimeHours =
            ExcelCellReader.GetCellDouble(
                workbookPart,
                ExcelCellReader.GetCell(
                    worksheetPart,
                    OvertimeCell
                )
            );

        var travelCost =
            ExcelCellReader.GetCellDouble(
                workbookPart,
                ExcelCellReader.GetCell(
                    worksheetPart,
                    TravelCostCell
                )
            );

        var rows =
            new List<MemberWorkRow>();

        for (var rowNumber = FirstWorkRow;
             rowNumber <= LastWorkRow;
             rowNumber += WorkRowInterval)
        {
            var totalCell =
                ExcelCellReader.GetCell(
                    worksheetPart,
                    (uint)rowNumber,
                    TotalColumn
                );

            var totalText =
                ExcelCellReader.GetCellText(
                    workbookPart,
                    totalCell
                ).Trim();

            // VBAと同じ条件:
            // AP列が空でない行のみ集計対象とする。
            if (string.IsNullOrWhiteSpace(totalText))
            {
                continue;
            }

            var sourceValues =
                new List<string>(
                    LastSourceColumn -
                    FirstSourceColumn +
                    1
                );

            var typedSourceValues =
                new List<ExcelCellValue>(
                    LastSourceColumn -
                    FirstSourceColumn +
                    1
                );

            for (var columnNumber =
                     FirstSourceColumn;
                 columnNumber <=
                     LastSourceColumn;
                 columnNumber++)
            {
                var cell =
                    ExcelCellReader.GetCell(
                        worksheetPart,
                        (uint)rowNumber,
                        columnNumber
                    );

                var typedValue =
                    ExcelCellValue.FromCell(
                        workbookPart,
                        cell
                    );

                typedSourceValues.Add(
                    typedValue
                );

                sourceValues.Add(
                    typedValue.Text
                );
            }

            var projectName =
                ExcelCellReader.GetCellText(
                    workbookPart,
                    ExcelCellReader.GetCell(
                        worksheetPart,
                        (uint)rowNumber,
                        ProjectColumn
                    )
                ).Trim();

            var taskName =
                ExcelCellReader.GetCellText(
                    workbookPart,
                    ExcelCellReader.GetCell(
                        worksheetPart,
                        (uint)(rowNumber + 1),
                        TaskColumn
                    )
                ).Trim();

            var note =
                ExcelCellReader.GetCellText(
                    workbookPart,
                    ExcelCellReader.GetCell(
                        worksheetPart,
                        (uint)(rowNumber + 1),
                        NoteColumn
                    )
                ).Trim();

            var totalHours =
                ExcelCellReader.GetCellDouble(
                    workbookPart,
                    totalCell
                );

            rows.Add(
                new MemberWorkRow(
                    SourceRowNumber: rowNumber,
                    SourceValues: sourceValues,
                    TypedSourceValues: typedSourceValues,
                    ProjectName: projectName,
                    TaskName: taskName,
                    Note: note,
                    TotalHours: totalHours
                )
            );
        }

        var internalWorkHours =
            rows
                .Where(
                    row => string.Equals(
                        row.ProjectName,
                        "社内工数",
                        StringComparison.Ordinal
                    )
                )
                .Sum(
                    row => row.TotalHours
                );

        var primaryExternalWork =
            rows
                .Where(
                    row => !row.ProjectName.Contains(
                        "社内工数",
                        StringComparison.Ordinal
                    )
                )
                .GroupBy(
                    row => BuildWorkKey(
                        row.ProjectName,
                        row.TaskName
                    ),
                    StringComparer.Ordinal
                )
                .Select(
                    group => new
                    {
                        WorkName = group.Key,
                        Hours = group.Sum(
                            row => row.TotalHours
                        ),
                    }
                )
                .OrderByDescending(
                    item => item.Hours
                )
                .ThenBy(
                    item => item.WorkName,
                    StringComparer.Ordinal
                )
                .FirstOrDefault()?
                .WorkName
            ?? string.Empty;

        return new MemberWorkbookData(
            WorkbookPath: workbookPath,
            MonthlySheetName: monthlySheetName,
            OvertimeHours: overtimeHours,
            TravelCost: travelCost,
            InternalWorkHours: internalWorkHours,
            PrimaryExternalWork: primaryExternalWork,
            WorkRows: rows
        );
    }

    private static string BuildWorkKey(
        string projectName,
        string taskName)
    {
        return projectName + "-" + taskName;
    }
}

public sealed record MemberWorkbookData(
    string WorkbookPath,
    string MonthlySheetName,
    double OvertimeHours,
    double TravelCost,
    double InternalWorkHours,
    string PrimaryExternalWork,
    IReadOnlyList<MemberWorkRow> WorkRows
);

public sealed record MemberWorkRow(
    int SourceRowNumber,
    IReadOnlyList<string> SourceValues,
    IReadOnlyList<ExcelCellValue> TypedSourceValues,
    string ProjectName,
    string TaskName,
    string Note,
    double TotalHours
);