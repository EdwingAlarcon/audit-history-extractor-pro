using AuditHistoryExtractorPro.Domain.ValueObjects;

namespace AuditHistoryExtractorPro.UI.Services;

public class ExtractViewService
{
    public ExtractCriteriaBuildResult BuildCriteria(ExtractInputModel input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.EntityName))
        {
            return ExtractCriteriaBuildResult.Fail("⚠️ Por favor ingrese el nombre de la entidad.");
        }

        if (input.StartDate.HasValue && input.EndDate.HasValue && input.StartDate > input.EndDate)
        {
            return ExtractCriteriaBuildResult.Fail("⚠️ La Fecha Inicio no puede ser mayor que la Fecha Fin.");
        }

        if (!string.IsNullOrWhiteSpace(input.RecordId) && !Guid.TryParse(input.RecordId, out _))
        {
            return ExtractCriteriaBuildResult.Fail("⚠️ El ID del Registro debe ser un GUID válido.");
        }

        if (input.MaxRecords < 1 || input.MaxRecords > 10000)
        {
            return ExtractCriteriaBuildResult.Fail("⚠️ El límite de registros debe estar entre 1 y 10000.");
        }

        var operations = new List<OperationType>();
        if (input.IncludeCreate) operations.Add(OperationType.Create);
        if (input.IncludeUpdate) operations.Add(OperationType.Update);
        if (input.IncludeDelete) operations.Add(OperationType.Delete);

        var customFilters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(input.RecordId))
        {
            customFilters["recordId"] = input.RecordId.Trim();
        }

        var criteria = new ExtractionCriteria
        {
            EntityNames = new List<string> { input.EntityName.Trim() },
            FromDate = input.StartDate,
            ToDate = input.EndDate,
            Operations = operations.Any() ? operations : null,
            PageSize = input.MaxRecords,
            CustomFilters = customFilters.Any() ? customFilters : null
        };

        return ExtractCriteriaBuildResult.Ok(criteria);
    }
}

public class ExtractInputModel
{
    public string EntityName { get; init; } = string.Empty;
    public string RecordId { get; init; } = string.Empty;
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public bool IncludeCreate { get; init; }
    public bool IncludeUpdate { get; init; }
    public bool IncludeDelete { get; init; }
    public int MaxRecords { get; init; }
}

public class ExtractCriteriaBuildResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ExtractionCriteria? Criteria { get; init; }

    public static ExtractCriteriaBuildResult Ok(ExtractionCriteria criteria)
    {
        return new ExtractCriteriaBuildResult
        {
            Success = true,
            Criteria = criteria
        };
    }

    public static ExtractCriteriaBuildResult Fail(string errorMessage)
    {
        return new ExtractCriteriaBuildResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}
