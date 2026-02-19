using AuditHistoryExtractorPro.Core.Models;
using AuditHistoryExtractorPro.Desktop.Models;

namespace AuditHistoryExtractorPro.Desktop.Services;

public static class AuditMetadataService
{
    public static List<CheckableItem<AuditOperation>> GetAuditOperations()
    {
        return Enum.GetValues<AuditOperation>()
            .Select(value => new CheckableItem<AuditOperation>
            {
                Value = value,
                Label = value.ToString(),
                IsSelected = true
            })
            .ToList();
    }

    public static List<CheckableItem<AuditAction>> GetAuditActions()
    {
        return Enum.GetValues<AuditAction>()
            .Select(value => new CheckableItem<AuditAction>
            {
                Value = value,
                Label = value.ToString(),
                IsSelected = false
            })
            .ToList();
    }
}
