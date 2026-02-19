using AuditHistoryExtractorPro.Core.Models;

namespace AuditHistoryExtractorPro.Core.Services;

public interface IExcelExportService
{
    Task ExportAsync(
        string outputFilePath,
        IAsyncEnumerable<AuditExportRow> rows,
        CancellationToken cancellationToken = default);
}
