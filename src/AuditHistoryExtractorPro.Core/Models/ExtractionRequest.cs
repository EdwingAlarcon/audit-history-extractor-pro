using AuditHistoryExtractorPro.Domain.ValueObjects;

namespace AuditHistoryExtractorPro.Core.Models;

public class ExtractionRequest
{
    public string EntityName { get; init; } = "account";
    public string RecordId { get; init; } = string.Empty;
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public bool IncludeCreate { get; init; } = true;
    public bool IncludeUpdate { get; init; } = true;
    public bool IncludeDelete { get; init; } = true;
    public int MaxRecords { get; init; } = 10000;

    public DateRangeFilter SelectedDateRange { get; init; } = DateRangeFilter.Todo;
    public LookupItem? SelectedUser { get; init; }
    public OperationFilter? SelectedOperation { get; init; }
    public IReadOnlyList<int> SelectedOperations { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> SelectedActions { get; init; } = Array.Empty<int>();

    public ExtractionCriteria ToCriteria()
    {
        if (string.IsNullOrWhiteSpace(EntityName))
        {
            throw new InvalidOperationException("EntityName es obligatorio.");
        }

        if (StartDate.HasValue && EndDate.HasValue && StartDate > EndDate)
        {
            throw new InvalidOperationException("StartDate no puede ser mayor que EndDate.");
        }

        if (MaxRecords < 1)
        {
            throw new InvalidOperationException("MaxRecords debe ser mayor a 0.");
        }

        var operations = new List<OperationType>();
        if (SelectedOperations.Count > 0)
        {
            operations.AddRange(SelectedOperations.Select(value => (OperationType)value));
        }
        else if (SelectedOperation.HasValue)
        {
            operations.Add((OperationType)(int)SelectedOperation.Value);
        }
        else
        {
            if (IncludeCreate) operations.Add(OperationType.Create);
            if (IncludeUpdate) operations.Add(OperationType.Update);
            if (IncludeDelete) operations.Add(OperationType.Delete);
        }

        var customFilters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(RecordId))
        {
            customFilters["recordId"] = RecordId.Trim();
        }

        if (SelectedUser is not null)
        {
            customFilters["userId"] = SelectedUser.Id.ToString();
        }

        var fromDate = StartDate;
        var toDate = EndDate;
        var now = DateTime.UtcNow;
        switch (SelectedDateRange)
        {
            case DateRangeFilter.Hoy:
                fromDate = now.Date;
                toDate = now;
                break;
            case DateRangeFilter.Semana:
                fromDate = now.Date.AddDays(-7);
                toDate = now;
                break;
            case DateRangeFilter.Mes:
                fromDate = now.Date.AddMonths(-1);
                toDate = now;
                break;
            case DateRangeFilter.Todo:
                break;
        }

        return new ExtractionCriteria
        {
            EntityNames = new List<string> { EntityName.Trim() },
            FromDate = fromDate,
            ToDate = toDate,
            Operations = operations.Any() ? operations : null,
            PageSize = Math.Min(MaxRecords, 5000),
            CustomFilters = customFilters.Any() ? customFilters : null
        };
    }
}
