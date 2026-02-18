using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.ValueObjects;

namespace AuditHistoryExtractorPro.Domain.Interfaces;

/// <summary>
/// Interfaz para autenticación con Dataverse
/// </summary>
public interface IAuthenticationProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default);
    AuthenticationType GetAuthenticationType();
}

/// <summary>
/// Interfaz para operaciones con el repositorio de auditoría de Dataverse
/// </summary>
public interface IAuditRepository
{
    Task<List<AuditRecord>> ExtractAuditRecordsAsync(
        ExtractionCriteria criteria,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default);
    
    Task<AuditRecord?> GetAuditRecordByIdAsync(
        Guid auditId,
        CancellationToken cancellationToken = default);
    
    Task<List<AuditRecord>> GetRecordHistoryAsync(
        string entityName,
        Guid recordId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
    
    Task<AuditStatistics> GetAuditStatisticsAsync(
        ExtractionCriteria criteria,
        CancellationToken cancellationToken = default);
    
    Task<DateTime?> GetLastExtractionDateAsync(
        string entityName,
        CancellationToken cancellationToken = default);
    
    Task SaveLastExtractionDateAsync(
        string entityName,
        DateTime date,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interfaz para servicios de exportación
/// </summary>
public interface IExportService
{
    Task<string> ExportAsync(
        List<AuditRecord> records,
        ExportConfiguration configuration,
        CancellationToken cancellationToken = default);
    
    Task<bool> SendToDestinationAsync(
        string filePath,
        ExportDestination destination,
        CancellationToken cancellationToken = default);
    
    bool SupportsFormat(ExportFormat format);
}

/// <summary>
/// Interfaz para procesamiento de datos de auditoría
/// </summary>
public interface IAuditProcessor
{
    Task<List<AuditRecord>> NormalizeRecordsAsync(
        List<AuditRecord> records,
        CancellationToken cancellationToken = default);
    
    Task<RecordComparison> CompareRecordVersionsAsync(
        AuditRecord previous,
        AuditRecord current,
        CancellationToken cancellationToken = default);
    
    Task<List<AuditRecord>> FilterRelevantChangesAsync(
        List<AuditRecord> records,
        List<string>? relevantFields = null,
        CancellationToken cancellationToken = default);
    
    Task<List<AuditRecord>> EnrichRecordsAsync(
        List<AuditRecord> records,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interfaz para servicios de caché
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interfaz para gestión de secretos (Azure Key Vault, etc.)
/// </summary>
public interface ISecretManager
{
    Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
    Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default);
    Task<bool> SecretExistsAsync(string secretName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Servicio para resolución de metadatos con caché optimizado
/// Convierte nombres lógicos a labels legibles y resuelve valores de OptionSet
/// </summary>
public interface IMetadataResolutionService
{
    /// <summary>
    /// Obtiene el nombre de display de un atributo lógico
    /// Utiliza caché de dos niveles para optimizar rendimiento
    /// </summary>
    Task<string> ResolveAttributeDisplayNameAsync(
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resuelve un valor de OptionSet a su etiqueta legible
    /// Cachea el conjunto completo en la primera solicitud
    /// </summary>
    Task<string> ResolveOptionSetLabelAsync(
        string entityLogicalName,
        string attributeLogicalName,
        int optionSetValue,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Obtiene el tipo de dato de un atributo
    /// </summary>
    Task<string> ResolveAttributeTypeAsync(
        string entityLogicalName,
        string attributeLogicalName,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Precarga metadatos para una entidad completa optimizando extracciones posteriores
    /// </summary>
    Task PreloadEntityMetadataAsync(
        string entityLogicalName,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Limpia el caché de metadatos (usar en casos de actualización de schema)
    /// </summary>
    Task ClearMetadataCacheAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interfaz para logging estructurado
/// </summary>
public interface ILogger<T>
{
    void LogInformation(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogError(Exception? exception, string message, params object[] args);
    void LogDebug(string message, params object[] args);
}

/// <summary>
/// Clase para reportar progreso de extracción
/// </summary>
public class ExtractionProgress
{
    public int TotalRecords { get; set; }
    public int ProcessedRecords { get; set; }
    public int FailedRecords { get; set; }
    public double PercentComplete => TotalRecords > 0 ? (ProcessedRecords * 100.0) / TotalRecords : 0;
    public string CurrentEntity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }
}
