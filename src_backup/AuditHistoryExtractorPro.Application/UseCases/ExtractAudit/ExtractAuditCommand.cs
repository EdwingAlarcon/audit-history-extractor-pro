using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.Interfaces;
using AuditHistoryExtractorPro.Domain.ValueObjects;
using MediatR;

namespace AuditHistoryExtractorPro.Application.UseCases.ExtractAudit;

/// <summary>
/// Comando para extraer registros de auditoría
/// </summary>
public record ExtractAuditCommand : IRequest<ExtractAuditResponse>
{
    public ExtractionCriteria Criteria { get; init; } = null!;
    public IProgress<ExtractionProgress>? Progress { get; init; }
}

/// <summary>
/// Respuesta del comando de extracción
/// </summary>
public record ExtractAuditResponse
{
    public bool Success { get; init; }
    public List<AuditRecord> Records { get; init; } = new();
    public ExtractionResult Result { get; init; } = null!;
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Handler para el comando de extracción de auditoría
/// </summary>
public class ExtractAuditCommandHandler : IRequestHandler<ExtractAuditCommand, ExtractAuditResponse>
{
    private readonly IAuditRepository _auditRepository;
    private readonly IAuditProcessor _auditProcessor;
    private readonly ICacheService _cacheService;
    private readonly ILogger<ExtractAuditCommandHandler> _logger;

    public ExtractAuditCommandHandler(
        IAuditRepository auditRepository,
        IAuditProcessor auditProcessor,
        ICacheService cacheService,
        ILogger<ExtractAuditCommandHandler> logger)
    {
        _auditRepository = auditRepository;
        _auditProcessor = auditProcessor;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<ExtractAuditResponse> Handle(
        ExtractAuditCommand request,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Validar criterios
            request.Criteria.Validate();
            
            _logger.LogInformation(
                "Starting audit extraction for entities: {Entities}",
                string.Join(", ", request.Criteria.EntityNames));

            // Si es modo incremental, obtener última fecha de extracción
            if (request.Criteria.IncrementalMode)
            {
                await ApplyIncrementalModeAsync(request.Criteria, cancellationToken);
            }

            // Extraer registros de auditoría
            var records = await _auditRepository.ExtractAuditRecordsAsync(
                request.Criteria,
                request.Progress,
                cancellationToken);

            _logger.LogInformation("Extracted {Count} audit records", records.Count);

            // Normalizar y enriquecer registros
            var normalizedRecords = await _auditProcessor.NormalizeRecordsAsync(
                records,
                cancellationToken);

            var enrichedRecords = await _auditProcessor.EnrichRecordsAsync(
                normalizedRecords,
                cancellationToken);

            // Guardar última fecha de extracción si es modo incremental
            if (request.Criteria.IncrementalMode)
            {
                await SaveLastExtractionDateAsync(request.Criteria, cancellationToken);
            }

            var result = new ExtractionResult
            {
                Success = true,
                RecordsExtracted = enrichedRecords.Count,
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };

            _logger.LogInformation(
                "Audit extraction completed successfully. {Summary}",
                result.GetSummary());

            return new ExtractAuditResponse
            {
                Success = true,
                Records = enrichedRecords,
                Result = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during audit extraction");

            var result = new ExtractionResult
            {
                Success = false,
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };
            result.Errors.Add(ex.Message);

            return new ExtractAuditResponse
            {
                Success = false,
                Records = new List<AuditRecord>(),
                Result = result,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task ApplyIncrementalModeAsync(
        ExtractionCriteria criteria,
        CancellationToken cancellationToken)
    {
        foreach (var entityName in criteria.EntityNames)
        {
            var lastExtractionDate = await _auditRepository.GetLastExtractionDateAsync(
                entityName,
                cancellationToken);

            if (lastExtractionDate.HasValue)
            {
                criteria.FromDate = lastExtractionDate.Value;
                _logger.LogInformation(
                    "Incremental mode: extracting {Entity} from {Date}",
                    entityName,
                    lastExtractionDate.Value);
            }
        }
    }

    private async Task SaveLastExtractionDateAsync(
        ExtractionCriteria criteria,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        foreach (var entityName in criteria.EntityNames)
        {
            await _auditRepository.SaveLastExtractionDateAsync(
                entityName,
                now,
                cancellationToken);
        }
    }
}
