using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using AuditHistoryExtractorPro.Infrastructure.Authentication;

namespace AuditHistoryExtractorPro.Core.Services;

public class AuthHelper
{
    public AuthenticationConfiguration BuildConfiguration(ConnectionSettings settings)
    {
        var normalizedUrl = NormalizeServiceUrl(settings.EnvironmentUrl);
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("La URL del entorno Dataverse no es válida.");
        }

        return new AuthenticationConfiguration
        {
            EnvironmentUrl = normalizedUrl,
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

    private static string NormalizeServiceUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        var open = trimmed.LastIndexOf('(');
        var close = trimmed.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            var candidate = trimmed.Substring(open + 1, close - open - 1).Trim();
            if (Uri.TryCreate(candidate, UriKind.Absolute, out _))
            {
                return candidate;
            }
        }

        return trimmed;
    }
}
