using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace WorkingTimeChecker.Core.Workload;

/// <summary>
/// 集計ブックの工数一覧表と
/// 残業・社内工数時間シートを更新します。
/// </summary>
public sealed class AggregationWorkbookWriter
{
    private const string WorkSheetName =
        "工数一覧表";

    private const string OvertimeSheetName =
        "残業・社内工数時間";

    private const uint WorkStartRow = 7;
    private const int WorkFirstColumn = 2;
    private const int WorkLastColumn = 44;

    private const uint OvertimeStartRow = 3;
    private const int OvertimeFirstColumn = 2;
    private const int OvertimeLastColumn = 6;

    public void Write(
        string sourceWorkbookPath,
        string destinationWorkbookPath,
        WorkloadAggregationData data)
    {
        ValidateArguments(
            sourceWorkbookPath,
            destinationWorkbookPath,
            data
        );

        var destinationDirectory =
            Path.GetDirectoryName(
                destinationWorkbookPath
            );

        if (!string.IsNullOrWhiteSpace(
                destinationDirectory))
        {
            Directory.CreateDirectory(
                destinationDirectory
            );
        }

        File.Copy(
            sourceWorkbookPath,
            destinationWorkbookPath,
            overwrite: true
        );

        using var document =
            SpreadsheetDocument.Open(
                destinationWorkbookPath,
                isEditable: true
            );

        var workbookPart =
            document.WorkbookPart
            ?? throw new InvalidOperationException(
                "集計ブックのWorkbookPartがありません。"
            );

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

        WriteWorkSheet(
            workWorksheetPart,
            data
        );

        WriteOvertimeSheet(
            overtimeWorksheetPart,
            data
        );

        SetFullCalculationOnOpen(
            workbookPart
        );

        workWorksheetPart.Worksheet.Save();
        overtimeWorksheetPart.Worksheet.Save();
        workbookPart.Workbook.Save();
    }

    private static void ValidateArguments(
        string sourceWorkbookPath,
        string destinationWorkbookPath,
        WorkloadAggregationData data)
    {
        if (string.IsNullOrWhiteSpace(
                sourceWorkbookPath))
        {
            throw new ArgumentException(
                "コピー元の集計ブックが未指定です。",
                nameof(sourceWorkbookPath)
            );
        }

        if (!File.Exists(sourceWorkbookPath))
        {
            throw new FileNotFoundException(
                "コピー元の集計ブックが見つかりません。",
                sourceWorkbookPath
            );
        }

        if (string.IsNullOrWhiteSpace(
                destinationWorkbookPath))
        {
            throw new ArgumentException(
                "出力先の集計ブックが未指定です。",
                nameof(destinationWorkbookPath)
            );
        }

        ArgumentNullException.ThrowIfNull(data);

        var sourceFullPath =
            Path.GetFullPath(
                sourceWorkbookPath
            );

        var destinationFullPath =
            Path.GetFullPath(
                destinationWorkbookPath
            );

        if (string.Equals(
                sourceFullPath,
                destinationFullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "コピー元と出力先には別のファイルを指定してください。"
            );
        }
    }

    private static void WriteWorkSheet(
        WorksheetPart worksheetPart,
        WorkloadAggregationData data)
    {
        var existingLastRow =
            FindLastUsedRow(
                worksheetPart,
                WorkStartRow,
                WorkFirstColumn
            );

        var outputLastRow =
            data.WorkRows.Count == 0
                ? WorkStartRow
                : WorkStartRow +
                  checked(
                      (uint)data.WorkRows.Count
                  ) -
                  1;

        var clearLastRow =
            Math.Max(
                existingLastRow,
                outputLastRow
            );

        ExcelCellWriter.ClearRangeValues(
            worksheetPart,
            WorkStartRow,
            clearLastRow,
            WorkFirstColumn,
            WorkLastColumn
        );

        for (var index = 0;
             index < data.WorkRows.Count;
             index++)
        {
            var destinationRow =
                WorkStartRow +
                checked((uint)index);

            if (destinationRow != WorkStartRow)
            {
                ExcelCellWriter.CopyRowStyles(
                    worksheetPart,
                    WorkStartRow,
                    destinationRow,
                    WorkFirstColumn,
                    WorkLastColumn
                );
            }

            var workRow =
                data.WorkRows[index];

            WriteEmployeeNumber(
                worksheetPart,
                destinationRow,
                workRow.EmployeeNumber
            );

            ExcelCellWriter.WriteText(
                worksheetPart,
                destinationRow,
                3,
                workRow.FullName,
                ExcelCellWriter.GetStyleIndex(
                    worksheetPart,
                    WorkStartRow,
                    3
                )
            );

            // 個人工数表のB～APを
            // 集計表のD～ARへ転記します。
            for (var sourceIndex = 0;
                 sourceIndex <
                     workRow.TypedSourceValues.Count;
                 sourceIndex++)
            {
                var destinationColumn =
                    sourceIndex + 4;

                var styleIndex =
                    ExcelCellWriter.GetStyleIndex(
                        worksheetPart,
                        WorkStartRow,
                        destinationColumn
                    );

                ExcelCellWriter.WriteValue(
                    worksheetPart,
                    destinationRow,
                    destinationColumn,
                    workRow.TypedSourceValues[
                        sourceIndex
                    ],
                    styleIndex
                );
            }

            // VBAと同じく、J列とK列は
            // 個人工数表の次行の値で上書きします。
            ExcelCellWriter.WriteText(
                worksheetPart,
                destinationRow,
                10,
                workRow.TaskName,
                ExcelCellWriter.GetStyleIndex(
                    worksheetPart,
                    WorkStartRow,
                    10
                )
            );

            ExcelCellWriter.WriteText(
                worksheetPart,
                destinationRow,
                11,
                workRow.Note,
                ExcelCellWriter.GetStyleIndex(
                    worksheetPart,
                    WorkStartRow,
                    11
                )
            );
        }

        // 更新日時はVBAと同じG2へ記録します。
        var updateTimeStyle =
            ExcelCellWriter.GetStyleIndex(
                worksheetPart,
                2,
                7
            );

        ExcelCellWriter.WriteNumber(
            worksheetPart,
            2,
            7,
            DateTime.Now.ToOADate(),
            updateTimeStyle
        );
    }

    private static void WriteOvertimeSheet(
        WorksheetPart worksheetPart,
        WorkloadAggregationData data)
    {
        var existingLastRow =
            FindLastUsedRow(
                worksheetPart,
                OvertimeStartRow,
                OvertimeFirstColumn
            );

        var outputLastRow =
            data.OvertimeRows.Count == 0
                ? OvertimeStartRow
                : OvertimeStartRow +
                  checked(
                      (uint)data.OvertimeRows.Count
                  ) -
                  1;

        var clearLastRow =
            Math.Max(
                existingLastRow,
                outputLastRow
            );

        ExcelCellWriter.ClearRangeValues(
            worksheetPart,
            OvertimeStartRow,
            clearLastRow,
            OvertimeFirstColumn,
            OvertimeLastColumn
        );

        for (var index = 0;
             index < data.OvertimeRows.Count;
             index++)
        {
            var destinationRow =
                OvertimeStartRow +
                checked((uint)index);

            if (destinationRow != OvertimeStartRow)
            {
                ExcelCellWriter.CopyRowStyles(
                    worksheetPart,
                    OvertimeStartRow,
                    destinationRow,
                    OvertimeFirstColumn,
                    OvertimeLastColumn
                );
            }

            var overtimeRow =
                data.OvertimeRows[index];

            ExcelCellWriter.WriteText(
                worksheetPart,
                destinationRow,
                2,
                overtimeRow.FullName,
                ExcelCellWriter.GetStyleIndex(
                    worksheetPart,
                    OvertimeStartRow,
                    2
                )
            );

            ExcelCellWriter.WriteNumber(
                worksheetPart,
                destinationRow,
                3,
                overtimeRow.OvertimeHours,
                ExcelCellWriter.GetStyleIndex(
                    worksheetPart,
                    OvertimeStartRow,
                    3
                )
            );

            ExcelCellWriter.WriteNumber(
                worksheetPart,
                destinationRow,
                4,
                overtimeRow.InternalWorkHours,
                ExcelCellWriter.GetStyleIndex(
                    worksheetPart,
                    OvertimeStartRow,
                    4
                )
            );

            ExcelCellWriter.WriteNumber(
                worksheetPart,
                destinationRow,
                5,
                overtimeRow.TravelCost,
                ExcelCellWriter.GetStyleIndex(
                    worksheetPart,
                    OvertimeStartRow,
                    5
                )
            );

            ExcelCellWriter.WriteText(
                worksheetPart,
                destinationRow,
                6,
                overtimeRow.PrimaryExternalWork,
                ExcelCellWriter.GetStyleIndex(
                    worksheetPart,
                    OvertimeStartRow,
                    6
                )
            );
        }
    }

    private static void WriteEmployeeNumber(
        WorksheetPart worksheetPart,
        uint rowIndex,
        string employeeNumber)
    {
        var styleIndex =
            ExcelCellWriter.GetStyleIndex(
                worksheetPart,
                WorkStartRow,
                2
            );

        if (double.TryParse(
                employeeNumber,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var numericEmployeeNumber))
        {
            ExcelCellWriter.WriteNumber(
                worksheetPart,
                rowIndex,
                2,
                numericEmployeeNumber,
                styleIndex
            );

            return;
        }

        ExcelCellWriter.WriteText(
            worksheetPart,
            rowIndex,
            2,
            employeeNumber,
            styleIndex
        );
    }

    private static uint FindLastUsedRow(
        WorksheetPart worksheetPart,
        uint minimumRow,
        int keyColumn)
    {
        var sheetData =
            worksheetPart.Worksheet
                .GetFirstChild<SheetData>();

        if (sheetData is null)
        {
            return minimumRow;
        }

        var lastUsedRow =
            minimumRow;

        foreach (var row in sheetData.Elements<Row>())
        {
            var rowIndex =
                row.RowIndex?.Value;

            if (!rowIndex.HasValue ||
                rowIndex.Value < minimumRow)
            {
                continue;
            }

            var cellReference =
                ExcelCellReader.GetColumnName(
                    keyColumn
                ) +
                rowIndex.Value.ToString(
                    CultureInfo.InvariantCulture
                );

            var cell = row
                .Elements<Cell>()
                .FirstOrDefault(
                    item => string.Equals(
                        item.CellReference?.Value,
                        cellReference,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (cell is null)
            {
                continue;
            }

            var hasValue =
                cell.CellValue is not null ||
                cell.InlineString is not null ||
                cell.CellFormula is not null;

            if (hasValue)
            {
                lastUsedRow =
                    Math.Max(
                        lastUsedRow,
                        rowIndex.Value
                    );
            }
        }

        return lastUsedRow;
    }

    private static void SetFullCalculationOnOpen(
        WorkbookPart workbookPart)
    {
        var calculationProperties =
            workbookPart
                .Workbook
                .CalculationProperties;

        if (calculationProperties is null)
        {
            calculationProperties =
                new CalculationProperties();

            workbookPart
                .Workbook
                .Append(
                    calculationProperties
                );
        }

        calculationProperties
            .ForceFullCalculation = true;

        calculationProperties
            .FullCalculationOnLoad = true;

        calculationProperties
            .CalculationMode =
                CalculateModeValues.Auto;
    }
}