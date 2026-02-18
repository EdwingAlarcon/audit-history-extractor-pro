using AuditHistoryExtractorPro.Domain.Interfaces;

namespace AuditHistoryExtractorPro.UI.Services;

public interface IAuditService
{
    Task<ExtractExecutionResult> ExtractAuditHistoryAsync(
        ExtractInputModel input,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
