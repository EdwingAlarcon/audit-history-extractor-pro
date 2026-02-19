using AuditHistoryExtractorPro.Core.Models;
using Microsoft.Xrm.Sdk.Query;
using System.Text;

namespace AuditHistoryExtractorPro.Core.Services;

public class QueryBuilderService
{
    public QueryExpression BuildAuditQuery(
        AuditQueryFilters filters,
        int pageNumber,
        string? pagingCookie,
        int pageSize)
    {
        var query = new QueryExpression("audit")
        {
            ColumnSet = new ColumnSet(
                "auditid",
                "createdon",
                "operation",
                "action",
                "attributemask",
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

        query.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, filters.EntityName);

        var (fromDate, toDate) = ResolveDateRange(filters);
        if (fromDate.HasValue)
        {
            query.Criteria.AddCondition("createdon", ConditionOperator.GreaterEqual, fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query.Criteria.AddCondition("createdon", ConditionOperator.LessEqual, toDate.Value);
        }

        var operations = filters.SelectedOperations?.Where(value => value > 0).Distinct().ToArray() ?? Array.Empty<int>();
        if (operations.Length == 0 && filters.SelectedOperation.HasValue)
        {
            operations = new[] { (int)filters.SelectedOperation.Value };
        }

        if (operations.Length > 0)
        {
            query.Criteria.AddCondition("operation", ConditionOperator.In, operations.Cast<object>().ToArray());
        }

        var actions = filters.SelectedActions?.Where(value => value > 0).Distinct().ToArray() ?? Array.Empty<int>();
        if (actions.Length > 0)
        {
            query.Criteria.AddCondition("action", ConditionOperator.In, actions.Cast<object>().ToArray());
        }

        if (filters.SelectedUser is not null)
        {
            query.Criteria.AddCondition("userid", ConditionOperator.Equal, filters.SelectedUser.Id);
        }

        if (!string.IsNullOrWhiteSpace(filters.RecordId) && Guid.TryParse(filters.RecordId, out var recordId))
        {
            query.Criteria.AddCondition("objectid", ConditionOperator.Equal, recordId);
        }

        query.Orders.Add(new OrderExpression("createdon", OrderType.Ascending));
        return query;
    }

    public QueryExpression BuildQueryExpression(
        AuditQueryFilters filters,
        int pageNumber,
        string? pagingCookie,
        int pageSize)
    {
        return BuildAuditQuery(filters, pageNumber, pagingCookie, pageSize);
    }

    public string BuildFetchXml(AuditQueryFilters filters, int pageNumber, int pageSize, string? pagingCookie = null)
    {
        var (fromDate, toDate) = ResolveDateRange(filters);
        var sb = new StringBuilder();
        sb.Append($"<fetch version='1.0' output-format='xml-platform' mapping='logical' count='{pageSize}' page='{pageNumber}'");
        if (!string.IsNullOrWhiteSpace(pagingCookie))
        {
            sb.Append($" paging-cookie='{System.Security.SecurityElement.Escape(pagingCookie)}'");
        }

        sb.Append(">\n  <entity name='audit'>\n");
        sb.Append("    <attribute name='auditid'/>\n");
        sb.Append("    <attribute name='createdon'/>\n");
        sb.Append("    <attribute name='operation'/>\n");
        sb.Append("    <attribute name='action'/>\n");
        sb.Append("    <attribute name='attributemask'/>\n");
        sb.Append("    <attribute name='objectid'/>\n");
        sb.Append("    <attribute name='objecttypecode'/>\n");
        sb.Append("    <attribute name='userid'/>\n");
        sb.Append("    <attribute name='transactionid'/>\n");
        sb.Append("    <attribute name='changedata'/>\n");
        sb.Append("    <filter type='and'>\n");
        sb.Append($"      <condition attribute='objecttypecode' operator='eq' value='{filters.EntityName}' />\n");

        if (fromDate.HasValue)
        {
            sb.Append($"      <condition attribute='createdon' operator='on-or-after' value='{fromDate.Value:yyyy-MM-ddTHH:mm:ssZ}' />\n");
        }

        if (toDate.HasValue)
        {
            sb.Append($"      <condition attribute='createdon' operator='on-or-before' value='{toDate.Value:yyyy-MM-ddTHH:mm:ssZ}' />\n");
        }

        var operations = filters.SelectedOperations?.Where(value => value > 0).Distinct().ToArray() ?? Array.Empty<int>();
        if (operations.Length == 0 && filters.SelectedOperation.HasValue)
        {
            operations = new[] { (int)filters.SelectedOperation.Value };
        }

        if (operations.Length > 0)
        {
            sb.Append("      <condition attribute='operation' operator='in'>\n");
            foreach (var operation in operations)
            {
                sb.Append($"        <value>{operation}</value>\n");
            }

            sb.Append("      </condition>\n");
        }

        var actions = filters.SelectedActions?.Where(value => value > 0).Distinct().ToArray() ?? Array.Empty<int>();
        if (actions.Length > 0)
        {
            sb.Append("      <condition attribute='action' operator='in'>\n");
            foreach (var action in actions)
            {
                sb.Append($"        <value>{action}</value>\n");
            }

            sb.Append("      </condition>\n");
        }

        if (filters.SelectedUser is not null)
        {
            sb.Append($"      <condition attribute='userid' operator='eq' value='{filters.SelectedUser.Id}' />\n");
        }

        if (!string.IsNullOrWhiteSpace(filters.RecordId) && Guid.TryParse(filters.RecordId, out var recordId))
        {
            sb.Append($"      <condition attribute='objectid' operator='eq' value='{recordId}' />\n");
        }

        sb.Append("    </filter>\n");
        sb.Append("    <order attribute='createdon' descending='false' />\n");
        sb.Append("  </entity>\n</fetch>");

        return sb.ToString();
    }

    private static (DateTime? fromDate, DateTime? toDate) ResolveDateRange(AuditQueryFilters filters)
    {
        var explicitFrom = filters.StartDate ?? filters.SelectedDateFrom;
        var explicitTo = filters.EndDate ?? filters.SelectedDateTo;

        if (explicitFrom.HasValue || explicitTo.HasValue)
        {
            if (filters.IsFullDay)
            {
                var fromDate = explicitFrom?.Date;
                var toDate = explicitTo?.Date.AddDays(1).AddTicks(-1);
                return (fromDate, toDate);
            }

            return (explicitFrom, explicitTo);
        }

        var now = DateTime.UtcNow;
        return filters.SelectedDateRange switch
        {
            DateRangeFilter.Hoy => (now.Date, now),
            DateRangeFilter.Semana => (now.Date.AddDays(-7), now),
            DateRangeFilter.Mes => (now.Date.AddMonths(-1), now),
            _ => (null, null)
        };
    }
}
