namespace AuditHistoryExtractorPro.Core.Models;

/// <summary>
/// Mapeo completo de los códigos de acción de la tabla 'audit' de Dataverse.
/// Basado en la documentación oficial de Dynamics 365 / Power Platform.
/// GetAuditActionName en AuditService cubre códigos no mapeados aquí.
/// </summary>
public enum AuditAction
{
    // ── CRUD principal ────────────────────────────────────────────────
    Create         = 1,
    Update         = 2,
    Delete         = 3,

    // ── Ciclo de vida / estado ───────────────────────────────────────
    Activate       = 4,
    Deactivate     = 5,
    Cancel         = 6,
    Complete       = 7,
    Close          = 8,
    Fulfill        = 9,
    StatusChange   = 10,   // Estado del sistema cambiado
    CancelOrder    = 11,
    Resolve        = 12,

    // ── Asignación y acceso ───────────────────────────────────────
    Assign         = 13,
    Share          = 14,
    Unshare        = 15,
    Merge          = 16,
    GrantAccess    = 17,
    ModifyAccess   = 18,
    RevokeAccess   = 19,

    // ── Oportunidades / leads ──────────────────────────────────
    QualifyLead    = 20,
    Disqualify     = 21,
    WinOpportunity = 22,
    LoseOpportunity = 23,
    QualifyOpportunity = 24,

    // ── Operaciones del sistema / privacidad ───────────────────
    Upsert         = 25,
    Cascade        = 26,
    Archive        = 27,
    Restore        = 28,
    PrivacyAction  = 29,

    // ── Cambios de campo del sistema ────────────────────────────
    SystemAttributeChange = 65,
}
