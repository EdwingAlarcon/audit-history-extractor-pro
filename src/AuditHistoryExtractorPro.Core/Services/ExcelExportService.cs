using AuditHistoryExtractorPro.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace AuditHistoryExtractorPro.Core.Services;

public class ExcelExportService : IExcelExportService
{
    public async Task ExportAsync(
        string outputFilePath,
        IAsyncEnumerable<AuditExportRow> rows,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var spreadsheetDocument = SpreadsheetDocument.Create(outputFilePath, SpreadsheetDocumentType.Workbook);
        var workbookPart = spreadsheetDocument.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateStylesheet();
        stylesPart.Stylesheet.Save();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        using var writer = OpenXmlWriter.Create(worksheetPart);

        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());

        WriteHeader(writer);

        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteDataRow(writer, row);
        }

        writer.WriteEndElement();
        writer.WriteEndElement();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Audit"
        });

        workbookPart.Workbook.Save();
    }

    private static void WriteHeader(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new Row());
        var headers = new[]
        {
            "AuditId", "CreatedOn", "EntityName", "RecordId", "ActionCode", "ActionName", "UserId", "UserName", "TransactionId", "ChangedField", "OldValue", "NewValue"
        };

        foreach (var header in headers)
        {
            WriteCell(writer, header, 3);
        }

        writer.WriteEndElement();
    }

    private static void WriteDataRow(OpenXmlWriter writer, AuditExportRow row)
    {
        var rowStyle = row.ActionName switch
        {
            "Create" => 1u,
            "Delete" => 2u,
            _ => 0u
        };

        writer.WriteStartElement(new Row());

        WriteCell(writer, row.AuditId, rowStyle);
        WriteCell(writer, row.CreatedOn, rowStyle);
        WriteCell(writer, row.EntityName, rowStyle);
        WriteCell(writer, row.RecordId, rowStyle);
        WriteCell(writer, row.ActionCode.ToString(), rowStyle);
        WriteCell(writer, row.ActionName, rowStyle);
        WriteCell(writer, row.UserId, rowStyle);
        WriteCell(writer, row.UserName, rowStyle);
        WriteCell(writer, row.TransactionId, rowStyle);
        WriteCell(writer, row.ChangedField, rowStyle);
        WriteCell(writer, row.OldValue, rowStyle);

        var changedValueStyle = row.ActionName == "Update" ? 3u : rowStyle;
        WriteCell(writer, row.NewValue, changedValueStyle);

        writer.WriteEndElement();
    }

    private static void WriteCell(OpenXmlWriter writer, string value, uint styleIndex)
    {
        writer.WriteElement(new Cell
        {
            DataType = CellValues.InlineString,
            StyleIndex = styleIndex,
            InlineString = new InlineString(new Text(value ?? string.Empty))
        });
    }

    private static Stylesheet CreateStylesheet()
    {
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(
                new ForegroundColor { Rgb = HexBinaryValue.FromString("E2F0D9") },
                new BackgroundColor { Indexed = 64 })
            { PatternType = PatternValues.Solid }),
            new Fill(new PatternFill(
                new ForegroundColor { Rgb = HexBinaryValue.FromString("FCE4D6") },
                new BackgroundColor { Indexed = 64 })
            { PatternType = PatternValues.Solid })
        );

        var fonts = new Fonts(
            new Font(),
            new Font(new Bold())
        );

        var borders = new Borders(new Border());

        var cellFormats = new CellFormats(
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0, ApplyFill = true },
            new CellFormat { FontId = 0, FillId = 2, BorderId = 0, ApplyFill = true },
            new CellFormat { FontId = 0, FillId = 3, BorderId = 0, ApplyFill = true },
            new CellFormat { FontId = 1, FillId = 0, BorderId = 0, ApplyFont = true }
        );

        return new Stylesheet(fonts, fills, borders, cellFormats);
    }
}
