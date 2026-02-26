namespace AuditHistoryExtractorPro.Core.Services;

/// <summary>
/// Diccionario centralizado para traducir los códigos numéricos de los campos
/// <c>action</c> y <c>operation</c> de la tabla <c>audit</c> de Dataverse
/// a etiquetas en español.
///
/// Fuente: documentación oficial de Microsoft Dataverse —
/// https://learn.microsoft.com/en-us/power-apps/developer/data-platform/auditing/reference/entities/audit
///
/// El campo <c>action</c> describe QUÉ acción de negocio ocurrió (Create, Update,
/// Share, Invoice, etc.). El campo <c>operation</c> describe el tipo de operación
/// DML subyacente (1=Crear, 2=Actualizar, 3=Eliminar, 4=Acceso, 5=Upsert).
/// </summary>
public static class AuditMetadataLookup
{
    // ─────────────────────────────────────────────────────────────────────────
    // ACCIÓN (campo 'action') — Picklist de Dataverse
    // Describe la acción de negocio que generó el evento de auditoría.
    // Ref: Audit.Action OptionSetType en la documentación del SDK
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly IReadOnlyDictionary<int, string> ActionLabels =
        new Dictionary<int, string>
        {
            [0]   = "Desconocido",
            [1]   = "Crear",
            [2]   = "Actualizar",
            [3]   = "Eliminar",
            [4]   = "Activar",
            [5]   = "Desactivar",
            [6]   = "Upsert",
            [11]  = "Operación en Cascada",
            [12]  = "Combinar",
            [13]  = "Asignar",
            [14]  = "Compartir",
            [15]  = "Dejar de Compartir",
            [16]  = "Cerrar",
            [17]  = "Cancelar",
            [18]  = "Completar",
            [20]  = "Resolver",
            [21]  = "Reabrir",
            [22]  = "Cumplir",
            [23]  = "Pagar",
            [24]  = "Calificar",
            [25]  = "Descalificar",
            [26]  = "Enviar",
            [27]  = "Rechazar",
            [28]  = "Aprobar",
            [29]  = "Facturar",
            [30]  = "Establecer Estado",
            [31]  = "Agregar Miembro",
            [32]  = "Quitar Miembro",
            [33]  = "Asociar Entidades",
            [34]  = "Desasociar Entidades",
            [35]  = "Agregar Miembros",
            [36]  = "Quitar Miembros",
            [37]  = "Agregar Elemento",
            [38]  = "Quitar Elemento",
            [39]  = "Agregar Sustituto",
            [40]  = "Quitar Sustituto",
            [41]  = "Establecer Estado (Secundario)",
            [42]  = "Renovar",
            [43]  = "Revisar",
            [44]  = "Ganar",
            [45]  = "Perder",
            [46]  = "Procesamiento Interno",
            [47]  = "Reprogramar",
            [48]  = "Modificar Uso Compartido",
            [49]  = "Dejar de Compartir (Heredado)",
            [50]  = "Reservar",
            [51]  = "Generar Cotización desde Oportunidad",
            [52]  = "Agregar a Cola",
            [53]  = "Asignar Rol a Equipo",
            [54]  = "Quitar Rol de Equipo",
            [55]  = "Asignar Rol a Usuario",
            [56]  = "Quitar Rol de Usuario",
            [57]  = "Agregar Privilegios a Rol",
            [58]  = "Quitar Privilegios de Rol",
            [59]  = "Reemplazar Privilegios en Rol",
            [60]  = "Importar Asignaciones",
            [61]  = "Clonar",
            [62]  = "Enviar Email Directo",
            [63]  = "Organización Habilitada",
            [64]  = "Acceso de Usuario vía Web",
            [65]  = "Acceso de Usuario vía Servicios Web",
            [100] = "Eliminar Entidad",
            [101] = "Eliminar Atributo",
            [102] = "Cambio de Auditoría a Nivel de Entidad",
            [103] = "Cambio de Auditoría a Nivel de Atributo",
            [104] = "Cambio de Auditoría a Nivel de Organización",
            [105] = "Auditoría de Entidad Iniciada",
            [106] = "Auditoría de Atributo Iniciada",
            [107] = "Auditoría Habilitada",
            [108] = "Auditoría de Entidad Detenida",
            [109] = "Auditoría de Atributo Detenida",
            [110] = "Auditoría Deshabilitada",
            [111] = "Eliminación de Registro de Auditoría",
            [112] = "Auditoría de Acceso de Usuario Iniciada",
            [113] = "Auditoría de Acceso de Usuario Detenida",
            [115] = "Archivar",
            [116] = "Retener",
            [117] = "Revertir Retención",
            [118] = "Acceso Denegado por Firewall IP",
            [119] = "Acceso Permitido por Firewall IP",
            [120] = "Restaurar",
        };

    // ─────────────────────────────────────────────────────────────────────────
    // OPERACIÓN (campo 'operation') — Picklist DML de Dataverse
    // Describe el tipo de operación de base de datos que originó el evento.
    // Ref: Audit.Operation OptionSetType
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly IReadOnlyDictionary<int, string> OperationLabels =
        new Dictionary<int, string>
        {
            [1]   = "Crear",
            [2]   = "Actualizar",
            [3]   = "Eliminar",
            [4]   = "Acceso",
            [5]   = "Upsert",
            [115] = "Archivar",
            [116] = "Retener",
            [117] = "Revertir Retención",
            [118] = "Restaurar",
            [200] = "Operación Personalizada",
        };

    // ─────────────────────────────────────────────────────────────────────────
    // API pública
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna la etiqueta en español del campo <c>action</c> de la tabla audit.
    /// Si el código no está en el diccionario oficial, retorna
    /// <c>"Acción {code}"</c> para que el valor numérico siga siendo visible.
    /// </summary>
    public static string GetActionLabel(int code) =>
        ActionLabels.TryGetValue(code, out var label)
            ? label
            : $"Acción {code}";

    /// <summary>
    /// Retorna la etiqueta en español del campo <c>operation</c> de la tabla audit.
    /// Si el código no está en el diccionario oficial, retorna
    /// <c>"Operación {code}"</c> para que el valor numérico siga siendo visible.
    /// </summary>
    public static string GetOperationLabel(int code) =>
        OperationLabels.TryGetValue(code, out var label)
            ? label
            : $"Operación {code}";
}
