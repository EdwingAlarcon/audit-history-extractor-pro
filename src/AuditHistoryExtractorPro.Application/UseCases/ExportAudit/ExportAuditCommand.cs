using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using MediatR;

namespace AuditHistoryExtractorPro.Application.UseCases.ExportAudit;

/// <summary>
/// Comando para exportar registros de auditoría
/// </summary>
public record ExportAuditCommand : IRequest<ExportAuditResponse>
{
    public List<AuditRecord> Records { get; init; } = new();
    public ExportConfiguration Configuration { get; init; } = null!;
}

/// <summary>
/// Respuesta del comando de exportación
/// </summary>
public record ExportAuditResponse
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public long FileSize { get; init; }
    public string? ErrorMessage { get; init; }
    public bool SentToDestination { get; init; }
}

/// <summary>
/// Handler para el comando de exportación
/// </summary>
public class ExportAuditCommandHandler : IRequestHandler<ExportAuditCommand, ExportAuditResponse>
{
    private readonly IExportService _exportService;
    private readonly ILogger<ExportAuditCommandHandler> _logger;

    public ExportAuditCommandHandler(
        IExportService exportService,
        ILogger<ExportAuditCommandHandler> logger)
    {
        _exportService = exportService;
        _logger = logger;
    }

    public async Task<ExportAuditResponse> Handle(
        ExportAuditCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!request.Records.Any())
            {
                _logger.LogWarning("No records to export");
                return new ExportAuditResponse
                {
                    Success = false,
                    ErrorMessage = "No records to export"
                };
            }

            _logger.LogInformation(
                "Starting export of {Count} records to {Format}",
                request.Records.Count,
                request.Configuration.Format);

            // Exportar a archivo
            var filePath = await _exportService.ExportAsync(
                request.Records,
                request.Configuration,
                cancellationToken);

            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;

            _logger.LogInformation(
                "Export completed: {FilePath} ({Size} bytes)",
                filePath,
                fileSize);

            // Enviar a destino adicional si está configurado
            var sentToDestination = false;
            if (request.Configuration.Destination != null)
            {
                sentToDestination = await _exportService.SendToDestinationAsync(
                    filePath,
                    request.Configuration.Destination,
                    cancellationToken);

                if (sentToDestination)
                {
                    _logger.LogInformation(
                        "File sent to destination: {Type}",
                        request.Configuration.Destination.Type);
                }
            }

            return new ExportAuditResponse
            {
                Success = true,
                FilePath = filePath,
                FileSize = fileSize,
                SentToDestination = sentToDestination
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during export");
            return new ExportAuditResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
