using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using Azure.Identity;
using Microsoft.Identity.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography.X509Certificates;

namespace AuditHistoryExtractorPro.Infrastructure.Authentication;

public record DeviceCodeChallengeInfo(string UserCode, string VerificationUrl, string Message);

/// <summary>
/// Proveedor de autenticación OAuth2 para Dataverse
/// </summary>
public class OAuth2AuthenticationProvider : IAuthenticationProvider
{
    public const string DefaultPublicClientAppId = "51f81489-12ee-4a9e-aaae-a2591f45987d";
    public const string DefaultPublicClientRedirectUri = "app://58145B91-0C36-4500-8554-080854F2AC97";

    private readonly AuthenticationConfiguration _config;
    private readonly ILogger<OAuth2AuthenticationProvider> _logger;
    private readonly Action<DeviceCodeChallengeInfo>? _deviceCodeCallback;
    private IPublicClientApplication? _clientApp;

    public OAuth2AuthenticationProvider(
        AuthenticationConfiguration config,
        ILogger<OAuth2AuthenticationProvider> logger,
        Action<DeviceCodeChallengeInfo>? deviceCodeCallback = null)
    {
        _config = config;
        _logger = logger;
        _deviceCodeCallback = deviceCodeCallback;
        InitializeClientApp();
    }

    private void InitializeClientApp()
    {
        var clientId = string.IsNullOrWhiteSpace(_config.ClientId)
            ? DefaultPublicClientAppId
            : _config.ClientId;

        var tenantId = string.IsNullOrWhiteSpace(_config.TenantId)
            ? "organizations"
            : _config.TenantId;

        _clientApp = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .WithRedirectUri(DefaultPublicClientRedirectUri)
            .Build();
    }

    private bool UseDeviceCodeFlow()
    {
        if (_config.AdditionalParameters?.TryGetValue("AuthFlow", out var authFlow) == true)
        {
            return string.Equals(authFlow, "device_code", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var scopes = new[] { $"{_config.EnvironmentUrl}/.default" };
            
            // Intentar obtener token del caché
            var accounts = await _clientApp!.GetAccountsAsync();
            AuthenticationResult? result = null;

            if (accounts.Any())
            {
                try
                {
                    result = await _clientApp.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                        .ExecuteAsync(cancellationToken);
                }
                catch (MsalUiRequiredException)
                {
                    // Token expirado, necesita interacción
                    _logger.LogInformation("Token expired, requesting interactive authentication");
                }
            }

            // Si no hay token válido, solicitar con Device Code (compatible con Blazor Server)
            if (result == null)
            {
                if (UseDeviceCodeFlow())
                {
                    result = await _clientApp.AcquireTokenWithDeviceCode(scopes, deviceCodeResult =>
                    {
                        _logger.LogInformation("Device Code challenge: {Message}", deviceCodeResult.Message);

                        _deviceCodeCallback?.Invoke(new DeviceCodeChallengeInfo(
                            deviceCodeResult.UserCode,
                            deviceCodeResult.VerificationUrl,
                            deviceCodeResult.Message));

                        return Task.CompletedTask;
                    }).ExecuteAsync(cancellationToken);
                }
                else
                {
                    result = await _clientApp.AcquireTokenInteractive(scopes)
                        .ExecuteAsync(cancellationToken);
                }
            }

            _logger.LogInformation("OAuth2 authentication successful");
            return result.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth2 authentication failed");
            throw new InvalidOperationException("Failed to authenticate with OAuth2", ex);
        }
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            return !string.IsNullOrEmpty(token);
        }
        catch
        {
            return false;
        }
    }

    public AuthenticationType GetAuthenticationType() => AuthenticationType.OAuth2;
}

/// <summary>
/// Proveedor de autenticación con Client Secret
/// </summary>
public class ClientSecretAuthenticationProvider : IAuthenticationProvider
{
    private readonly AuthenticationConfiguration _config;
    private readonly ISecretManager? _secretManager;
    private readonly ILogger<ClientSecretAuthenticationProvider> _logger;
    private IConfidentialClientApplication? _clientApp;

    public ClientSecretAuthenticationProvider(
        AuthenticationConfiguration config,
        ISecretManager? secretManager,
        ILogger<ClientSecretAuthenticationProvider> logger)
    {
        _config = config;
        _secretManager = secretManager;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Obtener el secret (desde Key Vault si está configurado)
            var clientSecret = _config.ClientSecret;
            
            if (clientSecret?.StartsWith("kv://") == true && _secretManager != null)
            {
                var secretName = clientSecret.Split("//")[1].Split('/').Last();
                clientSecret = await _secretManager.GetSecretAsync(secretName, cancellationToken);
                _logger.LogInformation("Client secret retrieved from Key Vault");
            }

            // Inicializar aplicación confidencial
            _clientApp = ConfidentialClientApplicationBuilder
                .Create(_config.ClientId)
                .WithClientSecret(clientSecret)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{_config.TenantId}"))
                .Build();

            var scopes = new[] { $"{_config.EnvironmentUrl}/.default" };
            var result = await _clientApp.AcquireTokenForClient(scopes)
                .ExecuteAsync(cancellationToken);

            _logger.LogInformation("Client Secret authentication successful");
            return result.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Client Secret authentication failed");
            throw new InvalidOperationException("Failed to authenticate with Client Secret", ex);
        }
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            return !string.IsNullOrEmpty(token);
        }
        catch
        {
            return false;
        }
    }

    public AuthenticationType GetAuthenticationType() => AuthenticationType.ClientSecret;
}

/// <summary>
/// Proveedor de autenticación con certificado
/// </summary>
public class CertificateAuthenticationProvider : IAuthenticationProvider
{
    private readonly AuthenticationConfiguration _config;
    private readonly ILogger<CertificateAuthenticationProvider> _logger;
    private IConfidentialClientApplication? _clientApp;

    public CertificateAuthenticationProvider(
        AuthenticationConfiguration config,
        ILogger<CertificateAuthenticationProvider> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            X509Certificate2 certificate;

            // Cargar certificado desde path o store
            if (!string.IsNullOrWhiteSpace(_config.CertificatePath))
            {
                certificate = new X509Certificate2(_config.CertificatePath);
                _logger.LogInformation("Certificate loaded from path");
            }
            else if (!string.IsNullOrWhiteSpace(_config.CertificateThumbprint))
            {
                certificate = FindCertificateByThumbprint(_config.CertificateThumbprint);
                _logger.LogInformation("Certificate loaded from store");
            }
            else
            {
                throw new InvalidOperationException("No certificate path or thumbprint provided");
            }

            _clientApp = ConfidentialClientApplicationBuilder
                .Create(_config.ClientId)
                .WithCertificate(certificate)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{_config.TenantId}"))
                .Build();

            var scopes = new[] { $"{_config.EnvironmentUrl}/.default" };
            var result = await _clientApp.AcquireTokenForClient(scopes)
                .ExecuteAsync(cancellationToken);

            _logger.LogInformation("Certificate authentication successful");
            return result.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Certificate authentication failed");
            throw new InvalidOperationException("Failed to authenticate with Certificate", ex);
        }
    }

    private X509Certificate2 FindCertificateByThumbprint(string thumbprint)
    {
        var store = new X509Store(StoreLocation.CurrentUser);
        try
        {
            store.Open(OpenFlags.ReadOnly);
            var certificates = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                thumbprint,
                false);

            if (certificates.Count == 0)
            {
                throw new InvalidOperationException($"Certificate with thumbprint {thumbprint} not found");
            }

            return certificates[0];
        }
        finally
        {
            store.Close();
        }
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            return !string.IsNullOrEmpty(token);
        }
        catch
        {
            return false;
        }
    }

    public AuthenticationType GetAuthenticationType() => AuthenticationType.Certificate;
}

/// <summary>
/// Proveedor de autenticación con Managed Identity
/// </summary>
public class ManagedIdentityAuthenticationProvider : IAuthenticationProvider
{
    private readonly AuthenticationConfiguration _config;
    private readonly ILogger<ManagedIdentityAuthenticationProvider> _logger;
    private readonly DefaultAzureCredential _credential;

    public ManagedIdentityAuthenticationProvider(
        AuthenticationConfiguration config,
        ILogger<ManagedIdentityAuthenticationProvider> logger)
    {
        _config = config;
        _logger = logger;
        _credential = new DefaultAzureCredential();
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenRequestContext = new Azure.Core.TokenRequestContext(
                new[] { $"{_config.EnvironmentUrl}/.default" });

            var token = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken);

            _logger.LogInformation("Managed Identity authentication successful");
            return token.Token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Managed Identity authentication failed");
            throw new InvalidOperationException("Failed to authenticate with Managed Identity", ex);
        }
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await GetAccessTokenAsync(cancellationToken);
            return !string.IsNullOrEmpty(token);
        }
        catch
        {
            return false;
        }
    }

    public AuthenticationType GetAuthenticationType() => AuthenticationType.ManagedIdentity;
}

/// <summary>
/// Factory para crear el proveedor de autenticación apropiado
/// </summary>
public class AuthenticationProviderFactory
{
    private readonly ISecretManager? _secretManager;
    private readonly IServiceProvider _serviceProvider;

    public AuthenticationProviderFactory(
        IServiceProvider serviceProvider,
        ISecretManager? secretManager = null)
    {
        _serviceProvider = serviceProvider;
        _secretManager = secretManager;
    }

    public IAuthenticationProvider Create(AuthenticationConfiguration config)
    {
        config.Validate();

        return config.Type switch
        {
            AuthenticationType.OAuth2 => new OAuth2AuthenticationProvider(
                config,
                _serviceProvider.GetService<ILogger<OAuth2AuthenticationProvider>>()!),
            
            AuthenticationType.ClientSecret => new ClientSecretAuthenticationProvider(
                config,
                _secretManager,
                _serviceProvider.GetService<ILogger<ClientSecretAuthenticationProvider>>()!),
            
            AuthenticationType.Certificate => new CertificateAuthenticationProvider(
                config,
                _serviceProvider.GetService<ILogger<CertificateAuthenticationProvider>>()!),
            
            AuthenticationType.ManagedIdentity => new ManagedIdentityAuthenticationProvider(
                config,
                _serviceProvider.GetService<ILogger<ManagedIdentityAuthenticationProvider>>()!),
            
            _ => throw new NotSupportedException($"Authentication type {config.Type} is not supported")
        };
    }
}
