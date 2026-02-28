namespace AuditHistoryExtractorPro.Domain.ValueObjects;

/// <summary>
/// Configuración de exportación.
/// Todas las propiedades son <c>init</c>: se configuran una sola vez al construir
/// el objeto y no deben cambiar durante el ciclo de vida de la exportación.
/// </summary>
public class ExportConfiguration
{
    public ExportFormat Format { get; init; } = ExportFormat.Excel;
    public string OutputPath { get; init; } = "./exports";
    public string FileName { get; init; } = "audit_export";
    public bool CompressOutput { get; init; }
    public bool IncludeTimestamp { get; init; } = true;
    public int BatchSize { get; init; } = 10000;
    public ExportDestination? Destination { get; init; }
}

public enum ExportFormat
{
    Excel,
    Csv,
    Json,
    Sql
}

/// <summary>
/// Destino de exportación adicional (email, blob storage, etc.)
/// </summary>
public class ExportDestination
{
    public DestinationType Type { get; init; }
    public Dictionary<string, string> Configuration { get; init; } = new();
}

public enum DestinationType
{
    Email,
    AzureBlobStorage,
    SharePoint,
    FileShare
}
