using AuditHistoryExtractorPro.Domain.Interfaces;

namespace AuditHistoryExtractorPro.UI.Services;

public interface IAuditService
{
    bool IsConnected { get; }
    string OrganizationName { get; }
    string CrmUrl { get; set; }
    string LastEntity { get; set; }
    string LastFilters { get; set; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveUserConfigAsync(CancellationToken cancellationToken = default);
    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<ExtractExecutionResult> ExtractAuditHistoryAsync(
        ExtractInputModel input,
        string outputFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
