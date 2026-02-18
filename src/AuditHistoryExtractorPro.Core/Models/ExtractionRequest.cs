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
        if (IncludeCreate) operations.Add(OperationType.Create);
        if (IncludeUpdate) operations.Add(OperationType.Update);
        if (IncludeDelete) operations.Add(OperationType.Delete);

        var customFilters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(RecordId))
        {
            customFilters["recordId"] = RecordId.Trim();
        }

        return new ExtractionCriteria
        {
            EntityNames = new List<string> { EntityName.Trim() },
            FromDate = StartDate,
            ToDate = EndDate,
            Operations = operations.Any() ? operations : null,
            PageSize = Math.Min(MaxRecords, 5000),
            CustomFilters = customFilters.Any() ? customFilters : null
        };
    }
}
