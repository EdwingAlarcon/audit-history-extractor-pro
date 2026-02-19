using AuditHistoryExtractorPro.Application.UseCases.ExportAudit;
using MediatR;

namespace AuditHistoryExtractorPro.UI.Services;

public class ExportPageCoordinator
{
    private readonly ExportViewService _exportViewService;
    private readonly IMediator _mediator;
    private readonly AuditSessionState _sessionState;

    public ExportPageCoordinator(
        ExportViewService exportViewService,
        IMediator mediator,
        AuditSessionState sessionState)
    {
        _exportViewService = exportViewService;
        _mediator = mediator;
        _sessionState = sessionState;
    }

    public int GetAvailableRecordCount() => _sessionState.Count;

    public async Task<ExportExecutionResult> ExecuteAsync(
        ExportInputModel input,
        CancellationToken cancellationToken = default)
    {
        var records = _sessionState.GetRecordsCopy();
        var totalRecords = records.Count;

        var configResult = _exportViewService.BuildConfiguration(new ExportInputModel
        {
            Format = input.Format,
            FileName = input.FileName,
            OutputPath = input.OutputPath,
            CompressFile = input.CompressFile,
            TotalRecords = totalRecords
        });

        if (!configResult.Success || configResult.Configuration is null)
        {
            return ExportExecutionResult.Fail(
                configResult.ErrorMessage ?? "❌ No fue posible construir la configuración de exportación.",
                totalRecords);
        }

        var command = new ExportAuditCommand
        {
            Records = records,
            Configuration = configResult.Configuration
        };

        var response = await _mediator.Send(command, cancellationToken);
        if (!response.Success)
        {
            return ExportExecutionResult.Fail($"❌ Error: {response.ErrorMessage}", totalRecords);
        }

        return ExportExecutionResult.Ok(
            totalRecords,
            response.FilePath ?? string.Empty,
            response.FileSize,
            "✅ Exportación completada exitosamente!");
    }
}

public class ExportExecutionResult
{
    public bool Success { get; init; }
    public int TotalRecords { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ExportedFilePath { get; init; } = string.Empty;
    public long FileSize { get; init; }

    public static ExportExecutionResult Ok(int totalRecords, string exportedFilePath, long fileSize, string message)
    {
        return new ExportExecutionResult
        {
            Success = true,
            TotalRecords = totalRecords,
            ExportedFilePath = exportedFilePath,
            FileSize = fileSize,
            Message = message
        };
    }

    public static ExportExecutionResult Fail(string message, int totalRecords)
    {
        return new ExportExecutionResult
        {
            Success = false,
            TotalRecords = totalRecords,
            Message = message
        };
    }
}
