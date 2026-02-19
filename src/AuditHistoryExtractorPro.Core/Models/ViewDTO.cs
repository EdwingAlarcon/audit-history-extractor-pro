namespace AuditHistoryExtractorPro.Core.Models;

public sealed class ViewDTO
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string FetchXml { get; init; } = string.Empty;

    public override string ToString() => Name;
}
