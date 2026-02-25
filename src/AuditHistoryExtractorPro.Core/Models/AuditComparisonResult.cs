namespace AuditHistoryExtractorPro.Core.Models;

public sealed class AuditComparisonResult
{
    public int LegacyTotal { get; init; }
    public int CurrentTotal { get; init; }
    public int MissingInNewCount { get; init; }
    public int ValueDifferenceCount { get; init; }
    public IReadOnlyList<AuditComparisonDiscrepancy> Discrepancies { get; init; } = Array.Empty<AuditComparisonDiscrepancy>();
    public IReadOnlyList<EntityCountDifference> EntityCountDifferences { get; init; } = Array.Empty<EntityCountDifference>();
}

public sealed class AuditComparisonDiscrepancy
{
    public string Type { get; init; } = string.Empty;
    public string AuditId { get; init; } = string.Empty;
    public string ObjectId { get; init; } = string.Empty;
    public string AttributeName { get; init; } = string.Empty;
    public string LegacyOldValue { get; init; } = string.Empty;
    public string LegacyNewValue { get; init; } = string.Empty;
    public string CurrentOldValue { get; init; } = string.Empty;
    public string CurrentNewValue { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
}

public sealed class EntityCountDifference
{
    public string EntityName { get; init; } = string.Empty;
    public int LegacyCount { get; init; }
    public int CurrentCount { get; init; }
    public int Difference => CurrentCount - LegacyCount;
}
