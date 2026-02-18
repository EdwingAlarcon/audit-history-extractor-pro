using AuditHistoryExtractorPro.Models;
using AuditHistoryExtractorPro.Services.Resilience;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Polly;
using Polly.Retry;
using System.ServiceModel;

namespace AuditHistoryExtractorPro.Services.Core;

/// <summary>
/// Repositorio para operaciones de auditoría con Dataverse
/// Optimizado a nivel empresarial con:
/// - Políticas de resiliencia for 429 throttling
/// - Resolución de metadatos con caché
/// - Filtrado de campos ruidosos
/// - Paginación adaptativa para volúmenes altos
/// </summary>
public class DataverseAuditRepository : IAuditRepository
{
    private readonly IAuthenticationProvider _authProvider;
    private readonly AuthenticationConfiguration _config;
    private readonly ILogger<DataverseAuditRepository> _logger;
    private readonly ICacheService _cacheService;
    private readonly IAsyncPolicy _retryPolicy;
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

        // ⭐ MEJORADO: Usar política compuesta con manejo específico de 429
        _retryPolicy = ResiliencePolicy.CreateCompositePolicyBase(
            logger,
            timeout: TimeSpan.FromSeconds(120),
            maxThrottleRetries: 5);
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

            // ⭐ MEJORADO: Usar política de resiliencia con manejo de 429
            var response = await _retryPolicy.ExecuteAsync(async (ct) =>
            {
                return await Task.Run(() => client.RetrieveMultiple(query), ct);
            }, cancellationToken);

            // ⭐ NUEVO: Aplicar limpieza de campos ruidosos si está configurado
            var cleaningConfig = criteria.DataCleaningConfig;

            foreach (var entity in response.Entities)
            {
                var auditRecord = MapToAuditRecord(entity, cleaningConfig);
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

        // ⭐ NUEVO: Filtrar por AuditActionCode (nuevo mapeo forense completo)
        if (criteria.ActionCodes?.Any() == true)
        {
            var actionValues = criteria.ActionCodes.Select(o => (int)o).ToArray();
            query.Criteria.AddCondition("action", ConditionOperator.In, actionValues);
        }
        // Fallback: soportar legacy OperationType
        else if (criteria.Operations?.Any() == true)
        {
            var operationValues = criteria.Operations.Select(o => (int)o).ToArray();
            query.Criteria.AddCondition("action", ConditionOperator.In, operationValues);
        }

        // Filtrar por usuarios
        if (criteria.UserIds?.Any() == true)
        {
            query.Criteria.AddCondition("userid", ConditionOperator.In, criteria.UserIds.ToArray());
        }

        query.Orders.Add(new OrderExpression("createdon", OrderType.Descending));

        return query;
    }

    private AuditRecord MapToAuditRecord(
        Entity entity,
        DataCleaningConfiguration? cleaningConfig = null)
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
            auditRecord.Changes = ParseChangeData(changeData, auditRecord.EntityName, cleaningConfig);
        }

        return auditRecord;
    }

    private AuditRecord MapToAuditRecord(Entity entity)
    {
        return MapToAuditRecord(entity, null);
    }

    private Dictionary<string, AuditFieldChange> ParseChangeData(
        string changeData,
        string entityName,
        DataCleaningConfiguration? cleaningConfig = null)
    {
        var changes = new Dictionary<string, AuditFieldChange>();

        // ⭐ NUEVO: Obtener campos a excluir si está configurado
        var fieldsToExclude = cleaningConfig?.GetFieldsToExclude() ?? new HashSet<string>();

        try
        {
            // El formato de changedata puede ser XML o JSON dependiendo de la versión de Dataverse
            var doc = System.Xml.Linq.XDocument.Parse(changeData);
            var attributes = doc.Descendants("attribute");

            foreach (var attr in attributes)
            {
                var fieldName = attr.Attribute("name")?.Value ?? string.Empty;
                
                // ⭐ NUEVO: Filtrar campos ruidosos del sistema
                if (fieldsToExclude.Contains(fieldName))
                {
                    _logger.LogDebug(
                        "Excluding noisy field '{Field}' from entity '{Entity}'",
                        fieldName,
                        entityName);
                    continue;
                }

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
            _logger.LogError(ex, "Failed to parse change data for entity {Entity}", entityName);
        }

        return changes;
    }

    private Dictionary<string, AuditFieldChange> ParseChangeData(string changeData)
    {
        return ParseChangeData(changeData, "Unknown", null);
    }

    private string GetOperationName(int operationCode)
    {
        // ⭐ MEJORADO: Mapeo exhaustivo de ActionCode según SDK de Dataverse
        return operationCode switch
        {
            1 => "Create",
            2 => "Update",
            3 => "Delete",
            4 => "Associate",
            5 => "Disassociate",
            6 => "Assign",              // ⭐ NUEVO
            7 => "Share",               // ⭐ NUEVO
            8 => "Unshare",             // ⭐ NUEVO
            9 => "Merge",               // ⭐ NUEVO
            10 => "Reparent",           // ⭐ NUEVO
            11 => "Qualify",            // ⭐ NUEVO (Sales Process)
            12 => "Disqualify",         // ⭐ NUEVO (Sales Process)
            13 => "Win",                // ⭐ NUEVO (Sales Process)
            14 => "Lose",               // ⭐ NUEVO (Sales Process)
            15 => "Deactivate",         // ⭐ NUEVO
            16 => "Activate",           // ⭐ NUEVO
            19 => "Fulfill",            // ⭐ NUEVO
            21 => "CancelOrders",       // ⭐ NUEVO
            22 => "ConvertQuote",       // ⭐ NUEVO
            27 => "Archive",
            28 => "Restore",
            _ => $"Unknown ({operationCode})"
        };
    }

    /// <summary>
    /// Obtiene la categoría de análisis forense para una acción
    /// </summary>
    private string GetActionCategory(int operationCode)
    {
        return operationCode switch
        {
            1 or 2 or 3 => "CrudBasic",
            4 or 5 => "Relational",
            6 or 7 or 8 => "Security",
            9 or 10 => "Operations",
            11 or 12 or 13 or 14 => "SalesProcess",
            15 or 16 => "StatusChange",
            27 or 28 => "Maintenance",
            _ => "Unknown"
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
