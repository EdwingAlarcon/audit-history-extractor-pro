namespace AuditHistoryExtractorPro.Core.Models;

public class ExtractionResult
{
    public bool Success { get; init; }
    public int RecordsExtracted { get; init; }
    public string OutputFilePath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public static ExtractionResult Ok(int records, string filePath, string message)
        => new()
        {
            Success = true,
            RecordsExtracted = records,
            OutputFilePath = filePath,
            Message = message
        };

    public static ExtractionResult Fail(string message)
        => new()
        {
            Success = false,
            Message = message
        };
}
