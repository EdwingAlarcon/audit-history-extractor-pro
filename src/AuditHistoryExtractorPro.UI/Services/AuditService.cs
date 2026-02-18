using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using CoreConnectionSettings = AuditHistoryExtractorPro.Core.Models.ConnectionSettings;
using CoreExtractionRequest = AuditHistoryExtractorPro.Core.Models.ExtractionRequest;
using CoreAuditService = AuditHistoryExtractorPro.Core.Services.IAuditService;

namespace AuditHistoryExtractorPro.UI.Services;

public class AuditService : IAuditService
{
    private readonly CoreAuditService _coreAuditService;
    private readonly AuthenticationConfiguration _authenticationConfiguration;
    private readonly AuditHistoryExtractorPro.Domain.Interfaces.ILogger<AuditService> _logger;
    private readonly IUserConfigService _userConfigService;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public bool IsConnected { get; private set; }
    public string OrganizationName { get; private set; } = string.Empty;
    public string CrmUrl { get; set; } = string.Empty;
    public string LastEntity { get; set; } = string.Empty;
    public string LastFilters { get; set; } = string.Empty;

    public AuditService(
        CoreAuditService coreAuditService,
        AuthenticationConfiguration authenticationConfiguration,
        IUserConfigService userConfigService,
        AuditHistoryExtractorPro.Domain.Interfaces.ILogger<AuditService> logger)
    {
        _coreAuditService = coreAuditService;
        _authenticationConfiguration = authenticationConfiguration;
        _userConfigService = userConfigService;
        _logger = logger;
        CrmUrl = authenticationConfiguration.EnvironmentUrl;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            var config = await _userConfigService.LoadAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(config.LastCrmUrl))
            {
                CrmUrl = config.LastCrmUrl;
                _authenticationConfiguration.EnvironmentUrl = config.LastCrmUrl;
            }

            LastEntity = config.LastEntity ?? string.Empty;
            LastFilters = config.LastFilters ?? string.Empty;

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task SaveUserConfigAsync(CancellationToken cancellationToken = default)
    {
        var config = new UserConfig
        {
            LastCrmUrl = CrmUrl,
            LastEntity = LastEntity,
            LastFilters = LastFilters
        };

        await _userConfigService.SaveAsync(config, cancellationToken);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            IsConnected = false;
            OrganizationName = string.Empty;

            if (!string.IsNullOrWhiteSpace(CrmUrl))
            {
                _authenticationConfiguration.EnvironmentUrl = CrmUrl.Trim();
            }

            if (!Uri.TryCreate(_authenticationConfiguration.EnvironmentUrl, UriKind.Absolute, out var dataverseUri))
            {
                throw new InvalidOperationException("La URL de Dataverse no es válida.");
            }

            await _coreAuditService.ConnectAsync(new CoreConnectionSettings
            {
                EnvironmentUrl = _authenticationConfiguration.EnvironmentUrl,
                TenantId = _authenticationConfiguration.TenantId,
                ClientId = _authenticationConfiguration.ClientId,
                ClientSecret = _authenticationConfiguration.ClientSecret
            }, cancellationToken);

            IsConnected = _coreAuditService.IsConnected;
            OrganizationName = string.IsNullOrWhiteSpace(_coreAuditService.OrganizationName)
                ? dataverseUri.Host
                : _coreAuditService.OrganizationName;

            CrmUrl = dataverseUri.ToString();
            await SaveUserConfigAsync(cancellationToken);
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

    public async Task<ExtractExecutionResult> ExtractAuditHistoryAsync(
        ExtractInputModel input,
        string outputFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        if (!IsConnected)
        {
            await ConnectAsync(cancellationToken);
        }

        var coreResult = await _coreAuditService.ExtractAuditHistoryAsync(
            new CoreExtractionRequest
            {
                EntityName = input.EntityName,
                RecordId = input.RecordId,
                StartDate = input.StartDate,
                EndDate = input.EndDate,
                IncludeCreate = input.IncludeCreate,
                IncludeUpdate = input.IncludeUpdate,
                IncludeDelete = input.IncludeDelete,
                MaxRecords = input.MaxRecords
            },
            outputFilePath,
            progress,
            cancellationToken);

        if (!coreResult.Success)
        {
            return ExtractExecutionResult.Fail($"❌ Error: {coreResult.Message}");
        }

        return ExtractExecutionResult.Ok(
            recordsExtracted: coreResult.RecordsExtracted,
            message: $"✅ {coreResult.Message}",
            outputFilePath: coreResult.OutputFilePath);
    }
}
