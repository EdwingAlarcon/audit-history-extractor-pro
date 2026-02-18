using AuditHistoryExtractorPro.Application.UseCases.ExtractAudit;
using AuditHistoryExtractorPro.Domain.Interfaces;
using MediatR;

namespace AuditHistoryExtractorPro.UI.Services;

public class ExtractPageCoordinator
{
    private readonly ExtractViewService _extractViewService;
    private readonly IMediator _mediator;
    private readonly AuditSessionState _sessionState;

    public ExtractPageCoordinator(
        ExtractViewService extractViewService,
        IMediator mediator,
        AuditSessionState sessionState)
    {
        _extractViewService = extractViewService;
        _mediator = mediator;
        _sessionState = sessionState;
    }

    public async Task<ExtractExecutionResult> ExecuteAsync(
        ExtractInputModel input,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var criteriaResult = _extractViewService.BuildCriteria(input);
        if (!criteriaResult.Success || criteriaResult.Criteria is null)
        {
            return ExtractExecutionResult.Fail(
                criteriaResult.ErrorMessage ?? "❌ No fue posible construir los criterios de extracción.");
        }

        var command = new ExtractAuditCommand
        {
            Criteria = criteriaResult.Criteria,
            Progress = progress
        };

        var response = await _mediator.Send(command, cancellationToken);
        if (!response.Success)
        {
            return ExtractExecutionResult.Fail($"❌ Error: {response.ErrorMessage}");
        }

        _sessionState.SetRecords(response.Records);

        return ExtractExecutionResult.Ok(
            recordsExtracted: response.Records.Count,
            message: "✅ Extracción completada exitosamente!");
    }
}

public class ExtractExecutionResult
{
    public bool Success { get; init; }
    public int RecordsExtracted { get; init; }
    public string Message { get; init; } = string.Empty;

    public static ExtractExecutionResult Ok(int recordsExtracted, string message)
    {
        return new ExtractExecutionResult
        {
            Success = true,
            RecordsExtracted = recordsExtracted,
            Message = message
        };
    }

    public static ExtractExecutionResult Fail(string message)
    {
        return new ExtractExecutionResult
        {
            Success = false,
            Message = message
        };
    }
}
