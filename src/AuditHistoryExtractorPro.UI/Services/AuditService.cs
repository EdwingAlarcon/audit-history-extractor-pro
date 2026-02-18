using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using DataverseServiceClient = Microsoft.PowerPlatform.Dataverse.Client.ServiceClient;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Text;

namespace AuditHistoryExtractorPro.UI.Services;

public class AuditService : IAuditService
{
    private const int MaxDataversePageSize = 5000;
    private readonly ExtractViewService _extractViewService;
    private readonly IAuthenticationProvider _authenticationProvider;
    private readonly AuthenticationConfiguration _authenticationConfiguration;
    private readonly AuditHistoryExtractorPro.Domain.Interfaces.ILogger<AuditService> _logger;
    private readonly IUserConfigService _userConfigService;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private DataverseServiceClient? _serviceClient;
    private bool _isInitialized;

    public bool IsConnected { get; private set; }
    public string OrganizationName { get; private set; } = string.Empty;
    public string CrmUrl { get; set; } = string.Empty;
    public string LastEntity { get; set; } = string.Empty;
    public string LastFilters { get; set; } = string.Empty;

    public AuditService(
        ExtractViewService extractViewService,
        IAuthenticationProvider authenticationProvider,
        AuthenticationConfiguration authenticationConfiguration,
        IUserConfigService userConfigService,
        AuditHistoryExtractorPro.Domain.Interfaces.ILogger<AuditService> logger)
    {
        _extractViewService = extractViewService;
        _authenticationProvider = authenticationProvider;
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

        return await Task.Run(async () =>
        {
            var criteriaResult = _extractViewService.BuildCriteria(input);
            if (!criteriaResult.Success || criteriaResult.Criteria is null)
            {
                return ExtractExecutionResult.Fail(
                    criteriaResult.ErrorMessage ?? "❌ No fue posible construir los criterios de extracción.");
            }

            if (!IsConnected || _serviceClient is null || !_serviceClient.IsReady)
            {
                await ConnectAsync(cancellationToken);
            }

            var filePath = ResolveOutputPath(outputFilePath, input.EntityName);
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return ExtractExecutionResult.Fail("❌ La ruta de salida no es válida.");
            }

            Directory.CreateDirectory(directory);

            progress?.Report("Iniciando extracción de auditoría...");

            var totalWritten = 0;
            var pageNumber = 1;
            var moreRecords = true;
            string? pagingCookie = null;
            var entityName = criteriaResult.Criteria.EntityNames.First();

            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536, useAsync: true);
            await using var writer = new StreamWriter(fileStream, new UTF8Encoding(false));

            await writer.WriteLineAsync("AuditId,CreatedOn,EntityName,RecordId,ActionCode,ActionName,UserId,UserName,TransactionId,ChangeData");

            while (moreRecords && !cancellationToken.IsCancellationRequested && totalWritten < input.MaxRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remaining = input.MaxRecords - totalWritten;
                var pageSize = Math.Min(MaxDataversePageSize, remaining);
                var query = BuildAuditQuery(criteriaResult.Criteria, entityName, pageNumber, pagingCookie, pageSize);

                progress?.Report($"Consultando página {pageNumber}...");

                var response = await Task.Run(() => _serviceClient!.RetrieveMultiple(query), cancellationToken);
                if (response.Entities.Count == 0)
                {
                    break;
                }

                var startIndex = totalWritten + 1;

                foreach (var entity in response.Entities)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(BuildCsvLine(entity));
                    totalWritten++;

                    if (totalWritten >= input.MaxRecords)
                    {
                        break;
                    }
                }

                await writer.FlushAsync();

                var endIndex = totalWritten;
                progress?.Report($"Escribiendo registros {startIndex}-{endIndex}...");

                moreRecords = response.MoreRecords && totalWritten < input.MaxRecords;
                if (moreRecords)
                {
                    pageNumber++;
                    pagingCookie = response.PagingCookie;
                }

                response.Entities.Clear();
            }

            progress?.Report($"Extracción completada. Total: {totalWritten} registros.");

            return ExtractExecutionResult.Ok(
                recordsExtracted: totalWritten,
                message: $"✅ Extracción completada. Archivo generado en: {filePath}",
                outputFilePath: filePath);
        }, cancellationToken);
    }

    private static string ResolveOutputPath(string outputFilePath, string entityName)
    {
        if (!string.IsNullOrWhiteSpace(outputFilePath))
        {
            return outputFilePath;
        }

        var safeEntity = string.IsNullOrWhiteSpace(entityName) ? "audit" : entityName.Trim();
        var fileName = $"audit_{safeEntity}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
        return Path.Combine(Path.GetTempPath(), "AuditHistoryExtractorPro", "exports", fileName);
    }

    private static QueryExpression BuildAuditQuery(
        ExtractionCriteria criteria,
        string entityName,
        int pageNumber,
        string? pagingCookie,
        int pageSize)
    {
        var query = new QueryExpression("audit")
        {
            ColumnSet = new ColumnSet(
                "auditid",
                "createdon",
                "action",
                "objectid",
                "objecttypecode",
                "userid",
                "transactionid",
                "changedata"),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo
            {
                Count = pageSize,
                PageNumber = pageNumber,
                PagingCookie = pagingCookie
            }
        };

        query.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, entityName);

        if (criteria.FromDate.HasValue)
        {
            query.Criteria.AddCondition("createdon", ConditionOperator.GreaterEqual, criteria.FromDate.Value);
        }

        if (criteria.ToDate.HasValue)
        {
            query.Criteria.AddCondition("createdon", ConditionOperator.LessEqual, criteria.ToDate.Value);
        }

        if (criteria.Operations?.Any() == true)
        {
            query.Criteria.AddCondition("action", ConditionOperator.In, criteria.Operations.Select(o => (int)o).ToArray());
        }

        if (criteria.CustomFilters?.TryGetValue("recordId", out var recordIdRaw) == true && Guid.TryParse(recordIdRaw, out var recordId))
        {
            query.Criteria.AddCondition("objectid", ConditionOperator.Equal, recordId);
        }

        query.Orders.Add(new OrderExpression("createdon", OrderType.Ascending));
        return query;
    }

    private static string BuildCsvLine(Entity entity)
    {
        var auditId = entity.GetAttributeValue<Guid>("auditid");
        var createdOn = entity.GetAttributeValue<DateTime>("createdon");
        var objectId = entity.GetAttributeValue<EntityReference>("objectid")?.Id;
        var objectType = entity.GetAttributeValue<string>("objecttypecode") ?? string.Empty;
        var actionCode = entity.GetAttributeValue<OptionSetValue>("action")?.Value ?? 0;
        var userRef = entity.GetAttributeValue<EntityReference>("userid");
        var transactionId = entity.GetAttributeValue<Guid?>("transactionid");
        var changeData = entity.GetAttributeValue<string>("changedata") ?? string.Empty;

        return string.Join(",", new[]
        {
            EscapeCsv(auditId.ToString()),
            EscapeCsv(createdOn == default ? string.Empty : createdOn.ToUniversalTime().ToString("O")),
            EscapeCsv(objectType),
            EscapeCsv(objectId?.ToString() ?? string.Empty),
            EscapeCsv(actionCode.ToString()),
            EscapeCsv(GetOperationName(actionCode)),
            EscapeCsv(userRef?.Id.ToString() ?? string.Empty),
            EscapeCsv(userRef?.Name ?? string.Empty),
            EscapeCsv(transactionId?.ToString() ?? string.Empty),
            EscapeCsv(changeData)
        });
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static string GetOperationName(int operationCode)
    {
        return operationCode switch
        {
            1 => "Create",
            2 => "Update",
            3 => "Delete",
            4 => "Associate",
            5 => "Disassociate",
            27 => "Archive",
            28 => "Restore",
            _ => "Unknown"
        };
    }
}
