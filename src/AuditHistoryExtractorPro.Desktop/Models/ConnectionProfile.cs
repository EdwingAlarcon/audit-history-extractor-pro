namespace AuditHistoryExtractorPro.Desktop.Models;

public sealed class ConnectionProfile
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime LastUsed { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(Name)
        ? Url
        : $"{Name} ({Url})";
}
