using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.ValueObjects;

namespace AuditHistoryExtractorPro.Domain.Interfaces;

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
