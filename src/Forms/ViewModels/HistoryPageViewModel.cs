using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.UI.Services;

namespace AuditHistoryExtractorPro.UI.ViewModels;

public class HistoryPageViewModel
{
    public string EntityFilter { get; set; } = string.Empty;
    public string RecordIdFilter { get; set; } = string.Empty;
    public string UserFilter { get; set; } = string.Empty;
    public string OperationFilter { get; set; } = string.Empty;
    public DateTime? DateFrom { get; set; } = DateTime.Now.AddDays(-30);
    public DateTime? DateTo { get; set; } = DateTime.Now;

    public List<AuditRecord> AllAuditRecords { get; } = new();
    public List<AuditRecord> FilteredRecords { get; set; } = new();
    public List<AuditRecord> PagedRecords { get; set; } = new();

    public bool IsLoading { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }

    public HistoryAuditStats Stats { get; set; } = new();
}
