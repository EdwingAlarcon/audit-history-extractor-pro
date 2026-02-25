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
                "callinguserid",
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

        // objecttypecode: siempre minúsculas para comparación exacta con el
        // nombre lógico de la entidad almacenado en Dataverse.
        var entityCode = (filters.EntityName ?? string.Empty).Trim().ToLowerInvariant();
        query.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, entityCode);

        var (fromDate, toDate) = ResolveDateRange(filters);
        if (fromDate.HasValue)
        {
            query.Criteria.AddCondition("createdon", ConditionOperator.GreaterEqual, fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query.Criteria.AddCondition("createdon", ConditionOperator.LessEqual, toDate.Value);
        }

        // ── Filtro de operación (BYPASS si lista vacía) ───────────────────────
        // Se usa ConditionExpression directamente para evitar el quirk del SDK:
        // AddCondition("field", In, params object[]) puede serializar el array
        // entero como UN SOLO valor cuando el compilador lo trata como objeto.
        // AddRange sobre Values garantiza que cada int sea un elemento separado.
        var operations = (filters.SelectedOperations ?? Array.Empty<int>())
            .Where(v => v > 0).Distinct().ToList();
        if (operations.Count == 0 && filters.SelectedOperation.HasValue)
        {
            operations = new List<int> { (int)filters.SelectedOperation.Value };
        }

        // Bypass: si no hay ninguna operación seleccionada → sin restricción.
        if (operations.Count > 0)
        {
            var opCond = new ConditionExpression("operation", ConditionOperator.In);
            opCond.Values.AddRange(operations.Select(v => (object)(int)v));
            query.Criteria.Conditions.Add(opCond);
        }

        // ── Filtro de acción (BYPASS si lista vacía) ──────────────────────────
        var actions = (filters.SelectedActions ?? Array.Empty<int>())
            .Where(v => v > 0).Distinct().ToList();
        if (actions.Count > 0)
        {
            var actCond = new ConditionExpression("action", ConditionOperator.In);
            actCond.Values.AddRange(actions.Select(v => (object)(int)v));
            query.Criteria.Conditions.Add(actCond);
        }

        // Filtro de usuario: TOTALMENTE OPCIONAL.
        // Solo se agrega si el usuario seleccionado tiene un GUID real (no nulo ni Guid.Empty).
        // SelectedUser == null  →  sin filtro  →  toda la auditoría de la entidad.
        // SelectedUser.Id == Guid.Empty  →  sentinel "Todos los usuarios"  →  sin filtro.
        if (filters.SelectedUser is not null && filters.SelectedUser.Id != Guid.Empty)
        {
            query.Criteria.AddCondition("userid", ConditionOperator.Equal, filters.SelectedUser.Id);
        }

        if (!string.IsNullOrWhiteSpace(filters.RecordId) && Guid.TryParse(filters.RecordId, out var recordId))
        {
            query.Criteria.AddCondition("objectid", ConditionOperator.Equal, recordId);
        }

        // objectid IN — filtro por IDs resueltos desde una Vista (chunk de max 500)
        if (filters.ObjectIds.Count > 0)
        {
            query.Criteria.AddCondition(
                "objectid",
                ConditionOperator.In,
                filters.ObjectIds.Select(id => (object)id).ToArray());
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
        sb.Append("    <attribute name='callinguserid'/>\n");
        sb.Append("    <attribute name='transactionid'/>\n");
        sb.Append("    <attribute name='changedata'/>\n");
        sb.Append("    <filter type='and'>\n");
        // objecttypecode: siempre minúsculas (coherente con BuildAuditQuery).
        var entityCodeFx = (filters.EntityName ?? string.Empty).Trim().ToLowerInvariant();
        sb.Append($"      <condition attribute='objecttypecode' operator='eq' value='{entityCodeFx}' />\n");

        if (fromDate.HasValue)
        {
            sb.Append($"      <condition attribute='createdon' operator='on-or-after' value='{fromDate.Value:yyyy-MM-ddTHH:mm:ssZ}' />\n");
        }

        if (toDate.HasValue)
        {
            sb.Append($"      <condition attribute='createdon' operator='on-or-before' value='{toDate.Value:yyyy-MM-ddTHH:mm:ssZ}' />\n");
        }

        // Bypass si no hay operaciones seleccionadas.
        var fxOperations = (filters.SelectedOperations ?? Array.Empty<int>())
            .Where(v => v > 0).Distinct().ToList();
        if (fxOperations.Count == 0 && filters.SelectedOperation.HasValue)
        {
            fxOperations = new List<int> { (int)filters.SelectedOperation.Value };
        }

        if (fxOperations.Count > 0)
        {
            sb.Append("      <condition attribute='operation' operator='in'>\n");
            foreach (var op in fxOperations)
                sb.Append($"        <value>{(int)op}</value>\n");
            sb.Append("      </condition>\n");
        }

        // Bypass si no hay acciones seleccionadas.
        var fxActions = (filters.SelectedActions ?? Array.Empty<int>())
            .Where(v => v > 0).Distinct().ToList();
        if (fxActions.Count > 0)
        {
            sb.Append("      <condition attribute='action' operator='in'>\n");
            foreach (var act in fxActions)
                sb.Append($"        <value>{(int)act}</value>\n");
            sb.Append("      </condition>\n");
        }

        // Filtro de usuario: TOTALMENTE OPCIONAL.
        // Solo se agrega si el usuario seleccionado tiene un GUID real (no nulo ni Guid.Empty).
        if (filters.SelectedUser is not null && filters.SelectedUser.Id != Guid.Empty)
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
        // ── GUARD: "Todos" = sin restricción de fechas ────────────────────────
        // Si el usuario seleccionó "Todo" (DateRangeFilter.Todo), no se aplica
        // ningún filtro de fecha. StartDate/EndDate siempre vienen poblados
        // desde BuildStartDateTime() aunque el usuario no haya especificado
        // un rango, por lo que el check de SelectedDateRange tiene prioridad.
        if (filters.SelectedDateRange == DateRangeFilter.Todo)
        {
            return (null, null);
        }

        var explicitFrom = filters.StartDate ?? filters.SelectedDateFrom;
        var explicitTo   = filters.EndDate   ?? filters.SelectedDateTo;

        if (explicitFrom.HasValue || explicitTo.HasValue)
        {
            // Usamos DateTime.SpecifyKind(..., Local) de forma explícita antes de
            // ToUniversalTime() para eliminar la ambigüedad de Kind=Unspecified que
            // devuelve DateTime.Date. Sin SpecifyKind, .NET trata Unspecified como
            // Local en ToUniversalTime(), pero la intención queda implícita y
            // puede variar según el entorno (e.g. servidor sin zona configurada).
            if (filters.IsFullDay)
            {
                // Inicio: medianoche del día seleccionado en hora local → UTC
                var fromLocal = explicitFrom.HasValue
                    ? DateTime.SpecifyKind(explicitFrom.Value.Date, DateTimeKind.Local)
                    : (DateTime?)null;

                // Fin: último instante del día seleccionado en hora local → UTC
                // Date.AddDays(1).AddTicks(-1) = 23:59:59.9999999 del día elegido
                var toLocal = explicitTo.HasValue
                    ? DateTime.SpecifyKind(explicitTo.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local)
                    : (DateTime?)null;

                return (fromLocal?.ToUniversalTime(), toLocal?.ToUniversalTime());
            }

            // Rango con hora explícita: el usuario especificó HH:mm
            // Forzamos Kind=Local antes de convertir a UTC.
            var fromExplicit = explicitFrom.HasValue
                ? DateTime.SpecifyKind(explicitFrom.Value, DateTimeKind.Local).ToUniversalTime()
                : (DateTime?)null;

            // Suma :59 segundos al minuto final para que el filtro ≤ cubra el
            // segundo completo (evita perder registros de :mm:01…:mm:59).
            var toExplicit = explicitTo.HasValue
                ? DateTime.SpecifyKind(explicitTo.Value.AddSeconds(59), DateTimeKind.Local).ToUniversalTime()
                : (DateTime?)null;

            return (fromExplicit, toExplicit);
        }

        var now = DateTime.UtcNow;
        return filters.SelectedDateRange switch
        {
            DateRangeFilter.Hoy    => (now.Date, now),
            DateRangeFilter.Semana => (now.Date.AddDays(-7), now),
            DateRangeFilter.Mes    => (now.Date.AddMonths(-1), now),
            _                      => (null, null)
        };
    }
}
