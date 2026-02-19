namespace AuditHistoryExtractorPro.UI.ViewModels;

public class ExtractPageViewModel
{
    public string EntityName { get; set; } = "account";
    public string RecordId { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; } = DateTime.Now.AddMonths(-1);
    public DateTime? EndDate { get; set; } = DateTime.Now;
    public bool IncludeCreate { get; set; } = true;
    public bool IncludeUpdate { get; set; } = true;
    public bool IncludeDelete { get; set; } = true;
    public int MaxRecords { get; set; } = 1000;

    public bool IsExtracting { get; set; }
    public int Progress { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public int RecordsExtracted { get; set; }
    public CancellationTokenSource? ExtractionCts { get; set; }
}
