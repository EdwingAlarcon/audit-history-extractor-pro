namespace AuditHistoryExtractorPro.Desktop.Models;

public sealed class SavedConnection
{
    public string Name { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
    public string EnvironmentColor { get; set; } = "#00A4EF";
    public DateTime LastUsed { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(Name)
        ? ServiceUrl
        : $"{Name} ({ServiceUrl})";
}
