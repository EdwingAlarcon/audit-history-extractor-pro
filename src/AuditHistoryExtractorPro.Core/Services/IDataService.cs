using AuditHistoryExtractorPro.Core.Models;

namespace AuditHistoryExtractorPro.Core.Services;

public interface IDataService
{
    Task<IReadOnlyList<UserDTO>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);
}
