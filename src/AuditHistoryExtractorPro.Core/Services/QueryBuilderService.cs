using AuditHistoryExtractorPro.Core.Models;
using Microsoft.Xrm.Sdk.Query;
using System.Globalization;
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

        // objectid IN — filtro por IDs resueltos desde una Vista (chunk de max 500).
        // Usamos ConditionExpression.Values.AddRange en lugar de AddCondition(params object[])
        // para evitar el quirk del SDK donde el array entero se serializa como un único valor.
        // StreamAllChunksAsync garantiza que ObjectIds.Count <= 500 en cada llamada.
        if (filters.ObjectIds.Count > 0)
        {
            var idCond = new ConditionExpression("objectid", ConditionOperator.In);
            idCond.Values.AddRange(filters.ObjectIds.Select(id => (object)id));
            query.Criteria.Conditions.Add(idCond);
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

    public string BuildBaseAuditQuery(AuditQueryFilters filters, int pageNumber, int pageSize, string? pagingCookie = null)
    {
        var (fromDate, toDate) = ResolveDateRange(filters);
        var sb = new StringBuilder();
        sb.Append($"<fetch version='1.0' output-format='xml-platform' mapping='logical' no-lock='true' count='{pageSize}' page='{pageNumber}'");
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
        if (!string.IsNullOrWhiteSpace(entityCodeFx))
        {
            sb.Append($"      <condition attribute='objecttypecode' operator='eq' value='{System.Security.SecurityElement.Escape(entityCodeFx)}' />\n");
        }

        // Fechas UTC estrictas ISO 8601: yyyy-MM-ddTHH:mm:ssZ
        // y SIN duplicar nodos createdon dentro del mismo <filter>.
        if (fromDate.HasValue)
        {
            var fromUtc = NormalizeToUtc(fromDate.Value);
            sb.Append($"      <condition attribute='createdon' operator='on-or-after' value='{FormatUtcIso8601(fromUtc)}' />\n");
        }

        if (toDate.HasValue)
        {
            var toUtc = NormalizeToUtc(toDate.Value);
            sb.Append($"      <condition attribute='createdon' operator='on-or-before' value='{FormatUtcIso8601(toUtc)}' />\n");
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

        // objectid IN — lote de IDs resueltos desde una Vista (max. 500/chunk).
        // Ruta de paridad con BuildAuditQuery para que el fallback FetchXml también
        // respete el filtro de IDs cuando StreamAllChunksAsync llama a esta ruta.
        if (filters.ObjectIds.Count > 0)
        {
            sb.Append("      <condition attribute='objectid' operator='in'>\n");
            foreach (var id in filters.ObjectIds)
                sb.Append($"        <value>{id:D}</value>\n");
            sb.Append("      </condition>\n");
        }

        sb.Append("    </filter>\n");
        sb.Append("    <order attribute='createdon' descending='false' />\n");
        sb.Append("  </entity>\n</fetch>");

        return sb.ToString();
    }

    public string BuildFetchXml(AuditQueryFilters filters, int pageNumber, int pageSize, string? pagingCookie = null)
    {
        // Compatibilidad hacia atrás: los consumidores existentes mantienen
        // el mismo punto de entrada, pero ahora usan la versión saneada.
        return BuildBaseAuditQuery(filters, pageNumber, pageSize, pagingCookie);
    }

    private static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
    }

    private static string FormatUtcIso8601(DateTime utc)
    {
        var safeUtc = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
        return safeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static (DateTime? fromDate, DateTime? toDate) ResolveDateRange(AuditQueryFilters filters)
    {
        // ── RAMA A: Personalizado ─────────────────────────────────────────────
        // El usuario eligió un intervalo concreto de fechas mediante los
        // date-pickers (SelectedDateRange se pone en Personalizado automáticamente
        // cuando el usuario modifica SelectedDateFrom/SelectedDateTo).
        // Solo en este modo se respetan StartDate/EndDate/SelectedDateFrom/To;
        // para los presets (Hoy/Semana/Mes) se usa la hora actual (rama B)
        // y para "Todo" no se aplica filtro, lo que evita que el valor por
        // defecto de DateTime.Today sombree el switch de presets.
        if (filters.SelectedDateRange == DateRangeFilter.Personalizado)
        {
            var explicitFrom = filters.StartDate ?? filters.SelectedDateFrom;
            var explicitTo   = filters.EndDate   ?? filters.SelectedDateTo;

            if (!explicitFrom.HasValue && !explicitTo.HasValue)
                return (null, null);

            // Usamos DateTime.SpecifyKind(..., Local) antes de ToUniversalTime()
            // para eliminar la ambigüedad de Kind=Unspecified que devuelve
            // DateTime.Date — sin SpecifyKind la conversión depende del entorno.
            if (filters.IsFullDay)
            {
                // Inicio: medianoche del día seleccionado en hora local → UTC
                var fromLocal = explicitFrom.HasValue
                    ? DateTime.SpecifyKind(explicitFrom.Value.Date, DateTimeKind.Local)
                    : (DateTime?)null;

                // Fin: último instante del día seleccionado en hora local → UTC
                // Date.AddDays(1).AddTicks(-1) = 23:59:59.9999999 del mismo día
                var toLocal = explicitTo.HasValue
                    ? DateTime.SpecifyKind(explicitTo.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local)
                    : (DateTime?)null;

                return (fromLocal?.ToUniversalTime(), toLocal?.ToUniversalTime());
            }

            // Rango con hora explícita: el usuario especificó HH:mm
            var fromExplicit = explicitFrom.HasValue
                ? DateTime.SpecifyKind(explicitFrom.Value, DateTimeKind.Local).ToUniversalTime()
                : (DateTime?)null;

            // Suma :59 segundos al minuto final para cubrir el segundo completo
            // (evita perder registros creados en :mm:01…:mm:59).
            var toExplicit = explicitTo.HasValue
                ? DateTime.SpecifyKind(explicitTo.Value.AddSeconds(59), DateTimeKind.Local).ToUniversalTime()
                : (DateTime?)null;

            return (fromExplicit, toExplicit);
        }

        // ── RAMA B: presets y "Todo" ──────────────────────────────────────────
        // Los presets usan la hora actual; "Todo" no aplica filtro de fecha.
        // Importante: NO se leen StartDate/EndDate para presets — así el valor
        // por defecto DateTime.Today de BuildStartDateTime() no los sombrea.
        var now = DateTime.UtcNow;
        return filters.SelectedDateRange switch
        {
            DateRangeFilter.Hoy    => (now.Date, now),
            DateRangeFilter.Semana => (now.Date.AddDays(-7), now),
            DateRangeFilter.Mes    => (now.Date.AddMonths(-1), now),
            _                      => (null, null)   // Todo y cualquier otro valor
        };
    }
}
