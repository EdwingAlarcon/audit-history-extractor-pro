using AuditHistoryExtractorPro.Domain.Entities;

namespace AuditHistoryExtractorPro.UI.Services;

public class HistoryViewService
{
    public HistoryViewResult BuildView(
        IReadOnlyCollection<AuditRecord> records,
        HistoryFilter filter,
        int requestedPage,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(filter);

        if (pageSize <= 0)
        {
            pageSize = 25;
        }

        if (filter.DateFrom.HasValue && filter.DateTo.HasValue && filter.DateFrom > filter.DateTo)
        {
            return HistoryViewResult.WithValidationError("⚠️ La fecha inicial no puede ser mayor que la fecha final.");
        }

        Guid? recordId = null;
        if (!string.IsNullOrWhiteSpace(filter.RecordId))
        {
            if (!Guid.TryParse(filter.RecordId.Trim(), out var parsedRecordId))
            {
                return HistoryViewResult.WithValidationError("⚠️ El filtro de ID debe ser un GUID válido.");
            }

            recordId = parsedRecordId;
        }

        IEnumerable<AuditRecord> query = records;

        if (!string.IsNullOrWhiteSpace(filter.Entity))
        {
            query = query.Where(r =>
                r.EntityName.Contains(filter.Entity.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.User))
        {
            query = query.Where(r =>
                r.UserName.Contains(filter.User.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.Operation))
        {
            query = query.Where(r =>
                string.Equals(r.Operation, filter.Operation.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (recordId.HasValue)
        {
            query = query.Where(r => r.RecordId == recordId.Value);
        }

        if (filter.DateFrom.HasValue)
        {
            var fromDate = filter.DateFrom.Value.Date;
            query = query.Where(r => r.CreatedOn >= fromDate);
        }

        if (filter.DateTo.HasValue)
        {
            var toDateInclusive = filter.DateTo.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(r => r.CreatedOn <= toDateInclusive);
        }

        var filtered = query
            .OrderByDescending(r => r.CreatedOn)
            .ToList();

        var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)pageSize));
        var currentPage = Math.Clamp(requestedPage, 1, totalPages);
        var pageItems = filtered
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var stats = new HistoryAuditStats
        {
            TotalCreates = filtered.Count(r => string.Equals(r.Operation, "Create", StringComparison.OrdinalIgnoreCase)),
            TotalUpdates = filtered.Count(r => string.Equals(r.Operation, "Update", StringComparison.OrdinalIgnoreCase)),
            TotalDeletes = filtered.Count(r => string.Equals(r.Operation, "Delete", StringComparison.OrdinalIgnoreCase)),
            UniqueEntities = filtered
                .Select(r => r.EntityName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        };

        return new HistoryViewResult
        {
            FilteredRecords = filtered,
            PagedRecords = pageItems,
            CurrentPage = currentPage,
            TotalPages = totalPages,
            Stats = stats
        };
    }
}

public class HistoryFilter
{
    public string Entity { get; init; } = string.Empty;
    public string RecordId { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
}

public class HistoryViewResult
{
    public List<AuditRecord> FilteredRecords { get; init; } = new();
    public List<AuditRecord> PagedRecords { get; init; } = new();
    public int CurrentPage { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public HistoryAuditStats Stats { get; init; } = new();
    public string? ValidationError { get; init; }

    public static HistoryViewResult WithValidationError(string message)
    {
        return new HistoryViewResult
        {
            ValidationError = message,
            Stats = new HistoryAuditStats()
        };
    }
}

public class HistoryAuditStats
{
    public int TotalCreates { get; init; }
    public int TotalUpdates { get; init; }
    public int TotalDeletes { get; init; }
    public int UniqueEntities { get; init; }
}
