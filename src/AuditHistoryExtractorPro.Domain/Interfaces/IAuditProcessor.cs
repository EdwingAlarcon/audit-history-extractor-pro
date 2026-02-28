using AuditHistoryExtractorPro.Domain.Entities;

namespace AuditHistoryExtractorPro.Domain.Interfaces;

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
