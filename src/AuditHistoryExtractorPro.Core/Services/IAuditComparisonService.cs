using AuditHistoryExtractorPro.Core.Models;

namespace AuditHistoryExtractorPro.Core.Services;

public interface IAuditComparisonService
{
    Task<AuditComparisonResult> CompareWithLegacyAsync(
        string legacyExcelPath,
        IReadOnlyList<AuditExportRow> currentRows,
        CancellationToken cancellationToken = default);
}
