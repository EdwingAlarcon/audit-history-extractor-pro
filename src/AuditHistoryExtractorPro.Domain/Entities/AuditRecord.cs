namespace AuditHistoryExtractorPro.Domain.Entities;

/// <summary>
/// Representa un registro de auditoría extraído de Dataverse.
/// Encapsula comportamiento de dominio relacionado al tipo de operación
/// y a los cambios de campos, siguiendo el principio de Tell-Don't-Ask.
/// </summary>
public class AuditRecord
{
    public Guid AuditId { get; set; }
    public DateTime CreatedOn { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public Guid RecordId { get; set; }
    public string LogicalName { get; set; } = string.Empty;
    public string RecordUrl { get; set; } = string.Empty;
    /// <summary>Código numérico del campo 'operation' (DML) de Dataverse.</summary>
    public int OperationCode { get; set; }
    public string Operation { get; set; } = string.Empty;
    /// <summary>Código numérico del campo 'action' de Dataverse.</summary>
    public int ActionCode { get; set; }
    /// <summary>Etiqueta legible del campo 'action'.</summary>
    public string Action { get; set; } = string.Empty;
    public string OperationName => Operation;
    public string ActionName => Action;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string RealActor { get; set; } = string.Empty;
    public Dictionary<string, AuditFieldChange> Changes { get; set; } = new();
    public string? TransactionId { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();

    // ── Comportamiento de dominio ────────────────────────────────────────────

    /// <summary>Devuelve true si la operación registrada es una creación.</summary>
    public bool IsCreateOperation =>
        string.Equals(Operation, "Create", StringComparison.OrdinalIgnoreCase);

    /// <summary>Devuelve true si la operación registrada es una actualización.</summary>
    public bool IsUpdateOperation =>
        string.Equals(Operation, "Update", StringComparison.OrdinalIgnoreCase);

    /// <summary>Devuelve true si la operación registrada es una eliminación.</summary>
    public bool IsDeleteOperation =>
        string.Equals(Operation, "Delete", StringComparison.OrdinalIgnoreCase);

    /// <summary>Número de campos que realmente cambiaron de valor.</summary>
    public int ChangedFieldCount => Changes.Values.Count(c => c.HasChanged);

    /// <summary>Indica si el campo especificado tiene un cambio registrado.</summary>
    public bool HasFieldChange(string fieldName) => Changes.ContainsKey(fieldName);

    /// <summary>
    /// Agrega o reemplaza el cambio de un campo de forma explícita.
    /// Método de intención clara frente al acceso directo al diccionario.
    /// </summary>
    public void AddFieldChange(AuditFieldChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (string.IsNullOrWhiteSpace(change.FieldName))
            throw new ArgumentException("FieldName cannot be empty.", nameof(change));

        Changes[change.FieldName] = change;
    }
}

/// <summary>
/// Representa el cambio de un campo específico en un registro de auditoría
/// </summary>
public class AuditFieldChange
{
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string FieldType { get; set; } = string.Empty;
    
    public bool HasChanged => OldValue != NewValue;
    
    public string GetChangeDescription()
    {
        if (OldValue == null && NewValue != null)
            return $"Set to '{NewValue}'";
        if (OldValue != null && NewValue == null)
            return $"Cleared from '{OldValue}'";
        return $"Changed from '{OldValue}' to '{NewValue}'";
    }
}

/// <summary>
/// Representa el resultado de una comparación entre dos versiones de un registro
/// </summary>
public class RecordComparison
{
    public Guid RecordId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public AuditRecord? PreviousVersion { get; set; }
    public AuditRecord CurrentVersion { get; set; } = null!;
    public List<FieldDifference> Differences { get; set; } = new();
    public DateTime ComparisonDate { get; set; }
}

/// <summary>
/// Representa una diferencia entre dos valores de un campo
/// </summary>
public class FieldDifference
{
    public string FieldName { get; set; } = string.Empty;
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
    public DifferenceType Type { get; set; }
    public string Description { get; set; } = string.Empty;
}

public enum DifferenceType
{
    Added,
    Modified,
    Removed,
    Unchanged
}

/// <summary>
/// Representa estadísticas de auditoría
/// </summary>
public class AuditStatistics
{
    public int TotalRecords { get; set; }
    public int CreateOperations { get; set; }
    public int UpdateOperations { get; set; }
    public int DeleteOperations { get; set; }
    public Dictionary<string, int> RecordsByEntity { get; set; } = new();
    public Dictionary<string, int> RecordsByUser { get; set; } = new();
    public DateTime? FirstAuditDate { get; set; }
    public DateTime? LastAuditDate { get; set; }
    public List<string> MostChangedFields { get; set; } = new();
}
