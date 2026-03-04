using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;

namespace AuditHistoryExtractorPro.Infrastructure.Repositories;

/// <summary>
/// Repositorio para operaciones de auditoría con Dataverse.
/// Implementa también <see cref="ISyncStateStore"/> para separar la responsabilidad
/// de gestión de estado de sincronización incremental (SRP).
/// </summary>
public class DataverseAuditRepository : IAuditRepository, ISyncStateStore
{
    private readonly IAuthenticationProvider _authProvider;
    private readonly AuthenticationConfiguration _config;
    private readonly ILogger<DataverseAuditRepository> _logger;
    private readonly ICacheService _cacheService;
    private readonly AsyncRetryPolicy _retryPolicy;
    private ServiceClient? _serviceClient;
    // SemaphoreSlim evita la race condition de crear dos ServiceClients simultáneos
    // cuando el repositorio se resuelve como Singleton bajo carga concurrente.
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

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

        // Política de resiliencia específica para throttling (429) y OrganizationServiceFault
        _retryPolicy = Policy
            .Handle<FaultException<OrganizationServiceFault>>(IsThrottlingFault)
            .Or<FaultException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, timeSpan, retryCount, _) =>
                {
                    _logger.LogWarning(
                        "Retry {RetryCount} after {Delay}s due to throttling ({Exception})",
                        retryCount,
                        timeSpan.TotalSeconds,
                        exception.GetType().Name);
                });
    }

    private async Task<ServiceClient> GetServiceClientAsync()
    {
        // Fast path: cliente listo, sin lock.
        if (_serviceClient?.IsReady == true)
            return _serviceClient;

        // Slow path: doble comprobación bajo lock para evitar race condition
        // en entornos multi-hilo (Blazor Server con múltiples sesiones concurrentes).
        await _connectionLock.WaitAsync();
        try
        {
            if (_serviceClient?.IsReady == true)
                return _serviceClient;

            if (!Uri.TryCreate(_config.EnvironmentUrl, UriKind.Absolute, out var dataverseUri))
            {
                throw new InvalidOperationException($"EnvironmentUrl inválida: {_config.EnvironmentUrl}");
            }

            _serviceClient?.Dispose();
            _serviceClient = new ServiceClient(
                dataverseUri,
                async _ => await _authProvider.GetAccessTokenAsync(),
                useUniqueInstance: true,
                logger: null);

            // Habilitar afinidad y reintentos internos del SDK para respetar Service Protection Limits
            _serviceClient.EnableAffinityCookie = true;
            _serviceClient.MaxRetryCount = 3;
            _serviceClient.RetryPauseTime = TimeSpan.FromSeconds(3);

            if (!_serviceClient.IsReady)
            {
                throw new InvalidOperationException(
                    $"Failed to connect to Dataverse: {_serviceClient.LastError}");
            }

            _logger.LogInformation("Connected to Dataverse: {Url}", _config.EnvironmentUrl);
            return _serviceClient;
        }
        finally
        {
            _connectionLock.Release();
        }
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

    public Task<List<AuditRecord>> ExtractAuditRecordsAsync(
        ExtractionCriteria criteria,
        IProgress<int>? percentProgress,
        CancellationToken cancellationToken = default)
    {
        IProgress<ExtractionProgress>? wrapper = null;

        if (percentProgress is not null)
        {
            wrapper = new Progress<ExtractionProgress>(p =>
            {
                var percent = (int)Math.Clamp(p.PercentComplete, 0, 100);
                percentProgress.Report(percent);
            });
        }

        return ExtractAuditRecordsAsync(criteria, wrapper, cancellationToken);
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

            var response = await _retryPolicy.ExecuteAsync(
                async ct => await Task.Run(() => client.RetrieveMultiple(query), ct),
                cancellationToken);

            foreach (var entity in response.Entities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var auditRecord = MapToAuditRecord(entity);
                await PopulateChangesFromAuditDetailAsync(client, auditRecord, cancellationToken);
                records.Add(auditRecord);
            }

            progressInfo.ProcessedRecords = records.Count;
            progressInfo.TotalRecords = response.TotalRecordCount > 0
                ? response.TotalRecordCount
                : Math.Max(records.Count, pageNumber * criteria.PageSize);
            progressInfo.Status = $"Extracting page {pageNumber}";
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

        var changeData = entity.GetAttributeValue<string>("changedata");
        if (!string.IsNullOrWhiteSpace(changeData))
        {
            auditRecord.AdditionalData["changedata"] = changeData;
        }

        return auditRecord;
    }

    private async Task PopulateChangesFromAuditDetailAsync(
        ServiceClient client,
        AuditRecord auditRecord,
        CancellationToken cancellationToken)
    {
        if (auditRecord.AuditId == Guid.Empty)
        {
            return;
        }

        try
        {
            var request = new RetrieveAuditDetailsRequest { AuditId = auditRecord.AuditId };

            var response = (RetrieveAuditDetailsResponse)await _retryPolicy.ExecuteAsync(
                async ct => (RetrieveAuditDetailsResponse)await Task.Run(() => client.Execute(request), ct),
                cancellationToken);

            if (response.AuditDetail is AttributeAuditDetail detail)
            {
                var keys = detail.NewValue?.Attributes.Keys
                    .Union(detail.OldValue?.Attributes.Keys ?? Enumerable.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    ?? Enumerable.Empty<string>();

                foreach (var attributeName in keys)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(attributeName))
                    {
                        continue;
                    }

                    object? newRaw = null;
                    object? oldRaw = null;

                    detail.NewValue?.Attributes.TryGetValue(attributeName, out newRaw);
                    detail.OldValue?.Attributes.TryGetValue(attributeName, out oldRaw);

                    var newFormatted = FormatAuditValue(
                        newRaw,
                        detail.NewValue?.FormattedValues,
                        attributeName);

                    var oldFormatted = FormatAuditValue(
                        oldRaw,
                        detail.OldValue?.FormattedValues,
                        attributeName);

                    auditRecord.AddFieldChange(new AuditFieldChange
                    {
                        FieldName = attributeName,
                        OldValue = oldFormatted,
                        NewValue = newFormatted,
                        FieldType = (newRaw ?? oldRaw)?.GetType().Name ?? "unknown"
                    });
                }

                return; // Con detalles completos no necesitamos el fallback XML
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RetrieveAuditDetailsRequest failed for {AuditId}; will fallback to changedata", auditRecord.AuditId);
        }

        // Fallback: parsear changedata (puede venir en XML/JSON)
        auditRecord.AdditionalData.TryGetValue("changedata", out var cachedChangedata);
        var changeData = cachedChangedata as string;

        var fallback = !string.IsNullOrWhiteSpace(changeData)
            ? ParseChangeData(changeData!)
            : new Dictionary<string, AuditFieldChange>();

        auditRecord.Changes = fallback;
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

    private static string FormatAuditValue(
        object? rawValue,
        FormattedValueCollection? formattedValues,
        string attributeName)
    {
        if (formattedValues != null && formattedValues.TryGetValue(attributeName, out var formatted))
        {
            return formatted;
        }

        return rawValue switch
        {
            null => string.Empty,
            EntityReference er when !string.IsNullOrWhiteSpace(er.Name) => er.Name,
            EntityReference er => er.Id.ToString(),
            OptionSetValue osv => osv.Value.ToString(),
            Money money => money.Value.ToString("F2"),
            DateTime dt => dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            bool b => b ? "true" : "false",
            _ => rawValue.ToString() ?? string.Empty
        };
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

    private static bool IsThrottlingFault(FaultException<OrganizationServiceFault> ex)
    {
        var fault = ex.Detail;
        if (fault == null)
        {
            return false;
        }

        if (fault.ErrorDetails != null && fault.ErrorDetails.TryGetValue("HttpStatusCode", out var statusObj)
            && int.TryParse(statusObj?.ToString(), out var status)
            && status == 429)
        {
            return true;
        }

        return fault.ErrorCode == -2147015902 // Throttling limit exceeded
            || fault.Message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || fault.Message.Contains("throttling", StringComparison.OrdinalIgnoreCase);
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
        var client = await GetServiceClientAsync();
        var records = await RetrieveRecordChangeHistoryAsync(
            client,
            entityName,
            recordId,
            fromDate,
            toDate,
            cancellationToken);

        return records.OrderBy(r => r.CreatedOn).ToList();
    }

    private async Task<List<AuditRecord>> RetrieveRecordChangeHistoryAsync(
        ServiceClient client,
        string entityName,
        Guid recordId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var result = new List<AuditRecord>();
        var pageNumber = 1;
        var more = true;
        string? pagingCookie = null;

        while (more && !cancellationToken.IsCancellationRequested)
        {
            var request = new RetrieveRecordChangeHistoryRequest
            {
                Target = new EntityReference(entityName, recordId),
                PagingInfo = new PagingInfo
                {
                    PageNumber = pageNumber,
                    Count = 5000,
                    PagingCookie = pagingCookie
                }
            };

            var response = (RetrieveRecordChangeHistoryResponse)await _retryPolicy.ExecuteAsync(
                async ct => (RetrieveRecordChangeHistoryResponse)await Task.Run(() => client.Execute(request), ct),
                cancellationToken);

            foreach (var detail in response.AuditDetailCollection?.AuditDetails.OfType<AttributeAuditDetail>()
                ?? Enumerable.Empty<AttributeAuditDetail>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var auditEntity = detail.AuditRecord;
                var auditId = auditEntity?.Id ?? Guid.Empty;
                var createdOn = auditEntity?.GetAttributeValue<DateTime>("createdon") ?? DateTime.MinValue;

                if (fromDate.HasValue && createdOn < fromDate.Value)
                {
                    continue;
                }

                if (toDate.HasValue && createdOn > toDate.Value)
                {
                    continue;
                }

                var record = new AuditRecord
                {
                    AuditId = auditId,
                    CreatedOn = createdOn,
                    EntityName = entityName,
                    LogicalName = entityName,
                    RecordId = recordId,
                    Operation = GetOperationName(auditEntity?.GetAttributeValue<OptionSetValue>("action")?.Value ?? 0),
                    UserId = auditEntity?.GetAttributeValue<EntityReference>("userid")?.Id.ToString() ?? string.Empty,
                    UserName = auditEntity?.GetAttributeValue<EntityReference>("userid")?.Name ?? string.Empty,
                    TransactionId = auditEntity?.GetAttributeValue<Guid?>("transactionid")?.ToString()
                };

                var keys = detail.NewValue?.Attributes.Keys
                    .Union(detail.OldValue?.Attributes.Keys ?? Enumerable.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    ?? Enumerable.Empty<string>();

                foreach (var attributeName in keys)
                {
                    if (string.IsNullOrWhiteSpace(attributeName))
                    {
                        continue;
                    }

                    object? newRaw = null;
                    object? oldRaw = null;

                    detail.NewValue?.Attributes.TryGetValue(attributeName, out newRaw);
                    detail.OldValue?.Attributes.TryGetValue(attributeName, out oldRaw);

                    var newFormatted = FormatAuditValue(
                        newRaw,
                        detail.NewValue?.FormattedValues,
                        attributeName);

                    var oldFormatted = FormatAuditValue(
                        oldRaw,
                        detail.OldValue?.FormattedValues,
                        attributeName);

                    record.AddFieldChange(new AuditFieldChange
                    {
                        FieldName = attributeName,
                        OldValue = oldFormatted,
                        NewValue = newFormatted,
                        FieldType = (newRaw ?? oldRaw)?.GetType().Name ?? "unknown"
                    });
                }

                result.Add(record);
            }

            more = response.AuditDetailCollection?.MoreRecords ?? false;
            pagingCookie = response.AuditDetailCollection?.PagingCookie;
            pageNumber++;
        }

        return result;
    }

    public async Task<AuditStatistics> GetAuditStatisticsAsync(
        ExtractionCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var records = await ExtractAuditRecordsAsync(
            criteria,
            (IProgress<ExtractionProgress>?)null,
            cancellationToken);

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
        _connectionLock.Dispose();
    }
}
