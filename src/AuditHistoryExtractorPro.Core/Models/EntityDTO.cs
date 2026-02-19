namespace AuditHistoryExtractorPro.Core.Models;

public sealed class EntityDTO
{
    public string LogicalName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int? ObjectTypeCode { get; init; }

    public override string ToString() => DisplayName;
}
