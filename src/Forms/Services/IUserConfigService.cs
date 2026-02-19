namespace AuditHistoryExtractorPro.UI.Services;

public interface IUserConfigService
{
    Task<UserConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UserConfig config, CancellationToken cancellationToken = default);
}
