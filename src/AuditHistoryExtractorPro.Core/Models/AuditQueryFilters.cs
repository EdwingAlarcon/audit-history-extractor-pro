namespace AuditHistoryExtractorPro.Core.Models;

public class AuditQueryFilters
{
    public string EntityName { get; init; } = "account";
    public DateRangeFilter SelectedDateRange { get; init; } = DateRangeFilter.Todo;
    public DateTime? SelectedDateFrom { get; init; }
    public DateTime? SelectedDateTo { get; init; }
    public bool IsFullDay { get; init; } = true;
    public LookupItem? SelectedUser { get; init; }
    public OperationFilter? SelectedOperation { get; init; }
    public IReadOnlyList<int> SelectedOperations { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> SelectedActions { get; init; } = Array.Empty<int>();
    public string RecordId { get; init; } = string.Empty;
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}
