using AuditHistoryExtractorPro.Core.Models;
using DataverseServiceClient = Microsoft.PowerPlatform.Dataverse.Client.ServiceClient;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace AuditHistoryExtractorPro.Core.Services;

public sealed class MetadataService : IMetadataService
{
    private readonly AuditService _auditService;
    private readonly SemaphoreSlim _entityCacheLock = new(1, 1);
    private readonly SemaphoreSlim _viewCacheLock = new(1, 1);
    private readonly SemaphoreSlim _attributeCacheLock = new(1, 1);

    private IReadOnlyList<EntityDTO>? _auditableEntitiesCache;
    private readonly Dictionary<string, IReadOnlyList<ViewDTO>> _viewsByEntityCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<AttributeDTO>> _attributesByEntityCache = new(StringComparer.OrdinalIgnoreCase);

    public MetadataService(AuditService auditService)
    {
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<EntityDTO>> GetAuditableEntitiesAsync(CancellationToken cancellationToken = default)
    {
        if (_auditableEntitiesCache is not null)
        {
            return _auditableEntitiesCache;
        }

        await _entityCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_auditableEntitiesCache is not null)
            {
                return _auditableEntitiesCache;
            }

            var client = GetReadyClient();

            var request = new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            };

            var response = await client.ExecuteAsync(request, cancellationToken) as RetrieveAllEntitiesResponse;
            if (response?.EntityMetadata is null)
            {
                return Array.Empty<EntityDTO>();
            }

            _auditableEntitiesCache = response.EntityMetadata
                .Where(e =>
                    e.IsAuditEnabled?.Value == true &&
                    e.MetadataId != null &&
                    e.MetadataId != Guid.Empty &&
                    !string.IsNullOrWhiteSpace(e.LogicalName) &&
                    !string.IsNullOrWhiteSpace(ResolveDisplayName(e)) &&
                    !((e.LogicalName?.IndexOf("(En desuso)", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    && !((e.LogicalName?.IndexOf("(Deprecated)", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    && !((ResolveDisplayName(e)?.IndexOf("(En desuso)", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    && !((ResolveDisplayName(e)?.IndexOf("(Deprecated)", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                )
                .Select(e => new EntityDTO
                {
                    LogicalName = e.LogicalName ?? string.Empty,
                    DisplayName = ResolveDisplayName(e),
                    ObjectTypeCode = e.ObjectTypeCode
                })
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return _auditableEntitiesCache;
        }
        finally
        {
            _entityCacheLock.Release();
        }
    }

    public async Task<IReadOnlyList<ViewDTO>> GetSystemViewsAsync(string entityLogicalName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return Array.Empty<ViewDTO>();
        }

        var normalizedEntity = entityLogicalName.Trim();
        if (_viewsByEntityCache.TryGetValue(normalizedEntity, out var cachedViews))
        {
            return cachedViews;
        }

        await _viewCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_viewsByEntityCache.TryGetValue(normalizedEntity, out cachedViews))
            {
                return cachedViews;
            }

            var client = GetReadyClient();
            var query = new QueryExpression("savedquery")
            {
                ColumnSet = new ColumnSet("name", "savedqueryid", "fetchxml"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("returnedtypecode", ConditionOperator.Equal, normalizedEntity),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    },
                    Filters =
                    {
                        new FilterExpression(LogicalOperator.Or)
                        {
                            Conditions =
                            {
                                new ConditionExpression("querytype", ConditionOperator.Equal, 0),
                                new ConditionExpression("querytype", ConditionOperator.Equal, 1)
                            }
                        }
                    }
                }
            };

            query.Orders.Add(new OrderExpression("name", OrderType.Ascending));

            var result = await Task.Run(() => client.RetrieveMultiple(query), cancellationToken);
            var views = result.Entities
                .Select(e => new ViewDTO
                {
                    Id = e.GetAttributeValue<Guid>("savedqueryid"),
                    Name = e.GetAttributeValue<string>("name") ?? "(sin nombre)",
                    FetchXml = e.GetAttributeValue<string>("fetchxml") ?? string.Empty
                })
                .Where(v => v.Id != Guid.Empty)
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _viewsByEntityCache[normalizedEntity] = views;
            return views;
        }
        finally
        {
            _viewCacheLock.Release();
        }
    }

    public async Task<IReadOnlyList<AttributeDTO>> GetEntityAttributesAsync(string entityLogicalName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return Array.Empty<AttributeDTO>();
        }

        var normalizedEntity = entityLogicalName.Trim();
        if (_attributesByEntityCache.TryGetValue(normalizedEntity, out var cachedAttributes))
        {
            return cachedAttributes;
        }

        await _attributeCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_attributesByEntityCache.TryGetValue(normalizedEntity, out cachedAttributes))
            {
                return cachedAttributes;
            }

            var client = GetReadyClient();
            var request = new RetrieveEntityRequest
            {
                LogicalName = normalizedEntity,
                EntityFilters = EntityFilters.Attributes,
                RetrieveAsIfPublished = true
            };

            var response = await client.ExecuteAsync(request, cancellationToken) as RetrieveEntityResponse;
            if (response?.EntityMetadata?.Attributes is null)
            {
                return Array.Empty<AttributeDTO>();
            }

            var attributes = response.EntityMetadata.Attributes
                .Where(a => !string.IsNullOrWhiteSpace(a.LogicalName))
                .Select(a => new AttributeDTO
                {
                    LogicalName = a.LogicalName ?? string.Empty,
                    DisplayName = ResolveDisplayName(a),
                    ColumnNumber = a.ColumnNumber
                })
                .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _attributesByEntityCache[normalizedEntity] = attributes;
            return attributes;
        }
        finally
        {
            _attributeCacheLock.Release();
        }
    }

    private DataverseServiceClient GetReadyClient()
    {
        var client = _auditService.ServiceClient;
        if (client is null || !client.IsReady)
        {
            throw new InvalidOperationException("No hay conexión activa a Dataverse. Conéctate antes de cargar metadatos.");
        }

        return client;
    }

    private static string ResolveDisplayName(EntityMetadata metadata)
    {
        var localized = metadata.DisplayName?.UserLocalizedLabel?.Label;
        if (!string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        var firstLabel = metadata.DisplayName?.LocalizedLabels?.FirstOrDefault()?.Label;
        if (!string.IsNullOrWhiteSpace(firstLabel))
        {
            return firstLabel;
        }

        return metadata.LogicalName ?? string.Empty;
    }

    private static string ResolveDisplayName(AttributeMetadata metadata)
    {
        var localized = metadata.DisplayName?.UserLocalizedLabel?.Label;
        if (!string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        var firstLabel = metadata.DisplayName?.LocalizedLabels?.FirstOrDefault()?.Label;
        if (!string.IsNullOrWhiteSpace(firstLabel))
        {
            return firstLabel;
        }

        return metadata.LogicalName ?? string.Empty;
    }
}
