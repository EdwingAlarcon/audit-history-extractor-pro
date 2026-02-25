namespace AuditHistoryExtractorPro.Core.Models;

/// <summary>
/// Códigos del campo 'operation' de la tabla 'audit' de Dataverse.
/// Representa EL TIPO de operación (a diferencia de 'action' que es el quidé).
/// </summary>
public enum AuditOperation
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Access = 4,
    Upsert = 5
}
