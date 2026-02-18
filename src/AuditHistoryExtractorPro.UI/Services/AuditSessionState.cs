using AuditHistoryExtractorPro.Domain.Entities;

namespace AuditHistoryExtractorPro.UI.Services;

public class AuditSessionState
{
    private readonly object _sync = new();
    private List<AuditRecord> _records = new();

    public IReadOnlyList<AuditRecord> Records
    {
        get
        {
            lock (_sync)
            {
                return _records.ToList();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _records.Count;
            }
        }
    }

    public DateTime? LastUpdatedUtc { get; private set; }

    public void SetRecords(IEnumerable<AuditRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        lock (_sync)
        {
            _records = records.ToList();
            LastUpdatedUtc = DateTime.UtcNow;
        }
    }

    public List<AuditRecord> GetRecordsCopy()
    {
        lock (_sync)
        {
            return _records.ToList();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _records.Clear();
            LastUpdatedUtc = DateTime.UtcNow;
        }
    }
}
