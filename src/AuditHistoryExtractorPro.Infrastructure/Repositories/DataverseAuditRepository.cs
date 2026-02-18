using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Polly;
using Polly.Retry;
using System.ServiceModel;

namespace AuditHistoryExtractorPro.Infrastructure.Repositories;

/// <summary>
/// Repositorio para operaciones de auditoría con Dataverse
/// </summary>
public class DataverseAuditRepository : IAuditRepository
{
    private readonly IAuthenticationProvider _authProvider;
    private readonly AuthenticationConfiguration _config;
    private readonly ILogger<DataverseAuditRepository> _logger;
    private readonly ICacheService _cacheService;
    private readonly AsyncRetryPolicy _retryPolicy;
    private ServiceClient? _serviceClient;

    public DataverseAuditRepository(
        IAuthenticationProvider authProvider,
        AuthenticationConfiguration config,
        ILogger<DataverseAuditRepository> logger,
        ICacheService cacheService)
    {
        _authProvider = authProvider;
        _config = config;
        _logger = logger;
        _cacheService = cacheService;

        // Configurar política de reintentos con Polly
        _retryPolicy = Policy
            .Handle<FaultException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "Retry {RetryCount} after {Delay}s due to {Exception}",
                        retryCount,
                        timeSpan.TotalSeconds,
                        exception.GetType().Name);
                });
    }

    private async Task<ServiceClient> GetServiceClientAsync()
    {
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            var token = await _authProvider.GetAccessTokenAsync();
            var connectionString = $"AuthType=OAuth;Url={_config.EnvironmentUrl};AccessToken={token};RequireNewInstance=True";
            
            _serviceClient = new ServiceClient(connectionString);
            
            if (!_serviceClient.IsReady)
            {
                throw new InvalidOperationException(
                    $"Failed to connect to Dataverse: {_serviceClient.LastError}");
            }

            _logger.LogInformation("Connected to Dataverse: {Url}", _config.EnvironmentUrl);
        }

        return _serviceClient;
    }

    public async Task<List<AuditRecord>> ExtractAuditRecordsAsync(
        ExtractionCriteria criteria,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var allRecords = new List<AuditRecord>();
        var startTime = DateTime.UtcNow;

        try
        {
            var client = await GetServiceClientAsync();

            foreach (var entityName in criteria.EntityNames)
            {
                _logger.LogInformation("Extracting audit records for entity: {Entity}", entityName);

                var progressInfo = new ExtractionProgress
                {
                    CurrentEntity = entityName,
                    Status = "Extracting...",
                    StartTime = startTime
                };

                var entityRecords = await ExtractEntityAuditRecordsAsync(
                    client,
                    entityName,
                    criteria,
                    progressInfo,
                    progress,
                    cancellationToken);

                allRecords.AddRange(entityRecords);

                _logger.LogInformation(
                    "Extracted {Count} audit records for {Entity}",
                    entityRecords.Count,
                    entityName);
            }

            return allRecords;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting audit records");
            throw;
        }
    }

    private async Task<List<AuditRecord>> ExtractEntityAuditRecordsAsync(
        ServiceClient client,
        string entityName,
        ExtractionCriteria criteria,
        ExtractionProgress progressInfo,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var records = new List<AuditRecord>();
        var pageNumber = 1;
        var moreRecords = true;
        string? pagingCookie = null;

        while (moreRecords && !cancellationToken.IsCancellationRequested)
        {
            var query = BuildAuditQuery(entityName, criteria, pageNumber, pagingCookie);

            var response = await _retryPolicy.ExecuteAsync(async () =>
            {
                return await Task.Run(() => client.RetrieveMultiple(query), cancellationToken);
            });

            foreach (var entity in response.Entities)
            {
                var auditRecord = MapToAuditRecord(entity);
                records.Add(auditRecord);
            }

            progressInfo.ProcessedRecords = records.Count;
            progressInfo.TotalRecords = response.TotalRecordCount;
            progress?.Report(progressInfo);

            moreRecords = response.MoreRecords;
            if (moreRecords)
            {
                pageNumber++;
                pagingCookie = response.PagingCookie;
            }

            _logger.LogDebug(
                "Page {Page}: Retrieved {Count} records, More: {More}",
                pageNumber,
                response.Entities.Count,
                moreRecords);
        }

        return records;
    }

    private QueryExpression BuildAuditQuery(
        string entityName,
        ExtractionCriteria criteria,
        int pageNumber,
        string? pagingCookie)
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
                "operation",
                "transactionid",
                "changedata"),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo
            {
                Count = criteria.PageSize,
                PageNumber = pageNumber,
                PagingCookie = pagingCookie
            }
        };

        // Filtrar por entidad
        query.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, entityName);

        // Filtrar por rango de fechas
        if (criteria.FromDate.HasValue)
        {
            query.Criteria.AddCondition(
                "createdon",
                ConditionOperator.GreaterEqual,
                criteria.FromDate.Value);
        }

        if (criteria.ToDate.HasValue)
        {
            query.Criteria.AddCondition(
                "createdon",
                ConditionOperator.LessEqual,
                criteria.ToDate.Value);
        }

        // Filtrar por tipo de operación
        if (criteria.Operations?.Any() == true)
        {
            var operationValues = criteria.Operations.Select(o => (int)o).ToArray();
            query.Criteria.AddCondition("action", ConditionOperator.In, operationValues);
        }

        // Filtrar por usuarios
        if (criteria.UserIds?.Any() == true)
        {
            query.Criteria.AddCondition("userid", ConditionOperator.In, criteria.UserIds.ToArray());
        }

        // Filtrar por recordId enviado como custom filter desde UI
        if (criteria.CustomFilters?.TryGetValue("recordId", out var recordIdValue) == true
            && Guid.TryParse(recordIdValue, out var recordId))
        {
            query.Criteria.AddCondition("objectid", ConditionOperator.Equal, recordId);
        }

        query.Orders.Add(new OrderExpression("createdon", OrderType.Descending));

        return query;
    }

    private AuditRecord MapToAuditRecord(Entity entity)
    {
        var auditRecord = new AuditRecord
        {
            AuditId = entity.GetAttributeValue<Guid>("auditid"),
            CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
            RecordId = entity.GetAttributeValue<EntityReference>("objectid")?.Id ?? Guid.Empty,
            EntityName = entity.GetAttributeValue<string>("objecttypecode") ?? string.Empty,
            Operation = GetOperationName(entity.GetAttributeValue<OptionSetValue>("action")?.Value ?? 0),
            UserId = entity.GetAttributeValue<EntityReference>("userid")?.Id.ToString() ?? string.Empty,
            UserName = entity.GetAttributeValue<EntityReference>("userid")?.Name ?? string.Empty,
            TransactionId = entity.GetAttributeValue<Guid?>("transactionid")?.ToString()
        };

        // Parsear cambios desde changedata (formato XML o JSON)
        var changeData = entity.GetAttributeValue<string>("changedata");
        if (!string.IsNullOrEmpty(changeData))
        {
            auditRecord.Changes = ParseChangeData(changeData);
        }

        return auditRecord;
    }

    private Dictionary<string, AuditFieldChange> ParseChangeData(string changeData)
    {
        var changes = new Dictionary<string, AuditFieldChange>();

        try
        {
            // El formato de changedata puede ser XML o JSON dependiendo de la versión de Dataverse
            // Aquí implementamos un parser básico
            var doc = System.Xml.Linq.XDocument.Parse(changeData);
            var attributes = doc.Descendants("attribute");

            foreach (var attr in attributes)
            {
                var fieldName = attr.Attribute("name")?.Value ?? string.Empty;
                var oldValue = attr.Element("oldValue")?.Value;
                var newValue = attr.Element("newValue")?.Value;
                var fieldType = attr.Attribute("type")?.Value ?? "string";

                changes[fieldName] = new AuditFieldChange
                {
                    FieldName = fieldName,
                    OldValue = oldValue,
                    NewValue = newValue,
                    FieldType = fieldType
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse change data");
        }

        return changes;
    }

    private string GetOperationName(int operationCode)
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
            _ => $"Unknown ({operationCode})"
        };
    }

    public async Task<AuditRecord?> GetAuditRecordByIdAsync(
        Guid auditId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetServiceClientAsync();

            var entity = await _retryPolicy.ExecuteAsync(async () =>
            {
                return await Task.Run(() =>
                    client.Retrieve("audit", auditId, new ColumnSet(true)),
                    cancellationToken);
            });

            return MapToAuditRecord(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit record {AuditId}", auditId);
            return null;
        }
    }

    public async Task<List<AuditRecord>> GetRecordHistoryAsync(
        string entityName,
        Guid recordId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var criteria = new ExtractionCriteria
        {
            EntityNames = new List<string> { entityName },
            FromDate = fromDate,
            ToDate = toDate
        };

        var records = await ExtractAuditRecordsAsync(criteria, null, cancellationToken);
        return records.Where(r => r.RecordId == recordId)
            .OrderBy(r => r.CreatedOn)
            .ToList();
    }

    public async Task<AuditStatistics> GetAuditStatisticsAsync(
        ExtractionCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var records = await ExtractAuditRecordsAsync(criteria, null, cancellationToken);

        var statistics = new AuditStatistics
        {
            TotalRecords = records.Count,
            CreateOperations = records.Count(r => r.Operation == "Create"),
            UpdateOperations = records.Count(r => r.Operation == "Update"),
            DeleteOperations = records.Count(r => r.Operation == "Delete"),
            RecordsByEntity = records.GroupBy(r => r.EntityName)
                .ToDictionary(g => g.Key, g => g.Count()),
            RecordsByUser = records.GroupBy(r => r.UserName)
                .ToDictionary(g => g.Key, g => g.Count()),
            FirstAuditDate = records.Any() ? records.Min(r => r.CreatedOn) : null,
            LastAuditDate = records.Any() ? records.Max(r => r.CreatedOn) : null,
            MostChangedFields = records
                .SelectMany(r => r.Changes.Keys)
                .GroupBy(k => k)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => g.Key)
                .ToList()
        };

        return statistics;
    }

    public async Task<DateTime?> GetLastExtractionDateAsync(
        string entityName,
        CancellationToken cancellationToken = default)
    {
        var key = $"last_extraction_{entityName}";
        return await _cacheService.GetAsync<DateTime?>(key, cancellationToken);
    }

    public async Task SaveLastExtractionDateAsync(
        string entityName,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        var key = $"last_extraction_{entityName}";
        await _cacheService.SetAsync(key, date, TimeSpan.FromDays(365), cancellationToken);
    }

    public void Dispose()
    {
        _serviceClient?.Dispose();
    }
}
