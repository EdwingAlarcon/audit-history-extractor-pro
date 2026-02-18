namespace AuditHistoryExtractorPro.Domain.ValueObjects;

/// <summary>
/// Representa los criterios de extracción de auditoría con optimizaciones Enterprise-Grade
/// </summary>
public class ExtractionCriteria
{
    public List<string> EntityNames { get; set; } = new();
    public List<string>? FieldNames { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public List<string>? UserIds { get; set; }
    
    /// <summary>
    /// Operaciones a filtrar (Legacy - usar ActionCodes para nuevas implementaciones)
    /// </summary>
    public List<OperationType>? Operations { get; set; }
    
    /// <summary>
    /// Códigos de auditoría a filtrar (nuevo - mapeo exhaustivo forense)
    /// </summary>
    public List<AuditActionCode>? ActionCodes { get; set; }
    
    public bool IncrementalMode { get; set; }
    public int PageSize { get; set; } = 5000;
    public int MaxParallelRequests { get; set; } = 10;
    
    // ⭐ NUEVOS CAMPOS PARA ENTERPRISE-GRADE
    /// <summary>
    /// Activar optimizaciones automáticas cuando el volumen es alto
    /// </summary>
    public bool EnableAdaptivePaging { get; set; } = true;
    
    /// <summary>
    /// Umbral de registros para activar paginación progresiva
    /// </summary>
    public int HighVolumeThreshold { get; set; } = 5000;
    
    /// <summary>
    /// Delay en ms entre batches cuando volumen es alto para evitar throttling
    /// </summary>
    public int ProgressiveDelayMs { get; set; } = 500;
    
    /// <summary>
    /// Memoria máxima permitida en MB antes de flush a disco
    /// </summary>
    public int MaxMemoryMb { get; set; } = 2048;
    
    /// <summary>
    /// Campos ruidosos del sistema a excluir automáticamente
    /// </summary>
    public DataCleaningConfiguration? DataCleaningConfig { get; set; }
    
    public Dictionary<string, string>? CustomFilters { get; set; }
    
    public void Validate()
    {
        if (!EntityNames.Any())
            throw new ArgumentException("At least one entity name must be specified");
        
        if (FromDate.HasValue && ToDate.HasValue && FromDate > ToDate)
            throw new ArgumentException("FromDate cannot be greater than ToDate");
        
        if (PageSize <= 0 || PageSize > 10000)
            throw new ArgumentException("PageSize must be between 1 and 10000");
        
        if (MaxMemoryMb < 256 || MaxMemoryMb > 8192)
            throw new ArgumentException("MaxMemoryMb must be between 256 and 8192");
        
        if (HighVolumeThreshold <= 0)
            throw new ArgumentException("HighVolumeThreshold must be greater than 0");
    }
}

/// <summary>
/// Tipos de operación de auditoría (Legacy - mantener para compatibilidad)
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
/// Códigos de auditoría completos de Dataverse (SDK 2023-2024)
/// Mapeo exhaustivo para análisis forense
/// </summary>
public enum AuditActionCode
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Associate = 4,
    Disassociate = 5,
    Assign = 6,
    Share = 7,
    Unshare = 8,
    Merge = 9,
    Reparent = 10,
    Qualify = 11,
    Disqualify = 12,
    Win = 13,
    Lose = 14,
    Deactivate = 15,
    Activate = 16,
    Fulfill = 19,
    CancelOrders = 21,
    ConvertQuote = 22,
    Archive = 27,
    Restore = 28
}

/// <summary>
/// Categoría de auditoría para análisis forense avanzado
/// </summary>
public enum AuditCategory
{
    /// <summary>Operaciones CRUD básicas: Create, Update, Delete</summary>
    CrudBasic,
    
    /// <summary>Cambios en relaciones: Associate, Disassociate</summary>
    Relational,
    
    /// <summary>Cambios de propiedad/permisos: Assign, Share, Unshare</summary>
    Security,
    
    /// <summary>Operaciones especiales: Merge, Reparent</summary>
    Operations,
    
    /// <summary>Procesos de Venta: Qualify, Disqualify, Win, Lose</summary>
    SalesProcess,
    
    /// <summary>Cambios de estado: Activate, Deactivate</summary>
    StatusChange,
    
    /// <summary>Mantenimiento y archivos: Archive, Restore</summary>
    Maintenance
}

}

/// <summary>
/// Configuración de limpieza de datos para eliminar ruido del sistema
/// </summary>
public class DataCleaningConfiguration
{
    /// <summary>
    /// Campos del sistema que deben excluirse siempre en análisis forense
    /// </summary>
    public static readonly HashSet<string> SystemNoiseFields = new(StringComparer.OrdinalIgnoreCase)
    {
        // Versionamiento automático
        "versionnumber",
        
        // Timestamps correlacionados
        "modifiedon",
        
        // Metadatos de flujos de procesos
        "traversedpath",
        "stageid",
        "stepid",
        "processid",
        
        // Datos de auditoría integrados
        "createdby",
        "modifiedby",
        "owneridtype",
        "owninguser",
        "owningteam",
        "owningbusinessunit",
        
        // Metadatos internos
        "resourcespec",
        "resourcespec_display",
        "utcconversiontimezonecode",
        "timezonecode",
        "organizationid",
    };
    
    /// <summary>
    /// Habilitar exclusión automática de campos ruidosos
    /// </summary>
    public bool EnableNoiseFiltering { get; set; } = true;
    
    /// <summary>
    /// Campos adicionales a excluir (configurable por usuario)
    /// </summary>
    public HashSet<string> CustomNoisyFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Obtiene la lista completa de campos a excluir
    /// </summary>
    public HashSet<string> GetFieldsToExclude()
    {
        if (!EnableNoiseFiltering)
            return new HashSet<string>();

        var combined = new HashSet<string>(SystemNoiseFields, StringComparer.OrdinalIgnoreCase);
        combined.UnionWith(CustomNoisyFields);
        return combined;
    }
    
    /// <summary>
    /// Verifica si un campo debe ser excluido
    /// </summary>
    public bool ShouldExcludeField(string fieldLogicalName)
    {
        return GetFieldsToExclude().Contains(fieldLogicalName);
    }
}

/// <summary>
/// Configuración de exportación
/// </summary>
public class ExportConfiguration
{
    public ExportFormat Format { get; set; } = ExportFormat.Excel;
    public string OutputPath { get; set; } = "./exports";
    public string FileName { get; set; } = "audit_export";
    public bool CompressOutput { get; set; }
    public bool IncludeTimestamp { get; set; } = true;
    public int BatchSize { get; set; } = 10000;
    public ExportDestination? Destination { get; set; }
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
    public DestinationType Type { get; set; }
    public Dictionary<string, string> Configuration { get; set; } = new();
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
