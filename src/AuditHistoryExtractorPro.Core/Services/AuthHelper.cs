using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using AuditHistoryExtractorPro.Infrastructure.Authentication;

namespace AuditHistoryExtractorPro.Core.Services;

public class AuthHelper
{
    public AuthenticationConfiguration BuildConfiguration(ConnectionSettings settings)
    {
        if (!Uri.TryCreate(settings.EnvironmentUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("La URL del entorno Dataverse no es válida.");
        }

        return new AuthenticationConfiguration
        {
            EnvironmentUrl = settings.EnvironmentUrl.Trim(),
            TenantId = settings.TenantId,
            ClientId = settings.ClientId,
            ClientSecret = settings.ClientSecret,
            Type = AuthenticationType.OAuth2
        };
    }

    public IAuthenticationProvider CreateProvider(AuthenticationConfiguration configuration)
    {
        return configuration.Type switch
        {
            AuthenticationType.ClientSecret => new ClientSecretAuthenticationProvider(
                configuration,
                secretManager: null,
                logger: new CoreDomainLogger<ClientSecretAuthenticationProvider>()),
            AuthenticationType.Certificate => new CertificateAuthenticationProvider(
                configuration,
                logger: new CoreDomainLogger<CertificateAuthenticationProvider>()),
            AuthenticationType.ManagedIdentity => new ManagedIdentityAuthenticationProvider(
                configuration,
                logger: new CoreDomainLogger<ManagedIdentityAuthenticationProvider>()),
            _ => new OAuth2AuthenticationProvider(
                configuration,
                logger: new CoreDomainLogger<OAuth2AuthenticationProvider>())
        };
    }
}
