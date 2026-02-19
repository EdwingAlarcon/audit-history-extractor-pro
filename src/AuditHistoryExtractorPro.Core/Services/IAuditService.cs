using AuditHistoryExtractorPro.Core.Models;

namespace AuditHistoryExtractorPro.Core.Services;

public interface IAuditService
{
    bool IsConnected { get; }
    string OrganizationName { get; }

    Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);

    Task<ExtractionResult> ExtractAuditHistoryAsync(
        ExtractionRequest request,
        string outputFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
