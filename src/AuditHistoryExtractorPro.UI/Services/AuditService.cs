using AuditHistoryExtractorPro.Domain.Interfaces;

namespace AuditHistoryExtractorPro.UI.Services;

public class AuditService : IAuditService
{
    private readonly ExtractPageCoordinator _extractPageCoordinator;

    public AuditService(ExtractPageCoordinator extractPageCoordinator)
    {
        _extractPageCoordinator = extractPageCoordinator;
    }

    public Task<ExtractExecutionResult> ExtractAuditHistoryAsync(
        ExtractInputModel input,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _extractPageCoordinator.ExecuteAsync(input, progress, cancellationToken);
    }
}
