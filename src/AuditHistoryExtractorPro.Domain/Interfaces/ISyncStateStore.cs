namespace AuditHistoryExtractorPro.Domain.Interfaces;

/// <summary>
/// Contrato para persistir y consultar el estado de sincronización incremental por entidad.
/// Separado de IAuditRepository para cumplir SRP: un repositorio de lectura de auditoría
/// no debería ser responsable de gestionar metadatos de sincronización.
/// </summary>
public interface ISyncStateStore
{
    /// <summary>
    /// Obtiene la fecha en que se realizó la última extracción para una entidad.
    /// Devuelve null si nunca se ha realizado una extracción o si el estado no persiste.
    /// </summary>
    Task<DateTime?> GetLastExtractionDateAsync(
        string entityName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra la fecha de extracción completada para una entidad.
    /// </summary>
    Task SaveLastExtractionDateAsync(
        string entityName,
        DateTime date,
        CancellationToken cancellationToken = default);
}
