namespace AuditHistoryExtractorPro.Domain.Interfaces;

/// <summary>
/// Interfaz para gestión de secretos (Azure Key Vault, etc.)
/// </summary>
public interface ISecretManager
{
    Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
    Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default);
    Task<bool> SecretExistsAsync(string secretName, CancellationToken cancellationToken = default);
}
