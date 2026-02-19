namespace AuditHistoryExtractorPro.UI.ViewModels;

public class ExportPageViewModel
{
    public string Format { get; set; } = "Excel";
    public string FileName { get; set; } = "AuditHistory_Export";
    public string OutputPath { get; set; } = string.Empty;
    public bool IncludeHeaders { get; set; } = true;
    public bool IncludeMetadata { get; set; } = true;
    public bool CompressFile { get; set; }

    public bool IsExporting { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public string ExportedFilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int TotalRecords { get; set; }
    public int ExportCount { get; set; }

    public bool CanExport => !IsExporting && TotalRecords > 0 && !string.IsNullOrWhiteSpace(FileName);
}
