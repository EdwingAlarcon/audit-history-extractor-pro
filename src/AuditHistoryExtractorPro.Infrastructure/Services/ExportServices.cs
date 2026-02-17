using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AuditHistoryExtractorPro.Infrastructure.Services.Export;

/// <summary>
/// Servicio de exportación a Excel
/// </summary>
public class ExcelExportService : IExportService
{
    private readonly ILogger<ExcelExportService> _logger;

    public ExcelExportService(ILogger<ExcelExportService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExportAsync(
        List<AuditRecord> records,
        ExportConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fileName = BuildFileName(configuration);
            var fullPath = Path.Combine(configuration.OutputPath, fileName);

            Directory.CreateDirectory(configuration.OutputPath);

            using var workbook = new XLWorkbook();
            
            // Crear hoja de resumen
            CreateSummarySheet(workbook, records);
            
            // Crear hoja con registros de auditoría
            CreateAuditRecordsSheet(workbook, records);
            
            // Crear hoja con cambios de campos
            CreateFieldChangesSheet(workbook, records);

            await Task.Run(() => workbook.SaveAs(fullPath), cancellationToken);

            _logger.LogInformation("Excel export completed: {FilePath}", fullPath);

            // Comprimir si está configurado
            if (configuration.CompressOutput)
            {
                return await CompressFileAsync(fullPath, cancellationToken);
            }

            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to Excel");
            throw;
        }
    }

    private void CreateSummarySheet(IXLWorkbook workbook, List<AuditRecord> records)
    {
        var ws = workbook.Worksheets.Add("Summary");
        
        ws.Cell("A1").Value = "Audit Export Summary";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;

        var row = 3;
        ws.Cell($"A{row}").Value = "Total Records:";
        ws.Cell($"B{row}").Value = records.Count;
        row++;

        ws.Cell($"A{row}").Value = "Export Date:";
        ws.Cell($"B{row}").Value = DateTime.Now;
        row++;

        ws.Cell($"A{row}").Value = "Date Range:";
        ws.Cell($"B{row}").Value = $"{records.Min(r => r.CreatedOn):yyyy-MM-dd} to {records.Max(r => r.CreatedOn):yyyy-MM-dd}";
        row += 2;

        ws.Cell($"A{row}").Value = "Records by Entity";
        ws.Cell($"A{row}").Style.Font.Bold = true;
        row++;

        foreach (var group in records.GroupBy(r => r.EntityName))
        {
            ws.Cell($"A{row}").Value = group.Key;
            ws.Cell($"B{row}").Value = group.Count();
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private void CreateAuditRecordsSheet(IXLWorkbook workbook, List<AuditRecord> records)
    {
        var ws = workbook.Worksheets.Add("Audit Records");
        
        // Encabezados
        ws.Cell("A1").Value = "Audit ID";
        ws.Cell("B1").Value = "Created On";
        ws.Cell("C1").Value = "Entity";
        ws.Cell("D1").Value = "Record ID";
        ws.Cell("E1").Value = "Operation";
        ws.Cell("F1").Value = "User Name";
        ws.Cell("G1").Value = "Transaction ID";
        ws.Cell("H1").Value = "Changes Count";

        ws.Range("A1:H1").Style.Font.Bold = true;
        ws.Range("A1:H1").Style.Fill.BackgroundColor = XLColor.LightBlue;

        // Datos
        var row = 2;
        foreach (var record in records)
        {
            ws.Cell($"A{row}").Value = record.AuditId.ToString();
            ws.Cell($"B{row}").Value = record.CreatedOn;
            ws.Cell($"C{row}").Value = record.EntityName;
            ws.Cell($"D{row}").Value = record.RecordId.ToString();
            ws.Cell($"E{row}").Value = record.Operation;
            ws.Cell($"F{row}").Value = record.UserName;
            ws.Cell($"G{row}").Value = record.TransactionId;
            ws.Cell($"H{row}").Value = record.Changes.Count;
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    private void CreateFieldChangesSheet(IXLWorkbook workbook, List<AuditRecord> records)
    {
        var ws = workbook.Worksheets.Add("Field Changes");
        
        // Encabezados
        ws.Cell("A1").Value = "Audit ID";
        ws.Cell("B1").Value = "Created On";
        ws.Cell("C1").Value = "Entity";
        ws.Cell("D1").Value = "Record ID";
        ws.Cell("E1").Value = "Field Name";
        ws.Cell("F1").Value = "Old Value";
        ws.Cell("G1").Value = "New Value";
        ws.Cell("H1").Value = "Change Description";

        ws.Range("A1:H1").Style.Font.Bold = true;
        ws.Range("A1:H1").Style.Fill.BackgroundColor = XLColor.LightGreen;

        // Datos
        var row = 2;
        foreach (var record in records)
        {
            foreach (var change in record.Changes.Values)
            {
                ws.Cell($"A{row}").Value = record.AuditId.ToString();
                ws.Cell($"B{row}").Value = record.CreatedOn;
                ws.Cell($"C{row}").Value = record.EntityName;
                ws.Cell($"D{row}").Value = record.RecordId.ToString();
                ws.Cell($"E{row}").Value = change.FieldName;
                ws.Cell($"F{row}").Value = change.OldValue;
                ws.Cell($"G{row}").Value = change.NewValue;
                ws.Cell($"H{row}").Value = change.GetChangeDescription();
                row++;
            }
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    public Task<bool> SendToDestinationAsync(
        string filePath,
        ExportDestination destination,
        CancellationToken cancellationToken = default)
    {
        // Implementación delegada a servicios específicos
        return Task.FromResult(false);
    }

    public bool SupportsFormat(ExportFormat format) => format == ExportFormat.Excel;

    private string BuildFileName(ExportConfiguration configuration)
    {
        var fileName = configuration.FileName;
        
        if (configuration.IncludeTimestamp)
        {
            fileName += $"_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        return fileName + ".xlsx";
    }

    private async Task<string> CompressFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var zipPath = Path.ChangeExtension(filePath, ".zip");
        
        using var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        zipArchive.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
        
        // Eliminar archivo original
        File.Delete(filePath);
        
        return zipPath;
    }
}

/// <summary>
/// Servicio de exportación a CSV
/// </summary>
public class CsvExportService : IExportService
{
    private readonly ILogger<CsvExportService> _logger;

    public CsvExportService(ILogger<CsvExportService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExportAsync(
        List<AuditRecord> records,
        ExportConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fileName = BuildFileName(configuration);
            var fullPath = Path.Combine(configuration.OutputPath, fileName);

            Directory.CreateDirectory(configuration.OutputPath);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = ","
            };

            using var writer = new StreamWriter(fullPath, false, Encoding.UTF8);
            using var csv = new CsvWriter(writer, config);

            // Escribir encabezados
            csv.WriteField("AuditId");
            csv.WriteField("CreatedOn");
            csv.WriteField("EntityName");
            csv.WriteField("RecordId");
            csv.WriteField("Operation");
            csv.WriteField("UserName");
            csv.WriteField("TransactionId");
            csv.WriteField("FieldName");
            csv.WriteField("OldValue");
            csv.WriteField("NewValue");
            await csv.NextRecordAsync();

            // Escribir datos
            foreach (var record in records)
            {
                if (record.Changes.Any())
                {
                    foreach (var change in record.Changes.Values)
                    {
                        csv.WriteField(record.AuditId);
                        csv.WriteField(record.CreatedOn);
                        csv.WriteField(record.EntityName);
                        csv.WriteField(record.RecordId);
                        csv.WriteField(record.Operation);
                        csv.WriteField(record.UserName);
                        csv.WriteField(record.TransactionId);
                        csv.WriteField(change.FieldName);
                        csv.WriteField(change.OldValue);
                        csv.WriteField(change.NewValue);
                        await csv.NextRecordAsync();
                    }
                }
                else
                {
                    csv.WriteField(record.AuditId);
                    csv.WriteField(record.CreatedOn);
                    csv.WriteField(record.EntityName);
                    csv.WriteField(record.RecordId);
                    csv.WriteField(record.Operation);
                    csv.WriteField(record.UserName);
                    csv.WriteField(record.TransactionId);
                    csv.WriteField("");
                    csv.WriteField("");
                    csv.WriteField("");
                    await csv.NextRecordAsync();
                }
            }

            _logger.LogInformation("CSV export completed: {FilePath}", fullPath);

            if (configuration.CompressOutput)
            {
                return await CompressFileAsync(fullPath, cancellationToken);
            }

            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to CSV");
            throw;
        }
    }

    public Task<bool> SendToDestinationAsync(
        string filePath,
        ExportDestination destination,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public bool SupportsFormat(ExportFormat format) => format == ExportFormat.Csv;

    private string BuildFileName(ExportConfiguration configuration)
    {
        var fileName = configuration.FileName;
        
        if (configuration.IncludeTimestamp)
        {
            fileName += $"_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        return fileName + ".csv";
    }

    private async Task<string> CompressFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var zipPath = Path.ChangeExtension(filePath, ".zip");
        
        using var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        zipArchive.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
        
        File.Delete(filePath);
        
        return zipPath;
    }
}

/// <summary>
/// Servicio de exportación a JSON
/// </summary>
public class JsonExportService : IExportService
{
    private readonly ILogger<JsonExportService> _logger;

    public JsonExportService(ILogger<JsonExportService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExportAsync(
        List<AuditRecord> records,
        ExportConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fileName = BuildFileName(configuration);
            var fullPath = Path.Combine(configuration.OutputPath, fileName);

            Directory.CreateDirectory(configuration.OutputPath);

            var json = JsonConvert.SerializeObject(records, Formatting.Indented);
            await File.WriteAllTextAsync(fullPath, json, cancellationToken);

            _logger.LogInformation("JSON export completed: {FilePath}", fullPath);

            if (configuration.CompressOutput)
            {
                return await CompressFileAsync(fullPath, cancellationToken);
            }

            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to JSON");
            throw;
        }
    }

    public Task<bool> SendToDestinationAsync(
        string filePath,
        ExportDestination destination,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public bool SupportsFormat(ExportFormat format) => format == ExportFormat.Json;

    private string BuildFileName(ExportConfiguration configuration)
    {
        var fileName = configuration.FileName;
        
        if (configuration.IncludeTimestamp)
        {
            fileName += $"_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        return fileName + ".json";
    }

    private async Task<string> CompressFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var zipPath = Path.ChangeExtension(filePath, ".zip");
        
        using var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        zipArchive.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
        
        File.Delete(filePath);
        
        return zipPath;
    }
}

/// <summary>
/// Servicio de exportación compuesto que elige el exportador apropiado
/// </summary>
public class CompositeExportService : IExportService
{
    private readonly Dictionary<ExportFormat, IExportService> _exporters;
    private readonly ILogger<CompositeExportService> _logger;

    public CompositeExportService(
        IEnumerable<IExportService> exportServices,
        ILogger<CompositeExportService> logger)
    {
        _exporters = new Dictionary<ExportFormat, IExportService>();
        _logger = logger;

        foreach (var service in exportServices)
        {
            if (service is not CompositeExportService)
            {
                foreach (ExportFormat format in Enum.GetValues<ExportFormat>())
                {
                    if (service.SupportsFormat(format))
                    {
                        _exporters[format] = service;
                    }
                }
            }
        }
    }

    public async Task<string> ExportAsync(
        List<AuditRecord> records,
        ExportConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!_exporters.TryGetValue(configuration.Format, out var exporter))
        {
            throw new NotSupportedException($"Export format {configuration.Format} is not supported");
        }

        _logger.LogInformation("Exporting to {Format}", configuration.Format);
        return await exporter.ExportAsync(records, configuration, cancellationToken);
    }

    public async Task<bool> SendToDestinationAsync(
        string filePath,
        ExportDestination destination,
        CancellationToken cancellationToken = default)
    {
        // Aquí se implementaría la lógica para enviar a diferentes destinos
        // Por ahora retornamos false
        return await Task.FromResult(false);
    }

    public bool SupportsFormat(ExportFormat format) => _exporters.ContainsKey(format);
}
