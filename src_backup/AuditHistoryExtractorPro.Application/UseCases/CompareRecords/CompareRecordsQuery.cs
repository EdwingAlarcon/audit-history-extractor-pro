using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.Interfaces;
using MediatR;

namespace AuditHistoryExtractorPro.Application.UseCases.CompareRecords;

/// <summary>
/// Query para comparar versiones de un registro
/// </summary>
public record CompareRecordsQuery : IRequest<CompareRecordsResponse>
{
    public string EntityName { get; init; } = string.Empty;
    public Guid RecordId { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
}

/// <summary>
/// Respuesta de la comparación
/// </summary>
public record CompareRecordsResponse
{
    public bool Success { get; init; }
    public List<RecordComparison> Comparisons { get; init; } = new();
    public AuditStatistics? Statistics { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Handler para la query de comparación
/// </summary>
public class CompareRecordsQueryHandler : IRequestHandler<CompareRecordsQuery, CompareRecordsResponse>
{
    private readonly IAuditRepository _auditRepository;
    private readonly IAuditProcessor _auditProcessor;
    private readonly ILogger<CompareRecordsQueryHandler> _logger;

    public CompareRecordsQueryHandler(
        IAuditRepository auditRepository,
        IAuditProcessor auditProcessor,
        ILogger<CompareRecordsQueryHandler> logger)
    {
        _auditRepository = auditRepository;
        _auditProcessor = auditProcessor;
        _logger = logger;
    }

    public async Task<CompareRecordsResponse> Handle(
        CompareRecordsQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Comparing versions for record {RecordId} in entity {Entity}",
                request.RecordId,
                request.EntityName);

            // Obtener historial del registro
            var history = await _auditRepository.GetRecordHistoryAsync(
                request.EntityName,
                request.RecordId,
                request.FromDate,
                request.ToDate,
                cancellationToken);

            if (!history.Any())
            {
                _logger.LogWarning("No audit history found for record {RecordId}", request.RecordId);
                return new CompareRecordsResponse
                {
                    Success = true,
                    Comparisons = new List<RecordComparison>()
                };
            }

            _logger.LogInformation("Found {Count} audit records for comparison", history.Count);

            // Comparar versiones consecutivas
            var comparisons = new List<RecordComparison>();
            for (int i = 1; i < history.Count; i++)
            {
                var comparison = await _auditProcessor.CompareRecordVersionsAsync(
                    history[i - 1],
                    history[i],
                    cancellationToken);

                comparisons.Add(comparison);
            }

            // Generar estadísticas
            var statistics = new AuditStatistics
            {
                TotalRecords = history.Count,
                FirstAuditDate = history.Min(h => h.CreatedOn),
                LastAuditDate = history.Max(h => h.CreatedOn)
            };

            _logger.LogInformation(
                "Comparison completed: {Count} comparisons generated",
                comparisons.Count);

            return new CompareRecordsResponse
            {
                Success = true,
                Comparisons = comparisons,
                Statistics = statistics
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during record comparison");
            return new CompareRecordsResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
