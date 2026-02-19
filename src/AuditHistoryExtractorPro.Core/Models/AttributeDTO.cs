namespace AuditHistoryExtractorPro.Core.Models;

public sealed class AttributeDTO
{
    public string LogicalName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int? ColumnNumber { get; init; }

    public override string ToString() => DisplayName;
}
