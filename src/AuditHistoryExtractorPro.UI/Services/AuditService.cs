using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using DataverseServiceClient = Microsoft.PowerPlatform.Dataverse.Client.ServiceClient;
using Microsoft.Crm.Sdk.Messages;

namespace AuditHistoryExtractorPro.UI.Services;

public class AuditService : IAuditService
{
    private readonly ExtractPageCoordinator _extractPageCoordinator;
    private readonly IAuthenticationProvider _authenticationProvider;
    private readonly AuthenticationConfiguration _authenticationConfiguration;
    private readonly AuditHistoryExtractorPro.Domain.Interfaces.ILogger<AuditService> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private DataverseServiceClient? _serviceClient;

    public bool IsConnected { get; private set; }
    public string OrganizationName { get; private set; } = string.Empty;

    public AuditService(
        ExtractPageCoordinator extractPageCoordinator,
        IAuthenticationProvider authenticationProvider,
        AuthenticationConfiguration authenticationConfiguration,
        AuditHistoryExtractorPro.Domain.Interfaces.ILogger<AuditService> logger)
    {
        _extractPageCoordinator = extractPageCoordinator;
        _authenticationProvider = authenticationProvider;
        _authenticationConfiguration = authenticationConfiguration;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            IsConnected = false;
            OrganizationName = string.Empty;

            if (!Uri.TryCreate(_authenticationConfiguration.EnvironmentUrl, UriKind.Absolute, out var dataverseUri))
            {
                throw new InvalidOperationException("La URL de Dataverse no es válida.");
            }

            _serviceClient = new DataverseServiceClient(
                dataverseUri,
                async _ => await _authenticationProvider.GetAccessTokenAsync(cancellationToken),
                useUniqueInstance: true,
                logger: null);

            if (!_serviceClient.IsReady)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(_serviceClient.LastError)
                        ? "No fue posible inicializar el cliente Dataverse."
                        : _serviceClient.LastError);
            }

            var whoAmIResponse = await _serviceClient.ExecuteAsync(new WhoAmIRequest(), cancellationToken);
            if (whoAmIResponse is null)
            {
                throw new InvalidOperationException("La validación WhoAmI no devolvió respuesta.");
            }

            IsConnected = true;
            OrganizationName = string.IsNullOrWhiteSpace(_serviceClient.ConnectedOrgFriendlyName)
                ? dataverseUri.Host
                : _serviceClient.ConnectedOrgFriendlyName;
        }
        catch
        {
            IsConnected = false;
            OrganizationName = string.Empty;
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public Task<ExtractExecutionResult> ExtractAuditHistoryAsync(
        ExtractInputModel input,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _extractPageCoordinator.ExecuteAsync(input, progress, cancellationToken);
    }
}
