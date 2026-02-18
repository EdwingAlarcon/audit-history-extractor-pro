using AuditHistoryExtractorPro.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AuditHistoryExtractorPro.Services.Export;

/// <summary>
/// Servicio de exportación a CSV optimizado para Power BI
/// Características Enterprise-Grade:
/// - Fechas en ISO 8601 (YYYY-MM-DDTHH:MM:SS.SSSZ)
/// - Resolución de Display Names para atributos
/// - Resolución de OptionSet Values a etiquetas legibles
/// - Estructura columnar optimizada para Power BI
/// - UTF-8 con BOM para compatibilidad Excel/Power Query
/// </summary>
public class PowerBIOptimizedCsvExportService : IExportService
{
    private readonly ILogger<PowerBIOptimizedCsvExportService> _logger;
    private readonly IMetadataResolutionService _metadataService;

    public PowerBIOptimizedCsvExportService(
        ILogger<PowerBIOptimizedCsvExportService> logger,
        IMetadataResolutionService metadataService)
    {
        _logger = logger;
        _metadataService = metadataService;
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

            // Usar TextWriter con UTF-8 BOM para Excel/Power Query
            using var writer = new StreamWriter(
                fullPath,
                false,
                new UTF8Encoding(true)); // true = agregar BOM

            using var csv = new CsvWriter(writer, GetCsvConfiguration());

            _logger.LogInformation(
                "Starting CSV export for Power BI: {FilePath}. Records: {Count}",
                fullPath,
                records.Count);

            // ⭐ NUEVO: Precargar metadatos para todas las entidades
            // Esto evita 50,000 llamadas a RetrieveMetadata durante la exportación
            var entities = records.Select(r => r.EntityName).Distinct();
            foreach (var entity in entities)
            {
                await _metadataService.PreloadEntityMetadataAsync(entity, cancellationToken);
            }

            // Escribir encabezados personalizados para Power BI
            await WriteHeadersForPowerBIAsync(csv);
            await csv.NextRecordAsync();

            // Escribir datos enriquecidos con metadatos resueltos
            var rowCount = 0;
            foreach (var record in records)
            {
                if (record.Changes.Any())
                {
                    foreach (var change in record.Changes.Values)
                    {
                        await WriteAuditRowAsync(
                            csv,
                            record,
                            change,
                            cancellationToken);
                        rowCount++;
                    }
                }
                else
                {
                    // Registros sin cambios específicos (Delete, Merge, etc.)
                    await WriteAuditRowAsync(
                        csv,
                        record,
                        null,
                        cancellationToken);
                    rowCount++;
                }

                // Log de progreso cada 10,000 registros
                if (rowCount % 10000 == 0)
                {
                    _logger.LogInformation(
                        "CSV export progress: {RowCount} rows written",
                        rowCount);
                }
            }

            await csv.FlushAsync();

            _logger.LogInformation(
                "CSV export completed successfully: {FilePath} ({RowCount} rows, {SizeKb}kb)",
                fullPath,
                rowCount,
                new FileInfo(fullPath).Length / 1024);

            if (configuration.CompressOutput)
            {
                return await CompressFileAsync(fullPath, cancellationToken);
            }

            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to CSV for Power BI");
            throw;
        }
    }

    public Task<bool> SendToDestinationAsync(
        string filePath,
        ExportDestination destination,
        CancellationToken cancellationToken = default)
    {
        // Implementación futura para enviar a diferentes destinos
        return Task.FromResult(false);
    }

    public bool SupportsFormat(ExportFormat format) => format == ExportFormat.Csv;

    // ============ Métodos Privados ============

    private async Task WriteHeadersForPowerBIAsync(IWriter csv)
    {
        // Categoría 1: Identidad y Auditoría
        csv.WriteField("AuditId", false);
        csv.WriteField("TransactionId", false);
        csv.WriteField("EntityName", false);
        csv.WriteField("RecordId", false);
        
        // Categoría 2: Temporal (ISO 8601 para Power BI)
        csv.WriteField("CreatedOnUtc", false);          // ISO 8601 completo
        csv.WriteField("CreatedOnDate", false);         // Fecha solo (para filtros)
        csv.WriteField("CreatedOnTime", false);         // Hora solo (para análisis)
        
        // Categoría 3: Cambio / Acción
        csv.WriteField("ActionCode", false);            // 1-28 (numérico)
        csv.WriteField("ActionName", false);            // Create, Update, Delete, etc.
        csv.WriteField("ActionCategory", false);        // CrudBasic, Security, SalesProcess, etc.
        csv.WriteField("FieldName", false);             // Nombre lógico del campo
        csv.WriteField("FieldDisplayName", false);      // Nombre legible (resuelto)
        
        // Categoría 4: Valores
        csv.WriteField("OldValue", false);              // Valor anterior
        csv.WriteField("NewValue", false);              // Valor nuevo
        csv.WriteField("FieldType", false);             // Tipo de atributo
        csv.WriteField("ChangeDescription", false);     // Descripción legible
        
        // Categoría 5: Usuario
        csv.WriteField("UserId", false);
        csv.WriteField("UserName", false);
        csv.WriteField("UserEmail", false);
    }

    private async Task WriteAuditRowAsync(
        IWriter csv,
        AuditRecord record,
        AuditFieldChange? change,
        CancellationToken cancellationToken)
    {
        // Categoría 1: Identidad
        csv.WriteField(record.AuditId.ToString("D"), false);
        csv.WriteField(record.TransactionId ?? "", false);
        csv.WriteField(record.EntityName, false);
        csv.WriteField(record.RecordId.ToString("D"), false);
        
        // Categoría 2: Temporal - ISO 8601 para Power BI
        // Format: 2024-02-17T14:30:45.123Z (compatible con Power BI, Excel, Tableau)
        csv.WriteField(record.CreatedOn.ToUniversalTime().ToString("O"), false);
        csv.WriteField(record.CreatedOn.ToString("yyyy-MM-dd"), false);
        csv.WriteField(record.CreatedOn.ToString("HH:mm:ss"), false);
        
        // Categoría 3: Cambio
        var actionCode = ParseActionCodeFromOperation(record.Operation);
        csv.WriteField((int)actionCode, false);
        csv.WriteField(record.Operation, false);
        csv.WriteField(GetActionCategory(actionCode), false);
        
        if (change != null)
        {
            csv.WriteField(change.FieldName, false);
            
            // ⭐ NUEVO: Resolver nombre de display del atributo
            var displayName = await _metadataService.ResolveAttributeDisplayNameAsync(
                record.EntityName,
                change.FieldName,
                cancellationToken);
            csv.WriteField(displayName, false);

            // Categoría 4: Valores
            csv.WriteField(change.OldValue ?? "", false);
            csv.WriteField(change.NewValue ?? "", false);
            csv.WriteField(change.FieldType, false);
            csv.WriteField(change.GetChangeDescription(), false);
        }
        else
        {
            // Sin cambios específicos de campo (operaciones que no tienen field changes)
            csv.WriteField("", false);
            csv.WriteField("", false);
            csv.WriteField("", false);
            csv.WriteField("", false);
            csv.WriteField("", false);
            csv.WriteField("", false);
        }
        
        // Categoría 5: Usuario
        csv.WriteField(record.UserId, false);
        csv.WriteField(record.UserName, false);
        csv.WriteField(
            record.AdditionalData.ContainsKey("UserEmail") 
                ? record.AdditionalData["UserEmail"]?.ToString() ?? ""
                : "",
            false);

        await csv.NextRecordAsync();
    }

    private CsvConfiguration GetCsvConfiguration()
    {
        return new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ",",
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,                    // No lanzar excepción con datos malformados
            MissingFieldFound = null,               // No lanzar excepción con campos faltantes
            NewLine = NewLine.CRLF,                 // Windows compatible (Excel estándar)
            Encoding = new UTF8Encoding(false),     // UTF-8 sin BOM en el contenido
            ShouldQuoteField = ShouldQuoteFieldFunc, // Función para determinar cuándo citar campos
        };
    }

    /// <summary>
    /// Determina si un campo debe ir entrecomillado en CSV
    /// Power BI y Excel son más laxos, pero es buena práctica citar valores complejos
    /// </summary>
    private static bool ShouldQuoteFieldFunc(
        string field,
        in WriteRecord context)
    {
        // Citar si contiene: coma, comilla, salto de línea
        if (field == null) return false;
        
        return field.Contains(',') ||
               field.Contains('"') ||
               field.Contains('\n') ||
               field.Contains('\r');
    }

    private AuditActionCode ParseActionCodeFromOperation(string operation)
    {
        return operation switch
        {
            "Create" => AuditActionCode.Create,
            "Update" => AuditActionCode.Update,
            "Delete" => AuditActionCode.Delete,
            "Associate" => AuditActionCode.Associate,
            "Disassociate" => AuditActionCode.Disassociate,
            "Assign" => AuditActionCode.Assign,
            "Share" => AuditActionCode.Share,
            "Unshare" => AuditActionCode.Unshare,
            "Merge" => AuditActionCode.Merge,
            "Reparent" => AuditActionCode.Reparent,
            "Qualify" => AuditActionCode.Qualify,
            "Disqualify" => AuditActionCode.Disqualify,
            "Win" => AuditActionCode.Win,
            "Lose" => AuditActionCode.Lose,
            "Deactivate" => AuditActionCode.Deactivate,
            "Activate" => AuditActionCode.Activate,
            "Archive" => AuditActionCode.Archive,
            "Restore" => AuditActionCode.Restore,
            _ => AuditActionCode.Update
        };
    }

    private string GetActionCategory(AuditActionCode actionCode)
    {
        return actionCode switch
        {
            AuditActionCode.Create or 
            AuditActionCode.Update or 
            AuditActionCode.Delete => "CrudBasic",
            
            AuditActionCode.Associate or 
            AuditActionCode.Disassociate => "Relational",
            
            AuditActionCode.Assign or 
            AuditActionCode.Share or 
            AuditActionCode.Unshare => "Security",
            
            AuditActionCode.Merge or 
            AuditActionCode.Reparent => "Operations",
            
            AuditActionCode.Qualify or 
            AuditActionCode.Disqualify or 
            AuditActionCode.Win or 
            AuditActionCode.Lose => "SalesProcess",
            
            AuditActionCode.Activate or 
            AuditActionCode.Deactivate => "StatusChange",
            
            AuditActionCode.Archive or 
            AuditActionCode.Restore => "Maintenance",
            
            _ => "Unknown"
        };
    }

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
        
        _logger.LogInformation(
            "File compressed: {ZipPath}",
            zipPath);
        
        return zipPath;
    }
}
