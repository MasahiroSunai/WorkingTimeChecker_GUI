using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;

namespace WorkingTimeChecker.Core.Workload;

public sealed class AggregationSettingsReader
{
    private const string SettingsSheetName =
        "工数集計表_設定シート";

    public AggregationSettings Read(
        string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath))
        {
            throw new ArgumentException(
                "集計ブックのパスが未指定です。",
                nameof(workbookPath)
            );
        }

        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException(
                "集計ブックが見つかりません。",
                workbookPath
            );
        }

        using var document =
            SpreadsheetDocument.Open(
                workbookPath,
                isEditable: false
            );

        var workbookPart =
            document.WorkbookPart
            ?? throw new InvalidOperationException(
                "集計ブックのWorkbookPartがありません。"
            );

        var worksheetPart =
            ExcelCellReader.GetWorksheetPart(
                workbookPart,
                SettingsSheetName
            );

        var yearReference =
            new DefinedNameReference(
                SettingsSheetName,
                "B8"
            );

        var monthReference =
            new DefinedNameReference(
                SettingsSheetName,
                "C8"
            );

        var serverPathReference =
            new DefinedNameReference(
                SettingsSheetName,
                "G8"
            );

        var nameReference =
            new DefinedNameReference(
                SettingsSheetName,
                "D8"
            );

        var employeeNumberReference =
            new DefinedNameReference(
                SettingsSheetName,
                "H8"
            );

        var fullNameReference =
            new DefinedNameReference(
                SettingsSheetName,
                "I8"
            );

        ValidateSettingsSheet(
            yearReference,
            monthReference,
            serverPathReference,
            nameReference,
            employeeNumberReference,
            fullNameReference
        );

        var year = ReadInteger(
            workbookPart,
            worksheetPart,
            yearReference.CellReference,
            "年"
        );

        var month = ReadInteger(
            workbookPart,
            worksheetPart,
            monthReference.CellReference,
            "月"
        );

        if (month is < 1 or > 12)
        {
            throw new InvalidDataException(
                $"月が1～12ではありません: {month}"
            );
        }

        var headerRow =
            GetRowIndex(
                serverPathReference.CellReference
            );

        var serverPathColumn =
            GetColumnIndex(
                serverPathReference.CellReference
            );

        var nameColumn =
            GetColumnIndex(
                nameReference.CellReference
            );

        var employeeNumberColumn =
            GetColumnIndex(
                employeeNumberReference.CellReference
            );

        var fullNameColumn =
            GetColumnIndex(
                fullNameReference.CellReference
            );

        var lastRow = FindLastUsedRow(
            workbookPart,
            worksheetPart,
            serverPathColumn,
            headerRow + 1
        );

        var members =
            new List<AggregationMember>();

        for (var rowIndex = headerRow + 1;
             rowIndex <= lastRow;
             rowIndex++)
        {
            var serverPathCell =
                ExcelCellReader.GetCell(
                    worksheetPart,
                    (uint)rowIndex,
                    serverPathColumn
                );

            var serverPath =
                ExcelCellReader.GetCellText(
                    workbookPart,
                    serverPathCell
                ).Trim();

            if (string.IsNullOrWhiteSpace(serverPath))
            {
                continue;
            }

            var nameCell =
                ExcelCellReader.GetCell(
                    worksheetPart,
                    (uint)rowIndex,
                    nameColumn
                );

            var employeeNumberCell =
                ExcelCellReader.GetCell(
                    worksheetPart,
                    (uint)rowIndex,
                    employeeNumberColumn
                );

            var fullNameCell =
                ExcelCellReader.GetCell(
                    worksheetPart,
                    (uint)rowIndex,
                    fullNameColumn
                );

            var member = new AggregationMember(
                RowNumber: rowIndex,
                Name: ExcelCellReader.GetCellText(
                    workbookPart,
                    nameCell
                ).Trim(),
                EmployeeNumber:
                    ExcelCellReader.GetCellText(
                        workbookPart,
                        employeeNumberCell
                    ).Trim(),
                FullName:
                    ExcelCellReader.GetCellText(
                        workbookPart,
                        fullNameCell
                    ).Trim(),
                WorkbookPath:
                    NormalizeWindowsPath(
                        serverPath
                    ),
                NameCellStyleIndex:
                    nameCell?.StyleIndex?.Value
            );

            members.Add(member);
        }

        return new AggregationSettings(
            Year: year,
            Month: month,
            MonthlySheetName:
                $"{year}-{month}月度",
            Members: members
        );
    }

    private static void ValidateSettingsSheet(
        DefinedNameReference yearReference,
        DefinedNameReference monthReference,
        DefinedNameReference serverPathReference,
        DefinedNameReference nameReference,
        DefinedNameReference employeeNumberReference,
        DefinedNameReference fullNameReference)
    {
        var references = new[]
        {
            yearReference,
            monthReference,
            serverPathReference,
            nameReference,
            employeeNumberReference,
            fullNameReference,
        };

        foreach (var reference in references)
        {
            if (!string.Equals(
                    reference.SheetName,
                    SettingsSheetName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "名前付き範囲が設定シート以外を" +
                    "参照しています: " +
                    $"{reference.SheetName}!" +
                    reference.CellReference
                );
            }
        }
    }

    private static int ReadInteger(
        WorkbookPart workbookPart,
        WorksheetPart worksheetPart,
        string cellReference,
        string itemName)
    {
        var text = ExcelCellReader.GetCellText(
            workbookPart,
            ExcelCellReader.GetCell(
                worksheetPart,
                cellReference
            )
        ).Trim();

        if (int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return value;
        }

        if (double.TryParse(
                text,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var doubleValue))
        {
            return Convert.ToInt32(
                doubleValue
            );
        }

        throw new InvalidDataException(
            $"{itemName}を数値へ変換できません: " +
            $"{cellReference} = {text}"
        );
    }

    private static int FindLastUsedRow(
        WorkbookPart workbookPart,
        WorksheetPart worksheetPart,
        int columnIndex,
        int startRow)
    {
        var lastRow = startRow - 1;

        var sheetData =
            worksheetPart.Worksheet.GetFirstChild<
                DocumentFormat.OpenXml.Spreadsheet.SheetData
            >();

        if (sheetData is null)
        {
            return lastRow;
        }

        foreach (var row in sheetData.Elements<
                     DocumentFormat.OpenXml.Spreadsheet.Row
                 >())
        {
            var rowIndex =
                checked((int)(row.RowIndex?.Value ?? 0));

            if (rowIndex < startRow)
            {
                continue;
            }

            var cell =
                ExcelCellReader.GetCell(
                    worksheetPart,
                    (uint)rowIndex,
                    columnIndex
                );

            var value =
                ExcelCellReader.GetCellText(
                    workbookPart,
                    cell
                );

            if (!string.IsNullOrWhiteSpace(value))
            {
                lastRow = Math.Max(
                    lastRow,
                    rowIndex
                );
            }
        }

        return lastRow;
    }

    private static int GetColumnIndex(
        string cellReference)
    {
        var match = Regex.Match(
            cellReference,
            "^[A-Za-z]+"
        );

        if (!match.Success)
        {
            throw new InvalidDataException(
                $"セル参照から列を取得できません: " +
                cellReference
            );
        }

        var result = 0;

        foreach (var character
                 in match.Value.ToUpperInvariant())
        {
            result =
                result * 26 +
                character - 'A' + 1;
        }

        return result;
    }

    private static int GetRowIndex(
        string cellReference)
    {
        var match = Regex.Match(
            cellReference,
            "[0-9]+$"
        );

        if (!match.Success ||
            !int.TryParse(
                match.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var rowIndex))
        {
            throw new InvalidDataException(
                $"セル参照から行を取得できません: " +
                cellReference
            );
        }

        return rowIndex;
    }

    private static string NormalizeWindowsPath(
        string path)
    {
        var normalized =
            path.Replace(
                '/',
                Path.DirectorySeparatorChar
            );

        if (normalized.StartsWith(
                @"\\",
                StringComparison.Ordinal))
        {
            return normalized;
        }

        return normalized;
    }
}

public sealed record AggregationSettings(
    int Year,
    int Month,
    string MonthlySheetName,
    IReadOnlyList<AggregationMember> Members
);

public sealed record AggregationMember(
    int RowNumber,
    string Name,
    string EmployeeNumber,
    string FullName,
    string WorkbookPath,
    uint? NameCellStyleIndex
);