namespace AuditHistoryExtractorPro.Core.Models;

public enum AuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Activate = 4,
    Deactivate = 5,
    Assign = 13,
    Share = 14,
    Unshare = 15,
    Merge = 16,
    GrantAccess = 17,
    ModifyAccess = 18,
    RevokeAccess = 19
}
