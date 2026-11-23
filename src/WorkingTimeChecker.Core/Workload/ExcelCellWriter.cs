using DocumentFormat.OpenXml.Packaging;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;

namespace WorkingTimeChecker.Core.Workload;

/// <summary>
/// 既存のセル書式を維持しながら、
/// Excelセルへ値を書き込みます。
/// </summary>
internal static class ExcelCellWriter
{
    public static Cell GetOrCreateCell(
        WorksheetPart worksheetPart,
        uint rowIndex,
        int columnIndex)
    {
        var sheetData =
            worksheetPart.Worksheet.GetFirstChild<SheetData>()
            ?? worksheetPart.Worksheet.AppendChild(
                new SheetData()
            );

        var row = sheetData
            .Elements<Row>()
            .FirstOrDefault(
                item =>
                    item.RowIndex?.Value ==
                    rowIndex
            );

        if (row is null)
        {
            row = new Row
            {
                RowIndex = rowIndex,
            };

            var nextRow = sheetData
                .Elements<Row>()
                .FirstOrDefault(
                    item =>
                        item.RowIndex?.Value >
                        rowIndex
                );

            if (nextRow is null)
            {
                sheetData.Append(row);
            }
            else
            {
                sheetData.InsertBefore(
                    row,
                    nextRow
                );
            }
        }

        var cellReference =
            ExcelCellReader.GetColumnName(
                columnIndex
            ) +
            rowIndex.ToString(
                CultureInfo.InvariantCulture
            );

        var existingCell = row
            .Elements<Cell>()
            .FirstOrDefault(
                item => string.Equals(
                    item.CellReference?.Value,
                    cellReference,
                    StringComparison.OrdinalIgnoreCase
                )
            );

        if (existingCell is not null)
        {
            return existingCell;
        }

        var newCell = new Cell
        {
            CellReference =
                cellReference,
        };

        var nextCell = row
            .Elements<Cell>()
            .FirstOrDefault(
                item =>
                    GetColumnIndex(
                        item.CellReference?.Value
                    ) >
                    columnIndex
            );

        if (nextCell is null)
        {
            row.Append(newCell);
        }
        else
        {
            row.InsertBefore(
                newCell,
                nextCell
            );
        }

        return newCell;
    }

    public static void WriteValue(
        WorksheetPart worksheetPart,
        uint rowIndex,
        int columnIndex,
        ExcelCellValue value,
        uint? styleIndex = null)
    {
        var cell = GetOrCreateCell(
            worksheetPart,
            rowIndex,
            columnIndex
        );

        if (styleIndex.HasValue)
        {
            cell.StyleIndex =
                styleIndex.Value;
        }

        cell.CellFormula = null;
        cell.InlineString = null;

        switch (value.ValueType)
        {
            case ExcelCellValueType.Empty:
                ClearValue(cell);
                break;

            case ExcelCellValueType.Text:
                cell.DataType =
                    CellValues.InlineString;

                cell.CellValue = null;

                cell.InlineString =
                    new InlineString(
                        new Text(value.Text)
                        {
                            Space =
                                SpaceProcessingModeValues
                                    .Preserve,
                        }
                    );
                break;

            case ExcelCellValueType.Number:
                cell.DataType = null;

                cell.CellValue =
                    new CellValue(
                        value.Text
                    );
                break;

            case ExcelCellValueType.Boolean:
                cell.DataType =
                    CellValues.Boolean;

                cell.CellValue =
                    new CellValue(
                        string.Equals(
                            value.Text,
                            "TRUE",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? "1"
                            : "0"
                    );
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value.ValueType,
                    "未対応のセル値型です。"
                );
        }
    }

    public static void WriteText(
        WorksheetPart worksheetPart,
        uint rowIndex,
        int columnIndex,
        string? value,
        uint? styleIndex = null)
    {
        WriteValue(
            worksheetPart,
            rowIndex,
            columnIndex,
            ExcelCellValue.FromText(value),
            styleIndex
        );
    }

    public static void WriteNumber(
        WorksheetPart worksheetPart,
        uint rowIndex,
        int columnIndex,
        double value,
        uint? styleIndex = null)
    {
        WriteValue(
            worksheetPart,
            rowIndex,
            columnIndex,
            ExcelCellValue.FromNumber(value),
            styleIndex
        );
    }

    public static void ClearRangeValues(
        WorksheetPart worksheetPart,
        uint firstRow,
        uint lastRow,
        int firstColumn,
        int lastColumn)
    {
        for (var rowIndex = firstRow;
             rowIndex <= lastRow;
             rowIndex++)
        {
            for (var columnIndex = firstColumn;
                 columnIndex <= lastColumn;
                 columnIndex++)
            {
                var cell =
                    ExcelCellReader.GetCell(
                        worksheetPart,
                        rowIndex,
                        columnIndex
                    );

                if (cell is null)
                {
                    continue;
                }

                ClearValue(cell);
            }
        }
    }

    public static uint? GetStyleIndex(
        WorksheetPart worksheetPart,
        uint rowIndex,
        int columnIndex)
    {
        return ExcelCellReader
            .GetCell(
                worksheetPart,
                rowIndex,
                columnIndex
            )
            ?.StyleIndex
            ?.Value;
    }

    public static void CopyRowStyles(
        WorksheetPart worksheetPart,
        uint sourceRow,
        uint destinationRow,
        int firstColumn,
        int lastColumn)
    {
        for (var column = firstColumn;
             column <= lastColumn;
             column++)
        {
            var sourceStyle =
                GetStyleIndex(
                    worksheetPart,
                    sourceRow,
                    column
                );

            if (!sourceStyle.HasValue)
            {
                continue;
            }

            var destinationCell =
                GetOrCreateCell(
                    worksheetPart,
                    destinationRow,
                    column
                );

            destinationCell.StyleIndex =
                sourceStyle.Value;
        }
    }

    private static void ClearValue(
        Cell cell)
    {
        cell.CellFormula = null;
        cell.CellValue = null;
        cell.InlineString = null;
        cell.DataType = null;
    }

    private static int GetColumnIndex(
        string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(
                cellReference))
        {
            return int.MaxValue;
        }

        var result = 0;

        foreach (var character
                 in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            result =
                result * 26 +
                char.ToUpperInvariant(
                    character
                ) -
                'A' +
                1;
        }

        return result;
    }
}