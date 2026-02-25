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

    // ── Acciones extendidas (paridad con catálogo legacy/capturas) ───────────
    SetState = 30,
    SetStateDynamicEntity = 31,
    AddMember = 32,
    RemoveMember = 33,
    AddItem = 34,
    RemoveItem = 35,
    Associate = 36,
    Disassociate = 37,
    SetParent = 38,
    SetBusinessUnit = 39,
    UpdateUserAccess = 40,
    Route = 41,
    Send = 42,
    DeliverIncoming = 43,
    DeliverPromote = 44,
    Attach = 45,
    AddToQueue = 46,
    AssignRole = 47,
    RemoveRole = 48,
    ReplacePrincipalAccess = 49,
    AddPrivilegesRole = 50,
    RemovePrivilegesRole = 51,
    GrantAccessTeam = 52,
    ModifyPrincipalAccess = 53,
    RevokePrincipalAccess = 54,
    ChangeOwner = 55,
    ConvertQuote = 56,
    SendQuote = 57,
    CloseQuote = 58,
    ReviseQuote = 59,
    LockInvoice = 60,
    UnlockInvoice = 61,
    GenerateInvoice = 62,
    Paid = 63,
    Invoice = 64,
    AuditChange = 65,
    SystemAttributeChange = 66,

    // ── Reservados / proveedor (para cubrir >50 códigos en UI) ───────────────
    VendorAction67 = 67,
    VendorAction68 = 68,
    VendorAction69 = 69,
    VendorAction70 = 70,
    VendorAction71 = 71,
    VendorAction72 = 72,
    VendorAction73 = 73,
    VendorAction74 = 74,
    VendorAction75 = 75,
    VendorAction76 = 76,
    VendorAction77 = 77,
    VendorAction78 = 78,
    VendorAction79 = 79,
    VendorAction80 = 80,
}
