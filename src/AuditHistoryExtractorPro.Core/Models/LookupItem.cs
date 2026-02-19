namespace AuditHistoryExtractorPro.Core.Models;

public class LookupItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}
