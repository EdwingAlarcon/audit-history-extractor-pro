using AuditHistoryExtractorPro.Core.Models;
using Microsoft.Xrm.Sdk.Query;

namespace AuditHistoryExtractorPro.Core.Services;

public sealed class DataService : IDataService
{
    private readonly AuditService _auditService;

    public DataService(AuditService auditService)
    {
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<UserDTO>> SearchUsersAsync(string query, CancellationToken cancellationToken = default)
    {
        var client = _auditService.ServiceClient;
        if (client is null || !client.IsReady)
        {
            return Array.Empty<UserDTO>();
        }

        var trimmed = query?.Trim() ?? string.Empty;

        var searchFilter = new FilterExpression(LogicalOperator.Or);
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            searchFilter.AddCondition("fullname", ConditionOperator.Like, $"%{trimmed}%");
            searchFilter.AddCondition("domainname", ConditionOperator.Like, $"%{trimmed}%");
        }

        var criteria = new FilterExpression(LogicalOperator.And);
        criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
        if (searchFilter.Conditions.Count > 0)
        {
            criteria.AddFilter(searchFilter);
        }

        var queryExpression = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid", "fullname", "domainname"),
            Criteria = criteria,
            TopCount = 50
        };
        queryExpression.Orders.Add(new OrderExpression("fullname", OrderType.Ascending));

        var result = await Task.Run(() => client.RetrieveMultiple(queryExpression), cancellationToken);
        return result.Entities
            .Select(e => new UserDTO
            {
                Id = e.GetAttributeValue<Guid>("systemuserid"),
                Name = e.GetAttributeValue<string>("fullname")
                    ?? e.GetAttributeValue<string>("domainname")
                    ?? "(sin nombre)"
            })
            .Where(u => u.Id != Guid.Empty)
            .ToList();
    }
}
