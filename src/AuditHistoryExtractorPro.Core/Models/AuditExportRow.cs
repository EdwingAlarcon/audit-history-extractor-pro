namespace AuditHistoryExtractorPro.Core.Models;

public class AuditExportRow
{
    public string AuditId { get; init; } = string.Empty;
    public string CreatedOn { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public string RecordId { get; init; } = string.Empty;
    public string LogicalName { get; init; } = string.Empty;
    public string RecordUrl { get; init; } = string.Empty;
    public int ActionCode { get; init; }
    public string ActionName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public string ChangedField { get; init; } = string.Empty;
    public string OldValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
}
