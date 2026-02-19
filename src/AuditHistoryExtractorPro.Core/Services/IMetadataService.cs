using AuditHistoryExtractorPro.Core.Models;

namespace AuditHistoryExtractorPro.Core.Services;

public interface IMetadataService
{
    Task<IReadOnlyList<EntityDTO>> GetAuditableEntitiesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ViewDTO>> GetSystemViewsAsync(string entityLogicalName, CancellationToken cancellationToken = default);
}
