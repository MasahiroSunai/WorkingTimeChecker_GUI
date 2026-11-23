using DocumentFormat.OpenXml.Packaging;
using System.Globalization;
using DocumentFormat.OpenXml.Spreadsheet;

namespace WorkingTimeChecker.Core.Workload;

/// <summary>
/// Excelセルから読み取った値と型情報を保持します。
/// </summary>
public sealed record ExcelCellValue(
    string Text,
    ExcelCellValueType ValueType,
    string? Formula = null)
{
    public static ExcelCellValue Empty { get; } =
        new(
            Text: string.Empty,
            ValueType: ExcelCellValueType.Empty
        );

    public bool IsEmpty =>
        ValueType == ExcelCellValueType.Empty ||
        string.IsNullOrWhiteSpace(Text);

    public bool IsNumeric =>
        ValueType == ExcelCellValueType.Number;

    public bool IsBoolean =>
        ValueType == ExcelCellValueType.Boolean;

    public static ExcelCellValue FromCell(
        WorkbookPart workbookPart,
        Cell? cell)
    {
        if (cell is null)
        {
            return Empty;
        }

        var formula =
            cell.CellFormula?.Text;

        var rawValue =
            cell.CellValue?.InnerText
            ?? string.Empty;

        if (cell.DataType?.Value ==
            CellValues.SharedString)
        {
            var text = ReadSharedString(
                workbookPart,
                rawValue
            );

            return new ExcelCellValue(
                Text: text,
                ValueType:
                    string.IsNullOrEmpty(text)
                        ? ExcelCellValueType.Empty
                        : ExcelCellValueType.Text,
                Formula: formula
            );
        }

        if (cell.DataType?.Value ==
            CellValues.InlineString)
        {
            var text =
                cell.InlineString?.Text?.Text
                ?? string.Concat(
                    cell.InlineString?
                        .Elements<Run>()
                        .Select(
                            run =>
                                run.Text?.Text
                                ?? string.Empty
                        )
                    ?? Enumerable.Empty<string>()
                );

            return new ExcelCellValue(
                Text: text,
                ValueType:
                    string.IsNullOrEmpty(text)
                        ? ExcelCellValueType.Empty
                        : ExcelCellValueType.Text,
                Formula: formula
            );
        }

        if (cell.DataType?.Value ==
            CellValues.Boolean)
        {
            return new ExcelCellValue(
                Text:
                    rawValue == "1"
                        ? "TRUE"
                        : "FALSE",
                ValueType:
                    ExcelCellValueType.Boolean,
                Formula: formula
            );
        }

        if (cell.DataType?.Value ==
            CellValues.String)
        {
            return new ExcelCellValue(
                Text: rawValue,
                ValueType:
                    string.IsNullOrEmpty(rawValue)
                        ? ExcelCellValueType.Empty
                        : ExcelCellValueType.Text,
                Formula: formula
            );
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return new ExcelCellValue(
                Text: string.Empty,
                ValueType:
                    ExcelCellValueType.Empty,
                Formula: formula
            );
        }

        if (double.TryParse(
                rawValue,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out _))
        {
            return new ExcelCellValue(
                Text: rawValue,
                ValueType:
                    ExcelCellValueType.Number,
                Formula: formula
            );
        }

        return new ExcelCellValue(
            Text: rawValue,
            ValueType:
                ExcelCellValueType.Text,
            Formula: formula
        );
    }

    public static ExcelCellValue FromText(
        string? value)
    {
        var text =
            value ?? string.Empty;

        return string.IsNullOrEmpty(text)
            ? Empty
            : new ExcelCellValue(
                Text: text,
                ValueType:
                    ExcelCellValueType.Text
            );
    }

    public static ExcelCellValue FromNumber(
        double value)
    {
        return new ExcelCellValue(
            Text: value.ToString(
                "0.################",
                CultureInfo.InvariantCulture
            ),
            ValueType:
                ExcelCellValueType.Number
        );
    }

    private static string ReadSharedString(
        WorkbookPart workbookPart,
        string rawValue)
    {
        if (!int.TryParse(
                rawValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var index))
        {
            return rawValue;
        }

        var item =
            workbookPart
                .SharedStringTablePart?
                .SharedStringTable?
                .Elements<SharedStringItem>()
                .ElementAtOrDefault(index);

        if (item is null)
        {
            return string.Empty;
        }

        // InnerTextを使用すると、Excel内部の
        // ふりがな情報まで連結される場合がある。
        if (item.Text is not null)
        {
            return item.Text.Text;
        }

        return string.Concat(
            item
                .Elements<Run>()
                .Select(
                    run =>
                        run.Text?.Text
                        ?? string.Empty
                )
        );
    }
}

public enum ExcelCellValueType
{
    Empty,
    Text,
    Number,
    Boolean,
}