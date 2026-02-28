namespace AuditHistoryExtractorPro.Domain.ValueObjects;

/// <summary>
/// Resultado de una operación de extracción de auditoría.
/// </summary>
public class ExtractionResult
{
    public bool Success { get; set; }
    public int RecordsExtracted { get; set; }
    public int RecordsFailed { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();

    public string GetSummary()
    {
        return $"Extracted {RecordsExtracted} records in {Duration.TotalSeconds:F2} seconds. " +
               $"Failed: {RecordsFailed}. Success: {Success}";
    }
}
