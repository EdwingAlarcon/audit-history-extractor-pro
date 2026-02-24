using AuditHistoryExtractorPro.Core.Models;

namespace AuditHistoryExtractorPro.Core.Services;

public interface IAuditService
{
    bool IsConnected { get; }
    string OrganizationName { get; }

    Task ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupItem>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task WarmupEntityMetadataAsync(string entityLogicalName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna hasta <paramref name="maxRows"/> filas de auditoría para previsualización en la UI.
    /// Nunca escribe archivos; la carga es acotada para evitar Out-Of-Memory.
    /// </summary>
    Task<IReadOnlyList<AuditExportRow>> GetPreviewRowsAsync(
        ExtractionRequest request,
        int maxRows = 50,
        CancellationToken cancellationToken = default);

    Task<ExtractionResult> ExtractAuditHistoryAsync(
        ExtractionRequest request,
        string outputFilePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
