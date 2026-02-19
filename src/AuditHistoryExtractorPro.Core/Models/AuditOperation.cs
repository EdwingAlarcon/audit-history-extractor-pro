namespace AuditHistoryExtractorPro.Core.Models;

public enum AuditOperation
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Access = 4,
    Upsert = 5
}
