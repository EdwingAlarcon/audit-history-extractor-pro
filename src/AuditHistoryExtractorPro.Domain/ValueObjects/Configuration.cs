namespace AuditHistoryExtractorPro.Domain.ValueObjects;

/// <summary>
/// Representa los criterios de extracción de auditoría.
/// La mayoría de propiedades son <c>init</c>: se asignan durante la construcción
/// y no pueden mutar después.
/// Excepción: <see cref="FromDate"/> y <see cref="ToDate"/> mantienen <c>set</c>
/// porque el handler de modo incremental los ajusta en tiempo de ejecución.
/// </summary>
public class ExtractionCriteria
{
    public List<string> EntityNames { get; init; } = new();
    public List<string>? FieldNames { get; init; }
    public DateTime? FromDate { get; set; }   // mutable: ajustado en modo incremental
    public DateTime? ToDate { get; set; }     // mutable: ajustado en modo incremental
    public List<string>? UserIds { get; init; }
    public List<OperationType>? Operations { get; init; }
    public bool IncrementalMode { get; init; }
    public int PageSize { get; init; } = 5000;
    public int MaxParallelRequests { get; init; } = 10;
    public Dictionary<string, string>? CustomFilters { get; init; }

    public void Validate()
    {
        if (!EntityNames.Any())
            throw new ArgumentException("At least one entity name must be specified");

        if (FromDate.HasValue && ToDate.HasValue && FromDate > ToDate)
            throw new ArgumentException("FromDate cannot be greater than ToDate");

        if (PageSize <= 0 || PageSize > 10000)
            throw new ArgumentException("PageSize must be between 1 and 10000");
    }
}

/// <summary>
/// Tipos de operación de auditoría
/// </summary>
public enum OperationType
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Associate = 4,
    Disassociate = 5,
    Archive = 27,
    Restore = 28
}

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

/// <summary>
/// Configuración de autenticación
/// </summary>
public class AuthenticationConfiguration
{
    public string EnvironmentUrl { get; set; } = string.Empty;
    public AuthenticationType Type { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string? CertificatePath { get; set; }
    public bool UseManagedIdentity { get; set; }
    public Dictionary<string, string>? AdditionalParameters { get; set; }
    
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EnvironmentUrl))
            throw new ArgumentException("EnvironmentUrl is required");
        
        switch (Type)
        {
            case AuthenticationType.OAuth2:
                if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(ClientId))
                    throw new ArgumentException("TenantId and ClientId are required for OAuth2");
                break;
            
            case AuthenticationType.ClientSecret:
                if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
                    throw new ArgumentException("ClientId and ClientSecret are required");
                break;
            
            case AuthenticationType.Certificate:
                if (string.IsNullOrWhiteSpace(CertificateThumbprint) && string.IsNullOrWhiteSpace(CertificatePath))
                    throw new ArgumentException("Certificate thumbprint or path is required");
                break;
        }
    }
}

public enum AuthenticationType
{
    OAuth2,
    ClientSecret,
    Certificate,
    ManagedIdentity
}

/// <summary>
/// Resultado de una operación de extracción
/// </summary>
public class ExtractionResult
{
    public bool Success { get; set; }
    public int RecordsExtracted { get; set; }
    public int RecordsFailed { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    public string GetSummary()
    {
        return $"Extracted {RecordsExtracted} records in {Duration.TotalSeconds:F2} seconds. " +
               $"Failed: {RecordsFailed}. Success: {Success}";
    }
}
