using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Core.Services;
using AuditHistoryExtractorPro.Desktop.Models;

namespace AuditHistoryExtractorPro.Desktop.Services;

public static class AuditMetadataService
{
    public static List<CheckableItem<AuditOperation>> GetAuditOperations()
    {
        // IsSelected = false → comportamiento de BYPASS por defecto:
        // si el usuario no marca ninguna operación, la consulta no filtra
        // por operación y devuelve todos los registros (equivalente a legacy).
        return Enum.GetValues<AuditOperation>()
            .Select(value => new CheckableItem<AuditOperation>
            {
                Value = value,
                Label = AuditService.GetAuditOperationName((int)value),
                IsSelected = false
            })
            .ToList();
    }

    public static List<CheckableItem<AuditAction>> GetAuditActions()
    {
        return Enum.GetValues<AuditAction>()
            .Select(value => new CheckableItem<AuditAction>
            {
                Value = value,
                Label = AuditService.GetAuditActionName((int)value),
                IsSelected = false
            })
            .ToList();
    }
}
