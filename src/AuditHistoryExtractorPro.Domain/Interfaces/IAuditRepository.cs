using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.ValueObjects;

namespace AuditHistoryExtractorPro.Domain.Interfaces;

/// <summary>
/// Interfaz para operaciones con el repositorio de auditoría de Dataverse.
/// Responsabilidad única: leer y consultar registros de auditoría.
/// La gestión del estado de sincronización incremental se delega a <see cref="ISyncStateStore"/>.
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
}
