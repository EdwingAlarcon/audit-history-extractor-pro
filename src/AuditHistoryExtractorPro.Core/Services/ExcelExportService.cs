using AuditHistoryExtractorPro.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace AuditHistoryExtractorPro.Core.Services;

public class ExcelExportService : IExcelExportService
{
    private readonly IMetadataTranslationService _metadataTranslationService;

    public ExcelExportService(IMetadataTranslationService metadataTranslationService)
    {
        _metadataTranslationService = metadataTranslationService;
    }

    public async Task ExportAsync(
        string outputFilePath,
        IAsyncEnumerable<AuditExportRow> rows,
        AuditComparisonResult? comparisonResult = null,
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

        if (comparisonResult is not null)
        {
            var validationWorksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            using var validationWriter = OpenXmlWriter.Create(validationWorksheetPart);
            WriteValidationSheet(validationWriter, comparisonResult);

            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(validationWorksheetPart),
                SheetId = 2,
                Name = "Validación"
            });
        }

        workbookPart.Workbook.Save();
    }

    private static void WriteValidationSheet(OpenXmlWriter writer, AuditComparisonResult comparison)
    {
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());

        WriteKeyValueRow(writer, "LegacyTotal", comparison.LegacyTotal.ToString());
        WriteKeyValueRow(writer, "CurrentTotal", comparison.CurrentTotal.ToString());
        WriteKeyValueRow(writer, "MissingInNewCount", comparison.MissingInNewCount.ToString());
        WriteKeyValueRow(writer, "ValueDifferenceCount", comparison.ValueDifferenceCount.ToString());
        WriteKeyValueRow(writer, "EntityCountDifferences", comparison.EntityCountDifferences.Count.ToString());
        writer.WriteStartElement(new Row());
        writer.WriteEndElement();

        writer.WriteStartElement(new Row());
        WriteCell(writer, "Discrepancies", 3);
        writer.WriteEndElement();

        writer.WriteStartElement(new Row());
        WriteCell(writer, "Type", 3);
        WriteCell(writer, "EntityName", 3);
        WriteCell(writer, "AuditId", 3);
        WriteCell(writer, "ObjectId", 3);
        WriteCell(writer, "AttributeName", 3);
        WriteCell(writer, "LegacyOldValue", 3);
        WriteCell(writer, "LegacyNewValue", 3);
        WriteCell(writer, "CurrentOldValue", 3);
        WriteCell(writer, "CurrentNewValue", 3);
        writer.WriteEndElement();

        foreach (var discrepancy in comparison.Discrepancies)
        {
            writer.WriteStartElement(new Row());
            WriteCell(writer, discrepancy.Type, 0);
            WriteCell(writer, discrepancy.EntityName, 0);
            WriteCell(writer, discrepancy.AuditId, 0);
            WriteCell(writer, discrepancy.ObjectId, 0);
            WriteCell(writer, discrepancy.AttributeName, 0);
            WriteCell(writer, discrepancy.LegacyOldValue, 0);
            WriteCell(writer, discrepancy.LegacyNewValue, 0);
            WriteCell(writer, discrepancy.CurrentOldValue, 0);
            WriteCell(writer, discrepancy.CurrentNewValue, 0);
            writer.WriteEndElement();
        }

        writer.WriteStartElement(new Row());
        writer.WriteEndElement();

        writer.WriteStartElement(new Row());
        WriteCell(writer, "EntityCountDifferences", 3);
        writer.WriteEndElement();

        writer.WriteStartElement(new Row());
        WriteCell(writer, "EntityName", 3);
        WriteCell(writer, "LegacyCount", 3);
        WriteCell(writer, "CurrentCount", 3);
        WriteCell(writer, "Difference", 3);
        writer.WriteEndElement();

        foreach (var entityDiff in comparison.EntityCountDifferences)
        {
            writer.WriteStartElement(new Row());
            WriteCell(writer, entityDiff.EntityName, 0);
            WriteCell(writer, entityDiff.LegacyCount.ToString(), 0);
            WriteCell(writer, entityDiff.CurrentCount.ToString(), 0);
            WriteCell(writer, entityDiff.Difference.ToString(), 0);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteKeyValueRow(OpenXmlWriter writer, string key, string value)
    {
        writer.WriteStartElement(new Row());
        WriteCell(writer, key, 3);
        WriteCell(writer, value, 0);
        writer.WriteEndElement();
    }

    private static void WriteHeader(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new Row());
        // Orden exacto requerido: compatible con el esquema de la app original.
        var headers = new[]
        {
            "RecordId",        // objectid del registro auditado
            "AuditId",         // auditid
            "EntityId",        // alias de RecordId
            "ActionId",        // código numérico de 'action'
            "OperationId",     // código numérico de 'operation'
            "OldValue",        // valor anterior (legible)
            "NewValue",        // valor nuevo (legible)
            "CreatedOn",       // fecha/hora del evento (hora local)
            "EntityName",      // nombre lógico de la entidad
            "UserId",          // GUID del usuario
            "AttributeName",   // campo modificado
            "RecordKeyValue",  // primary-name del registro auditado
            "Action",          // nombre de la acción (Create, Update, Delete...)
            "Operation",       // nombre de la operación DML
            "Username",        // nombre completo del usuario
            "LookupOldValue",  // Name del EntityReference anterior
            "LookupNewValue"   // Name del EntityReference nuevo
        };

        foreach (var header in headers)
        {
            WriteCell(writer, header, 3);
        }

        writer.WriteEndElement();
    }

    private void WriteDataRow(OpenXmlWriter writer, AuditExportRow row)
    {
        // Color por ActionCode: 1=Create(verde), 3=Delete(rojo), resto=sin relleno.
        var rowStyle = row.ActionCode switch
        {
            1 => 1u,   // Create  → verde (E2F0D9)
            3 => 2u,   // Delete  → rojo   (FCE4D6)
            _ => 0u
        };

        string translatedOldValue;
        string translatedNewValue;
        try
        {
            translatedOldValue = _metadataTranslationService.TranslateValue(row.LogicalName, row.ChangedField, row.OldValue);
            translatedNewValue = _metadataTranslationService.TranslateValue(row.LogicalName, row.ChangedField, row.NewValue);
        }
        catch
        {
            translatedOldValue = row.OldValue;
            translatedNewValue = row.NewValue;
        }

        // Columna NewValue con bold en filas de Update para resaltar el cambio.
        var newValueStyle = row.ActionCode == 2 ? 3u : rowStyle;

        writer.WriteStartElement(new Row());

        // Orden: RecordId, AuditId, EntityId, ActionId, OperationId, OldValue, NewValue,
        //        CreatedOn, EntityName, UserId, AttributeName, RecordKeyValue,
        //        Action, Operation, Username, LookupOldValue, LookupNewValue
        WriteCell(writer, row.RecordId,                           rowStyle);
        WriteCell(writer, row.AuditId,                            rowStyle);
        WriteCell(writer, row.EntityId,                           rowStyle);
        WriteCell(writer, row.ActionId.ToString(),                rowStyle);
        WriteCell(writer, row.OperationId.ToString(),             rowStyle);
        WriteCell(writer, translatedOldValue,                     rowStyle);
        WriteCell(writer, translatedNewValue,                     newValueStyle);
        WriteCell(writer, row.CreatedOn,                          rowStyle);
        WriteCell(writer, row.EntityName,                         rowStyle);
        WriteCell(writer, row.UserId,                             rowStyle);
        WriteCell(writer, row.AttributeName,                      rowStyle);
        WriteCell(writer, row.RecordKeyValue,                     rowStyle);
        WriteCell(writer, row.Action,                             rowStyle);
        WriteCell(writer, row.Operation,                          rowStyle);
        WriteCell(writer, row.Username,                           rowStyle);
        WriteCell(writer, row.LookupOldValue,                     rowStyle);
        WriteCell(writer, row.LookupNewValue,                     newValueStyle);

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
