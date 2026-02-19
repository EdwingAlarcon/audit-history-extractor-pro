using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using DataverseServiceClient = Microsoft.PowerPlatform.Dataverse.Client.ServiceClient;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AuditHistoryExtractorPro.Core.Services;

public class AuditService : IAuditService
{
    private const int MaxDataversePageSize = 5000;
    private readonly AuthHelper _authHelper;
    private readonly QueryBuilderService _queryBuilderService;
    private readonly IExcelExportService _excelExportService;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _nameResolutionLock = new(1, 1);
    private DataverseServiceClient? _serviceClient;
    private IAuthenticationProvider? _authenticationProvider;
    private AuthenticationConfiguration? _authenticationConfiguration;
    private Dictionary<string, Dictionary<int, string>> _optionSetCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, string> _attributeByColumnNumber = new();
    private Dictionary<string, string> _lookupTargetByAttribute = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _primaryNameAttributeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _nameResolutionCache = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConnected { get; private set; }
    public string OrganizationName { get; private set; } = string.Empty;
    internal DataverseServiceClient? ServiceClient => _serviceClient;

    public AuditService(
        AuthHelper authHelper,
        QueryBuilderService queryBuilderService,
        IExcelExportService excelExportService)
    {
        _authHelper = authHelper;
        _queryBuilderService = queryBuilderService;
        _excelExportService = excelExportService;
    }

    public async Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            IsConnected = false;
            OrganizationName = string.Empty;

            _authenticationConfiguration = _authHelper.BuildConfiguration(settings);
            _authenticationProvider = _authHelper.CreateProvider(_authenticationConfiguration);

            var dataverseUri = new Uri(_authenticationConfiguration.EnvironmentUrl, UriKind.Absolute);
            _serviceClient = new DataverseServiceClient(
                dataverseUri,
                async _ => await _authenticationProvider.GetAccessTokenAsync(cancellationToken),
                useUniqueInstance: true,
                logger: null);

            if (!_serviceClient.IsReady)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(_serviceClient.LastError)
                    ? "No fue posible inicializar el cliente Dataverse."
                    : _serviceClient.LastError);
            }

            var whoAmIResponse = await _serviceClient.ExecuteAsync(new WhoAmIRequest(), cancellationToken);
            if (whoAmIResponse is null)
            {
                throw new InvalidOperationException("WhoAmI no devolvió respuesta.");
            }

            IsConnected = true;
            OrganizationName = string.IsNullOrWhiteSpace(_serviceClient.ConnectedOrgFriendlyName)
                ? dataverseUri.Host
                : _serviceClient.ConnectedOrgFriendlyName;

            _optionSetCache.Clear();
            _attributeByColumnNumber.Clear();
            _lookupTargetByAttribute.Clear();
            _primaryNameAttributeCache.Clear();
            _nameResolutionCache.Clear();
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

    public async Task<IReadOnlyList<LookupItem>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceClient is null || !_serviceClient.IsReady)
        {
            return Array.Empty<LookupItem>();
        }

        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid", "fullname"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("isdisabled", ConditionOperator.Equal, false)
                }
            },
            TopCount = 200
        };

        query.Orders.Add(new OrderExpression("fullname", OrderType.Ascending));

        var users = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);
        return users.Entities
            .Select(e => new LookupItem
            {
                Id = e.GetAttributeValue<Guid>("systemuserid"),
                Name = e.GetAttributeValue<string>("fullname") ?? "(sin nombre)"
            })
            .Where(u => u.Id != Guid.Empty)
            .ToList();
    }

    public async Task<AuditHistoryExtractorPro.Core.Models.ExtractionResult> ExtractAuditHistoryAsync(
        ExtractionRequest request,
        string outputFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            if (_serviceClient is null || !_serviceClient.IsReady)
            {
                return AuditHistoryExtractorPro.Core.Models.ExtractionResult.Fail("No hay conexión activa a Dataverse.");
            }

            var filePath = ResolveOutputPath(outputFilePath, request.EntityName);
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return AuditHistoryExtractorPro.Core.Models.ExtractionResult.Fail("La ruta de salida no es válida.");
            }

            Directory.CreateDirectory(directory);
            if (!filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                filePath = Path.ChangeExtension(filePath, ".xlsx");
            }

            await LoadEntityMetadataContextAsync(request.EntityName, cancellationToken);

            progress?.Report("Iniciando extracción de auditoría...");

            var totalWritten = 0;
            var asyncRows = StreamRowsAsync(request, progress, count => totalWritten = count, cancellationToken);
            await _excelExportService.ExportAsync(filePath, asyncRows, cancellationToken);

            progress?.Report($"Extracción completada. Total: {totalWritten} registros.");
            return AuditHistoryExtractorPro.Core.Models.ExtractionResult.Ok(totalWritten, filePath, $"Extracción completada. Archivo generado en: {filePath}");
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

    private async IAsyncEnumerable<AuditExportRow> StreamRowsAsync(
        ExtractionRequest request,
        IProgress<string>? progress,
        Action<int> updateCount,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var totalWritten = 0;
        var pageNumber = 1;
        var moreRecords = true;
        string? pagingCookie = null;

        var filters = new AuditQueryFilters
        {
            EntityName = request.EntityName,
            SelectedDateRange = request.SelectedDateRange,
            SelectedDateFrom = request.SelectedDateFrom,
            SelectedDateTo = request.SelectedDateTo,
            IsFullDay = request.IsFullDay,
            SelectedUser = request.SelectedUser,
            SelectedOperation = request.SelectedOperation,
            SelectedOperations = request.SelectedOperations,
            SelectedActions = request.SelectedActions,
            SelectedAttributes = request.SelectedAttributes,
            RecordId = request.RecordId,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };

        var selectedAttributes = new HashSet<string>(request.SelectedAttributes, StringComparer.OrdinalIgnoreCase);

        while (moreRecords && !cancellationToken.IsCancellationRequested && totalWritten < request.MaxRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = request.MaxRecords - totalWritten;
            var pageSize = Math.Min(MaxDataversePageSize, remaining);
            var query = _queryBuilderService.BuildQueryExpression(filters, pageNumber, pagingCookie, pageSize);

            progress?.Report($"Consultando página {pageNumber}...");

            var response = await Task.Run(() => _serviceClient!.RetrieveMultiple(query), cancellationToken);
            if (response.Entities.Count == 0)
            {
                yield break;
            }

            var startIndex = totalWritten + 1;
            foreach (var entity in response.Entities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var changeData = entity.GetAttributeValue<string>("changedata") ?? string.Empty;
                var attributeMask = entity.GetAttributeValue<string>("attributemask") ?? string.Empty;
                var change = ParseChangeData(changeData);

                if (!ShouldIncludeRecord(attributeMask, change.field, selectedAttributes))
                {
                    continue;
                }

                var row = await BuildExportRowAsync(entity, change, cancellationToken);
                totalWritten++;
                updateCount(totalWritten);
                yield return row;

                if (totalWritten >= request.MaxRecords)
                {
                    break;
                }
            }

            progress?.Report($"Escribiendo registros {startIndex}-{totalWritten}...");

            moreRecords = response.MoreRecords && totalWritten < request.MaxRecords;
            if (moreRecords)
            {
                pageNumber++;
                pagingCookie = response.PagingCookie;
            }

            response.Entities.Clear();
        }
    }

    private async Task<AuditExportRow> BuildExportRowAsync(
        Entity entity,
        (string field, string oldValue, string newValue) parsedChange,
        CancellationToken cancellationToken)
    {
        var auditId = entity.GetAttributeValue<Guid>("auditid");
        var createdOn = entity.GetAttributeValue<DateTime>("createdon");
        var objectReference = entity.GetAttributeValue<EntityReference>("objectid");
        var objectId = objectReference?.Id;
        var logicalName = objectReference?.LogicalName
            ?? entity.GetAttributeValue<string>("objecttypecode")
            ?? string.Empty;
        var actionCode = entity.GetAttributeValue<OptionSetValue>("action")?.Value ?? 0;
        var userRef = entity.GetAttributeValue<EntityReference>("userid");
        var transactionId = entity.GetAttributeValue<Guid?>("transactionid");
        var fieldName = parsedChange.field;
        var oldValue = await ResolveNameIfReferenceAsync(parsedChange.oldValue, fieldName, cancellationToken);
        var newValue = await ResolveNameIfReferenceAsync(parsedChange.newValue, fieldName, cancellationToken);

        var recordId = objectId?.ToString("D") ?? string.Empty;

        return new AuditExportRow
        {
            AuditId = auditId.ToString(),
            CreatedOn = createdOn == default ? string.Empty : createdOn.ToUniversalTime().ToString("O"),
            EntityName = logicalName,
            RecordId = recordId,
            LogicalName = logicalName,
            RecordUrl = BuildRecordUrl(logicalName, recordId),
            ActionCode = actionCode,
            ActionName = GetOperationName(actionCode),
            UserId = userRef?.Id.ToString() ?? string.Empty,
            UserName = userRef?.Name ?? string.Empty,
            TransactionId = transactionId?.ToString() ?? string.Empty,
            ChangedField = fieldName,
            OldValue = oldValue,
            NewValue = newValue
        };
    }

    private string BuildRecordUrl(string logicalName, string recordId)
    {
        if (string.IsNullOrWhiteSpace(logicalName) || string.IsNullOrWhiteSpace(recordId))
        {
            return string.Empty;
        }

        var baseUrl = _authenticationConfiguration?.EnvironmentUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        return $"{baseUrl}/main.aspx?etn={WebUtility.UrlEncode(logicalName)}&id={WebUtility.UrlEncode(recordId)}&pagetype=entityrecord";
    }

    private (string field, string oldValue, string newValue) ParseChangeData(string changeData)
    {
        if (string.IsNullOrWhiteSpace(changeData))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        try
        {
            var document = XDocument.Parse(changeData);
            var firstAttribute = document.Descendants("attribute").FirstOrDefault();
            if (firstAttribute is null)
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            var fieldName = firstAttribute.Attribute("name")?.Value ?? string.Empty;
            var oldValue = firstAttribute.Element("oldValue")?.Value ?? string.Empty;
            var newValue = firstAttribute.Element("newValue")?.Value ?? string.Empty;

            if (_optionSetCache.TryGetValue(fieldName, out var labels))
            {
                if (int.TryParse(oldValue, out var oldCode) && labels.TryGetValue(oldCode, out var oldLabel))
                {
                    oldValue = oldLabel;
                }

                if (int.TryParse(newValue, out var newCode) && labels.TryGetValue(newCode, out var newLabel))
                {
                    newValue = newLabel;
                }
            }

            return (fieldName, oldValue, newValue);
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty);
        }
    }

    private async Task LoadEntityMetadataContextAsync(
        string entityName,
        CancellationToken cancellationToken)
    {
        if (_serviceClient is null)
        {
            _optionSetCache = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
            _attributeByColumnNumber = new Dictionary<int, string>();
            _lookupTargetByAttribute = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var request = new RetrieveEntityRequest
        {
            LogicalName = entityName,
            EntityFilters = EntityFilters.Attributes | EntityFilters.Entity,
            RetrieveAsIfPublished = true
        };

        var response = (RetrieveEntityResponse)await _serviceClient.ExecuteAsync(request, cancellationToken);
        var cache = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        var attributeByColumnNumber = new Dictionary<int, string>();
        var lookupTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(response.EntityMetadata.PrimaryNameAttribute))
        {
            _primaryNameAttributeCache[entityName] = response.EntityMetadata.PrimaryNameAttribute;
        }

        foreach (var attribute in response.EntityMetadata.Attributes)
        {
            if (attribute.ColumnNumber.HasValue && !string.IsNullOrWhiteSpace(attribute.LogicalName))
            {
                attributeByColumnNumber[attribute.ColumnNumber.Value] = attribute.LogicalName;
            }

            if (attribute is LookupAttributeMetadata lookupAttribute
                && !string.IsNullOrWhiteSpace(attribute.LogicalName)
                && lookupAttribute.Targets is { Length: > 0 })
            {
                lookupTargets[attribute.LogicalName] = lookupAttribute.Targets[0];
            }

            if (attribute is not EnumAttributeMetadata enumAttribute)
            {
                continue;
            }

            var labels = enumAttribute.OptionSet?.Options?
                .Where(o => o.Value.HasValue)
                .ToDictionary(
                    o => o.Value!.Value,
                    o => o.Label?.UserLocalizedLabel?.Label
                        ?? o.Label?.LocalizedLabels?.FirstOrDefault()?.Label
                        ?? o.Value!.Value.ToString())
                ?? new Dictionary<int, string>();

            if (labels.Count > 0 && !string.IsNullOrWhiteSpace(enumAttribute.LogicalName))
            {
                cache[enumAttribute.LogicalName] = labels;
            }
        }

        _optionSetCache = cache;
        _attributeByColumnNumber = attributeByColumnNumber;
        _lookupTargetByAttribute = lookupTargets;
    }

    private bool ShouldIncludeRecord(
        string attributeMask,
        string changedField,
        HashSet<string> selectedAttributes)
    {
        if (selectedAttributes.Count == 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(changedField) && selectedAttributes.Contains(changedField))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(attributeMask))
        {
            return false;
        }

        var tokens = attributeMask.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var columnNumber))
            {
                continue;
            }

            if (_attributeByColumnNumber.TryGetValue(columnNumber, out var logicalName)
                && selectedAttributes.Contains(logicalName))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string> ResolveNameIfReferenceAsync(string value, string fieldName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var guid = ExtractGuid(value);
        if (!guid.HasValue)
        {
            return value;
        }

        var targetEntity = ResolveTargetEntity(fieldName);
        if (string.IsNullOrWhiteSpace(targetEntity))
        {
            return value;
        }

        var resolvedName = await ResolveEntityPrimaryNameAsync(targetEntity, guid.Value, cancellationToken);
        return string.IsNullOrWhiteSpace(resolvedName) ? value : resolvedName;
    }

    private string? ResolveTargetEntity(string fieldName)
    {
        if (_lookupTargetByAttribute.TryGetValue(fieldName, out var target))
        {
            return target;
        }

        return null;
    }

    private async Task<string?> ResolveEntityPrimaryNameAsync(string entityLogicalName, Guid id, CancellationToken cancellationToken)
    {
        if (_serviceClient is null || !_serviceClient.IsReady)
        {
            return null;
        }

        var cacheKey = $"{entityLogicalName}:{id:D}";
        if (_nameResolutionCache.TryGetValue(cacheKey, out var cachedName))
        {
            return cachedName;
        }

        await _nameResolutionLock.WaitAsync(cancellationToken);
        try
        {
            if (_nameResolutionCache.TryGetValue(cacheKey, out cachedName))
            {
                return cachedName;
            }

            var primaryNameAttribute = await GetPrimaryNameAttributeAsync(entityLogicalName, cancellationToken);
            if (string.IsNullOrWhiteSpace(primaryNameAttribute))
            {
                return null;
            }

            var entity = await Task.Run(() => _serviceClient.Retrieve(entityLogicalName, id, new ColumnSet(primaryNameAttribute)), cancellationToken);
            var resolvedName = entity.GetAttributeValue<string>(primaryNameAttribute);
            if (!string.IsNullOrWhiteSpace(resolvedName))
            {
                _nameResolutionCache[cacheKey] = resolvedName;
            }

            return resolvedName;
        }
        catch
        {
            return null;
        }
        finally
        {
            _nameResolutionLock.Release();
        }
    }

    private async Task<string?> GetPrimaryNameAttributeAsync(string entityLogicalName, CancellationToken cancellationToken)
    {
        if (_primaryNameAttributeCache.TryGetValue(entityLogicalName, out var cachedPrimaryName))
        {
            return cachedPrimaryName;
        }

        if (_serviceClient is null || !_serviceClient.IsReady)
        {
            return null;
        }

        try
        {
            var request = new RetrieveEntityRequest
            {
                LogicalName = entityLogicalName,
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            };

            var response = (RetrieveEntityResponse)await _serviceClient.ExecuteAsync(request, cancellationToken);
            var primaryNameAttribute = response.EntityMetadata.PrimaryNameAttribute;
            if (!string.IsNullOrWhiteSpace(primaryNameAttribute))
            {
                _primaryNameAttributeCache[entityLogicalName] = primaryNameAttribute;
                return primaryNameAttribute;
            }
        }
        catch
        {
        }

        return null;
    }

    private static Guid? ExtractGuid(string value)
    {
        if (Guid.TryParse(value.Trim('{', '}'), out var directGuid))
        {
            return directGuid;
        }

        var match = Regex.Match(value, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        if (match.Success && Guid.TryParse(match.Value, out var embeddedGuid))
        {
            return embeddedGuid;
        }

        return null;
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
