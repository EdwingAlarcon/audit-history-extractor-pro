using AuditHistoryExtractorPro.Domain.Interfaces;

namespace AuditHistoryExtractorPro.Core.Services;

internal sealed class CoreDomainLogger<T> : ILogger<T>
{
    public void LogInformation(string message, params object[] args) =>
        Console.WriteLine($"[INFO] {Format(message, args)}");

    public void LogWarning(string message, params object[] args) =>
        Console.WriteLine($"[WARN] {Format(message, args)}");

    public void LogError(Exception? exception, string message, params object[] args)
    {
        Console.WriteLine($"[ERROR] {Format(message, args)}");
        if (exception is not null)
        {
            Console.WriteLine(exception);
        }
    }

    public void LogDebug(string message, params object[] args) =>
        Console.WriteLine($"[DEBUG] {Format(message, args)}");

    private static string Format(string message, object[] args) =>
        args is null || args.Length == 0
            ? message
            : $"{message} | {string.Join(", ", args.Select(a => a?.ToString()))}";
}
