namespace AuditHistoryExtractorPro.Domain.ValueObjects;

/// <summary>
/// Representa los criterios de extracción de auditoría.
/// La mayoría de propiedades son <c>init</c>: se asignan durante la construcción
/// y no pueden mutar después.
/// Excepción: <see cref="FromDate"/> y <see cref="ToDate"/> mantienen <c>set</c>
/// porque el handler de modo incremental los ajusta en tiempo de ejecución.
/// </summary>
public class ExtractionCriteria
{
    public List<string> EntityNames { get; init; } = new();
    public List<string>? FieldNames { get; init; }
    public DateTime? FromDate { get; set; }   // mutable: ajustado en modo incremental
    public DateTime? ToDate { get; set; }     // mutable: ajustado en modo incremental
    public List<string>? UserIds { get; init; }
    public List<OperationType>? Operations { get; init; }
    public bool IncrementalMode { get; init; }
    public int PageSize { get; init; } = 5000;
    public int MaxParallelRequests { get; init; } = 10;
    public Dictionary<string, string>? CustomFilters { get; init; }

    public void Validate()
    {
        if (!EntityNames.Any())
            throw new ArgumentException("At least one entity name must be specified");

        if (FromDate.HasValue && ToDate.HasValue && FromDate > ToDate)
            throw new ArgumentException("FromDate cannot be greater than ToDate");

        if (PageSize <= 0 || PageSize > 10000)
            throw new ArgumentException("PageSize must be between 1 and 10000");
    }
}

/// <summary>
/// Tipos de operación de auditoría
/// </summary>
public enum OperationType
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Associate = 4,
    Disassociate = 5,
    Archive = 27,
    Restore = 28
}
