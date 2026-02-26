namespace AuditHistoryExtractorPro.Core.Models;

public class AuditQueryFilters
{
    public string EntityName { get; init; } = "account";
    public DateRangeFilter SelectedDateRange { get; init; } = DateRangeFilter.Todo;
    public DateTime? SelectedDateFrom { get; init; }
    public DateTime? SelectedDateTo { get; init; }
    public bool IsFullDay { get; init; } = true;
    public LookupItem? SelectedUser { get; init; }
    public OperationFilter? SelectedOperation { get; init; }
    public IReadOnlyList<int> SelectedOperations { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> SelectedActions { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> SelectedAttributes { get; init; } = Array.Empty<string>();
    public string SearchValue { get; init; } = string.Empty;
    public string RecordId { get; init; } = string.Empty;
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    /// <summary>IDs de registros de la entidad destino resueltos desde una Vista (objectid IN).  
    /// Vacío = sin restricción por IDs.</summary>
    public IReadOnlyList<Guid> ObjectIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Nombre lógico de la entidad al que apuntan los <see cref="ObjectIds"/>.
    /// Se usa como atributo uitype='' en la condición objectid IN del FetchXML.
    /// Dataverse requiere uitype para resolver correctamente lookups polimórficos.
    /// Vacío = no se agrega uitype (puede causar resultados incompletos).</summary>
    public string ObjectIdsEntityType { get; init; } = string.Empty;

    /// <summary>Código de tipo de entidad (ObjectTypeCode) resuelto desde Dataverse.
    /// Cuando se proporciona, se usa directamente como entero en la condición
    /// 'objecttypecode' del FetchXML. Null = fallback al nombre lógico (menos fiable).</summary>
    public int? EntityTypeCode { get; init; }
}
