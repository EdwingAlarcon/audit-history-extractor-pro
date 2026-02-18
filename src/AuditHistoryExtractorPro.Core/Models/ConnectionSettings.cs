namespace AuditHistoryExtractorPro.Core.Models;

public class ConnectionSettings
{
    public string EnvironmentUrl { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}
