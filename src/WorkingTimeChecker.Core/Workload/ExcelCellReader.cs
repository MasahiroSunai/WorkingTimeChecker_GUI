using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace WorkingTimeChecker.Core.Workload;

internal static class ExcelCellReader
{
    public static WorksheetPart GetWorksheetPart(
        WorkbookPart workbookPart,
        string sheetName)
    {
        var sheet = workbookPart
            .Workbook
            .Sheets?
            .Elements<Sheet>()
            .FirstOrDefault(
                item => string.Equals(
                    item.Name?.Value,
                    sheetName,
                    StringComparison.Ordinal
                )
            );

        if (sheet is null)
        {
            throw new InvalidOperationException(
                $"シートが見つかりません: {sheetName}"
            );
        }

        if (sheet.Id?.Value is null)
        {
            throw new InvalidOperationException(
                $"シートIDがありません: {sheetName}"
            );
        }

        return (WorksheetPart)workbookPart.GetPartById(
            sheet.Id.Value
        );
    }

    public static Cell? GetCell(
        WorksheetPart worksheetPart,
        string cellReference)
    {
        return worksheetPart
            .Worksheet
            .Descendants<Cell>()
            .FirstOrDefault(
                cell => string.Equals(
                    cell.CellReference?.Value,
                    cellReference,
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }

    public static Cell? GetCell(
        WorksheetPart worksheetPart,
        uint rowIndex,
        int columnIndex)
    {
        var cellReference =
            GetColumnName(columnIndex) +
            rowIndex.ToString(
                CultureInfo.InvariantCulture
            );

        return GetCell(
            worksheetPart,
            cellReference
        );
    }

    public static string GetCellText(
        WorkbookPart workbookPart,
        Cell? cell)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        var rawValue =
            cell.CellValue?.InnerText
            ?? cell.InnerText
            ?? string.Empty;

        if (cell.DataType?.Value ==
            CellValues.SharedString)
        {
            if (!int.TryParse(
                    rawValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var sharedStringIndex))
            {
                return rawValue;
            }

            var sharedStringPart =
                workbookPart.SharedStringTablePart;

            var sharedString = sharedStringPart?
                .SharedStringTable?
                .Elements<SharedStringItem>()
                .ElementAtOrDefault(
                    sharedStringIndex
                );

            if (sharedString is null)
            {
                return string.Empty;
            }

            // InnerTextを使用すると、Excel内部の
            // ふりがな情報まで連結される場合がある。
            // 表示文字のTextとRunだけを取得する。
            if (sharedString.Text is not null)
            {
                return sharedString.Text.Text;
            }

            return string.Concat(
                sharedString
                    .Elements<Run>()
                    .Select(
                        run => run.Text?.Text
                            ?? string.Empty
                    )
            );
        }

        if (cell.DataType?.Value ==
            CellValues.InlineString)
        {
            return cell.InlineString?.InnerText
                ?? string.Empty;
        }

        if (cell.DataType?.Value ==
            CellValues.Boolean)
        {
            return rawValue == "1"
                ? "TRUE"
                : "FALSE";
        }

        return rawValue;
    }

    public static double GetCellDouble(
        WorkbookPart workbookPart,
        Cell? cell,
        double defaultValue = 0)
    {
        var text = GetCellText(
            workbookPart,
            cell
        );

        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        if (double.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return value;
        }

        throw new InvalidDataException(
            $"セルの値を数値へ変換できません: " +
            $"{cell?.CellReference?.Value} = {text}"
        );
    }

    public static string GetColumnName(
        int columnIndex)
    {
        if (columnIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(columnIndex),
                "列番号は1以上で指定してください。"
            );
        }

        var dividend = columnIndex;
        var columnName = string.Empty;

        while (dividend > 0)
        {
            var remainder =
                (dividend - 1) % 26;

            columnName =
                Convert.ToChar('A' + remainder) +
                columnName;

            dividend =
                (dividend - remainder - 1) / 26;
        }

        return columnName;
    }

    public static DefinedNameReference
        ResolveDefinedName(
            WorkbookPart workbookPart,
            string definedName)
    {
        var item = workbookPart
            .Workbook
            .DefinedNames?
            .Elements<DefinedName>()
            .FirstOrDefault(
                name => string.Equals(
                    name.Name?.Value,
                    definedName,
                    StringComparison.Ordinal
                )
            );

        if (item is null ||
            string.IsNullOrWhiteSpace(item.Text))
        {
            throw new InvalidOperationException(
                $"名前付き範囲が見つかりません: " +
                definedName
            );
        }

        var reference = item.Text
            .Replace("$", string.Empty)
            .Trim();

        var separatorIndex =
            reference.LastIndexOf('!');

        if (separatorIndex <= 0 ||
            separatorIndex >= reference.Length - 1)
        {
            throw new InvalidOperationException(
                $"名前付き範囲の形式が不正です: " +
                $"{definedName} = {item.Text}"
            );
        }

        var sheetName = reference[
            ..separatorIndex
        ].Trim('\'');

        var cellReference = reference[
            (separatorIndex + 1)..
        ];

        var rangeSeparatorIndex =
            cellReference.IndexOf(':');

        if (rangeSeparatorIndex >= 0)
        {
            cellReference = cellReference[
                ..rangeSeparatorIndex
            ];
        }

        return new DefinedNameReference(
            SheetName: sheetName,
            CellReference: cellReference
        );
    }
}

internal sealed record DefinedNameReference(
    string SheetName,
    string CellReference
);