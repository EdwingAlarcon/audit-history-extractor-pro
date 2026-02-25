namespace AuditHistoryExtractorPro.Core.Models;

/// <summary>
/// Modelo de fila de exportación de auditoría.
/// Una instancia = UN CAMPO CAMBIADO de un registro de auditoría.
/// Propiedades marcadas con nota de 'alias' son computed forwarders de
/// propiedades canonicas para garantizar compatibilidad con el esquema de
/// la app original (alduzzen1985).
/// </summary>
public class AuditExportRow
{
    // ── Identificadores ────────────────────────────────────────────
    /// <summary>auditid — GUID del registro en la tabla audit.</summary>
    public string AuditId { get; init; } = string.Empty;

    /// <summary>objectid (GUID del registro auditado).</summary>
    public string RecordId { get; init; } = string.Empty;

    /// <summary>Alias de RecordId — usado como "EntityId" en el esquema original.</summary>
    public string EntityId => RecordId;

    // ── Acción y operación ──────────────────────────────────────
    /// <summary>Código numérico del campo 'action' de Dataverse.</summary>
    public int ActionCode { get; init; }

    /// <summary>Alias de ActionCode — usado como "ActionId" en el esquema original.</summary>
    public int ActionId => ActionCode;

    /// <summary>Nombre legible de la acción (ej. "Update", "Create").</summary>
    public string ActionName { get; init; } = string.Empty;

    /// <summary>Alias de ActionName — columna "Action" del esquema original.</summary>
    public string Action => ActionName;

    /// <summary>Código numérico del campo 'operation' de Dataverse (1=Create,2=Update,3=Delete,4=Access,5=Upsert).</summary>
    public int OperationId { get; init; }

    /// <summary>Nombre legible del campo 'operation' (ej. "Update").</summary>
    public string Operation { get; init; } = string.Empty;

    // ── Entidad y registro ─────────────────────────────────────────
    /// <summary>Nombre lógico de la entidad auditada (ej. "contact").</summary>
    public string EntityName { get; init; } = string.Empty;

    /// <summary>Alias de EntityName — usado como LogicalName en algunas vistas.</summary>
    public string LogicalName { get; init; } = string.Empty;

    /// <summary>Valor primary-name del registro auditado (ej. el fullname del contacto).</summary>
    public string RecordKeyValue { get; init; } = string.Empty;

    /// <summary>URL directa al registro en el entorno de Dataverse.</summary>
    public string RecordUrl { get; init; } = string.Empty;

    // ── Fecha ──────────────────────────────────────────────────
    /// <summary>Fecha y hora del evento de auditoría (hora local, yyyy-MM-dd HH:mm:ss).</summary>
    public string CreatedOn { get; init; } = string.Empty;

    // ── Usuario ──────────────────────────────────────────────────
    /// <summary>GUID del usuario que realizó la acción.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Nombre completo del usuario (fullname).</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Alias de UserName — columna "Username" del esquema original.</summary>
    public string Username => UserName;

    /// <summary>Actor real cuando se usó impersonación.</summary>
    public string RealActor { get; init; } = string.Empty;

    // ── Campo cambiado y valores ─────────────────────────────────
    /// <summary>Nombre lógico del atributo modificado.</summary>
    public string ChangedField { get; init; } = string.Empty;

    /// <summary>Alias de ChangedField — columna "AttributeName" del esquema original.</summary>
    public string AttributeName => ChangedField;

    /// <summary>Valor anterior — traducido a string legible (incluye label de OptionSet, Name de Lookup).</summary>
    public string OldValue { get; init; } = string.Empty;

    /// <summary>Valor nuevo — traducido a string legible.</summary>
    public string NewValue { get; init; } = string.Empty;

    /// <summary>Name del EntityReference anterior (sólo para campos de tipo Lookup; vacío en otro caso).</summary>
    public string LookupOldValue { get; init; } = string.Empty;

    /// <summary>Name del EntityReference nuevo (sólo para campos de tipo Lookup; vacío en otro caso).</summary>
    public string LookupNewValue { get; init; } = string.Empty;

    // ── Trazabilidad ─────────────────────────────────────────────
    /// <summary>ID de transacción — agrupa cambios atómicos en una misma operación.</summary>
    public string TransactionId { get; init; } = string.Empty;
}
