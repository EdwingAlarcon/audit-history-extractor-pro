namespace AuditHistoryExtractorPro.Models;

/// <summary>
/// Representa un registro de auditoría extraído de Dataverse
/// </summary>
public class AuditRecord
{
    public Guid AuditId { get; set; }
    public DateTime CreatedOn { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public Guid RecordId { get; set; }
    public string LogicalName { get; set; } = string.Empty;
    public string RecordUrl { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public Dictionary<string, AuditFieldChange> Changes { get; set; } = new();
    public string? TransactionId { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();
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
