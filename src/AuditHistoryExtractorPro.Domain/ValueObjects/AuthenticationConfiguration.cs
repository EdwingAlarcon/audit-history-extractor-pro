namespace AuditHistoryExtractorPro.Domain.ValueObjects;

/// <summary>
/// Configuración de autenticación para conectar con Dataverse.
/// Todas las propiedades son mutables (<c>set</c>) porque la página de Configuración
/// permite al usuario cambiar los parámetros de conexión en tiempo de ejecución
/// sin necesidad de reiniciar la aplicación.
/// </summary>
public class AuthenticationConfiguration
{
    public string EnvironmentUrl { get; set; } = string.Empty;
    public AuthenticationType Type { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string? CertificatePath { get; set; }
    public bool UseManagedIdentity { get; set; }
    public Dictionary<string, string>? AdditionalParameters { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EnvironmentUrl))
            throw new ArgumentException("EnvironmentUrl is required");

        switch (Type)
        {
            case AuthenticationType.OAuth2:
                if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(ClientId))
                    throw new ArgumentException("TenantId and ClientId are required for OAuth2");
                break;

            case AuthenticationType.ClientSecret:
                if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
                    throw new ArgumentException("ClientId and ClientSecret are required");
                break;

            case AuthenticationType.Certificate:
                if (string.IsNullOrWhiteSpace(CertificateThumbprint) && string.IsNullOrWhiteSpace(CertificatePath))
                    throw new ArgumentException("Certificate thumbprint or path is required");
                break;
        }
    }
}

public enum AuthenticationType
{
    OAuth2,
    ClientSecret,
    Certificate,
    ManagedIdentity
}
