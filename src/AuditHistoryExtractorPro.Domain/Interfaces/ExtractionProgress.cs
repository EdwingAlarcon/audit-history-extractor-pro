namespace AuditHistoryExtractorPro.Domain.Interfaces;

/// <summary>
/// Reporta el progreso de una operación de extracción de auditoría
/// </summary>
public class ExtractionProgress
{
    public int TotalRecords { get; set; }
    public int ProcessedRecords { get; set; }
    public int FailedRecords { get; set; }
    public double PercentComplete => TotalRecords > 0 ? (ProcessedRecords * 100.0) / TotalRecords : 0;
    public string CurrentEntity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }
}
