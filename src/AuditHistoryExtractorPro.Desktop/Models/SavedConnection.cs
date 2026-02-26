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
    public string ServiceUrl => NormalizeServiceUrl(Url);
    public bool RememberPassword { get; set; }
    public bool IsProduction
    {
        get
        {
            var name = ConnectionName ?? string.Empty;
            return name.Contains("produccion", StringComparison.OrdinalIgnoreCase)
                || name.Contains("producción", StringComparison.OrdinalIgnoreCase);
        }
    }
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public EnvironmentType EnvironmentType { get; set; } = EnvironmentType.Prod;
    public DateTime LastUsed { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(ConnectionName)
        ? Url
        : $"{ConnectionName} ({Url})";

    public static string NormalizeServiceUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var trimmed = value.Trim();
        var open = trimmed.LastIndexOf('(');
        var close = trimmed.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            var candidate = trimmed.Substring(open + 1, close - open - 1).Trim();
            if (Uri.TryCreate(candidate, UriKind.Absolute, out _))
            {
                return candidate;
            }
        }

        return trimmed;
    }
}
