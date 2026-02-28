using AuditHistoryExtractorPro.Domain.ValueObjects;

namespace AuditHistoryExtractorPro.Domain.Interfaces;

/// <summary>
/// Interfaz para autenticación con Dataverse
/// </summary>
public interface IAuthenticationProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default);
    AuthenticationType GetAuthenticationType();
}
