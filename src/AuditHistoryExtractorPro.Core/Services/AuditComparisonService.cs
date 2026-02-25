using AuditHistoryExtractorPro.Core.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AuditHistoryExtractorPro.Core.Services;

public sealed class AuditComparisonService : IAuditComparisonService
{
    private readonly ILogger<AuditComparisonService> _logger;

    public AuditComparisonService(ILogger<AuditComparisonService> logger)
    {
        _logger = logger;
    }

    public Task<AuditComparisonResult> CompareWithLegacyAsync(
        string legacyExcelPath,
        IReadOnlyList<AuditExportRow> currentRows,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(legacyExcelPath) || !File.Exists(legacyExcelPath))
        {
            throw new FileNotFoundException("No se encontró el archivo Excel legacy para cotejo.", legacyExcelPath);
        }

        var legacyRows = LoadLegacyRows(legacyExcelPath, cancellationToken);

        var legacyByKey = legacyRows
            .GroupBy(GetCompositeKey)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var currentByKey = currentRows
            .Select(r => new ComparisonRow
            {
                AuditId = Normalize(r.AuditId),
                ObjectId = Normalize(r.RecordId),
                AttributeName = Normalize(r.AttributeName),
                OldValue = Normalize(r.OldValue),
                NewValue = Normalize(r.NewValue),
                EntityName = Normalize(r.EntityName)
            })
            .GroupBy(GetCompositeKey)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var discrepancies = new List<AuditComparisonDiscrepancy>();
        var missingInNew = 0;
        var valueDifferences = 0;

        foreach (var legacyEntry in legacyByKey)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!currentByKey.TryGetValue(legacyEntry.Key, out var currentCandidates))
            {
                foreach (var missing in legacyEntry.Value)
                {
                    missingInNew++;
                    discrepancies.Add(new AuditComparisonDiscrepancy
                    {
                        Type = "MissingInNew",
                        AuditId = missing.AuditId,
                        ObjectId = missing.ObjectId,
                        AttributeName = missing.AttributeName,
                        LegacyOldValue = missing.OldValue,
                        LegacyNewValue = missing.NewValue,
                        EntityName = missing.EntityName
                    });
                }

                continue;
            }

            var currentPool = new List<ComparisonRow>(currentCandidates);
            foreach (var legacyRow in legacyEntry.Value)
            {
                var exactMatchIndex = currentPool.FindIndex(c =>
                    string.Equals(c.OldValue, legacyRow.OldValue, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.NewValue, legacyRow.NewValue, StringComparison.OrdinalIgnoreCase));

                if (exactMatchIndex >= 0)
                {
                    currentPool.RemoveAt(exactMatchIndex);
                    continue;
                }

                valueDifferences++;
                var firstCurrent = currentPool.FirstOrDefault();
                if (firstCurrent is not null)
                {
                    currentPool.RemoveAt(0);
                }

                discrepancies.Add(new AuditComparisonDiscrepancy
                {
                    Type = "ValueDifference",
                    AuditId = legacyRow.AuditId,
                    ObjectId = legacyRow.ObjectId,
                    AttributeName = legacyRow.AttributeName,
                    LegacyOldValue = legacyRow.OldValue,
                    LegacyNewValue = legacyRow.NewValue,
                    CurrentOldValue = firstCurrent?.OldValue ?? string.Empty,
                    CurrentNewValue = firstCurrent?.NewValue ?? string.Empty,
                    EntityName = legacyRow.EntityName
                });
            }
        }

        var legacyEntityCounts = legacyRows
            .GroupBy(r => Normalize(r.EntityName))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var currentEntityCounts = currentRows
            .GroupBy(r => Normalize(r.EntityName))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var allEntities = legacyEntityCounts.Keys
            .Union(currentEntityCounts.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var countDiffs = new List<EntityCountDifference>();
        foreach (var entity in allEntities)
        {
            var legacyCount = legacyEntityCounts.TryGetValue(entity, out var lc) ? lc : 0;
            var currentCount = currentEntityCounts.TryGetValue(entity, out var cc) ? cc : 0;
            if (legacyCount != currentCount)
            {
                countDiffs.Add(new EntityCountDifference
                {
                    EntityName = entity,
                    LegacyCount = legacyCount,
                    CurrentCount = currentCount
                });
            }
        }

        var result = new AuditComparisonResult
        {
            LegacyTotal = legacyRows.Count,
            CurrentTotal = currentRows.Count,
            MissingInNewCount = missingInNew,
            ValueDifferenceCount = valueDifferences,
            Discrepancies = discrepancies,
            EntityCountDifferences = countDiffs
        };

        _logger.LogInformation(
            "[AuditComparison] LegacyTotal={LegacyTotal} CurrentTotal={CurrentTotal} MissingInNew={MissingInNew} ValueDiffs={ValueDiffs} EntityCountDiffs={EntityCountDiffs}",
            result.LegacyTotal,
            result.CurrentTotal,
            result.MissingInNewCount,
            result.ValueDifferenceCount,
            result.EntityCountDifferences.Count);

        return Task.FromResult(result);
    }

    private static string GetCompositeKey(ComparisonRow row)
        => $"{Normalize(row.AuditId)}|{Normalize(row.ObjectId)}|{Normalize(row.AttributeName)}";

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim();

    private List<ComparisonRow> LoadLegacyRows(string excelPath, CancellationToken cancellationToken)
    {
        using var document = SpreadsheetDocument.Open(excelPath, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidOperationException("Workbook inválido.");
        var firstSheet = workbookPart.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault()
            ?? throw new InvalidOperationException("El Excel legacy no contiene hojas.");

        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(firstSheet.Id!);
        var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>()
            ?? throw new InvalidOperationException("La hoja legacy no contiene datos.");

        var rows = sheetData.Elements<Row>().ToList();
        if (rows.Count == 0)
        {
            return new List<ComparisonRow>();
        }

        var headers = ParseRow(rows[0], workbookPart)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        int? GetIndex(params string[] names)
        {
            foreach (var name in names)
            {
                var found = headers.FirstOrDefault(h => string.Equals(h.Value, name, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(found.Value)) return found.Key;
            }

            return null;
        }

        var idxAuditId = GetIndex("AuditId");
        var idxObjectId = GetIndex("RecordId", "Record ID", "ObjectId", "EntityId");
        var idxAttr = GetIndex("AttributeName", "ChangedField", "Campo");
        var idxOld = GetIndex("OldValue", "Valor Anterior");
        var idxNew = GetIndex("NewValue", "Valor Nuevo");
        var idxEntity = GetIndex("EntityName", "LogicalName", "Entidad");

        if (!idxAuditId.HasValue || !idxObjectId.HasValue || !idxAttr.HasValue)
        {
            throw new InvalidOperationException("No se pudieron mapear las columnas mínimas del Excel legacy (AuditId/ObjectId/AttributeName).");
        }

        var result = new List<ComparisonRow>(Math.Max(0, rows.Count - 1));

        foreach (var row in rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = ParseRow(row, workbookPart);

            string Read(int? index) => index.HasValue && values.TryGetValue(index.Value, out var value)
                ? value
                : string.Empty;

            result.Add(new ComparisonRow
            {
                AuditId = Read(idxAuditId),
                ObjectId = Read(idxObjectId),
                AttributeName = Read(idxAttr),
                OldValue = Read(idxOld),
                NewValue = Read(idxNew),
                EntityName = Read(idxEntity)
            });
        }

        return result;
    }

    private static Dictionary<int, string> ParseRow(Row row, WorkbookPart workbookPart)
    {
        var map = new Dictionary<int, string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var columnIndex = GetColumnIndexFromReference(cell.CellReference?.Value);
            map[columnIndex] = GetCellValue(cell, workbookPart);
        }

        return map;
    }

    private static string GetCellValue(Cell cell, WorkbookPart workbookPart)
    {
        var value = cell.CellValue?.Text ?? string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(value, out var sharedIndex)
            && workbookPart.SharedStringTablePart?.SharedStringTable is { } table)
        {
            return table.ElementAt(sharedIndex).InnerText;
        }

        if (cell.InlineString?.Text is { } inline)
        {
            return inline.Text;
        }

        return value;
    }

    private static int GetColumnIndexFromReference(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference)) return 0;

        var match = Regex.Match(cellReference, "[A-Z]+", RegexOptions.IgnoreCase);
        if (!match.Success) return 0;

        var letters = match.Value.ToUpperInvariant();
        var sum = 0;
        foreach (var c in letters)
        {
            sum *= 26;
            sum += c - 'A' + 1;
        }

        return sum - 1;
    }

    private sealed class ComparisonRow
    {
        public string AuditId { get; init; } = string.Empty;
        public string ObjectId { get; init; } = string.Empty;
        public string AttributeName { get; init; } = string.Empty;
        public string OldValue { get; init; } = string.Empty;
        public string NewValue { get; init; } = string.Empty;
        public string EntityName { get; init; } = string.Empty;
    }
}
