namespace AuditHistoryExtractorPro.Desktop.Models;

public enum EnvironmentType
{
    Dev,
    QA,
    Prod
}

public sealed class SavedConnection
{
    public string ConnectionName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public EnvironmentType EnvironmentType { get; set; } = EnvironmentType.Prod;
    public DateTime LastUsed { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(ConnectionName)
        ? Url
        : $"{ConnectionName} ({Url})";
}
