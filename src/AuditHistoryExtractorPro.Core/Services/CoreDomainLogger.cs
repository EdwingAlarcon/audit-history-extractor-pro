using Microsoft.Extensions.Logging;

namespace AuditHistoryExtractorPro.Core.Services;

/// <summary>
/// Logger de consola mínimo para contextos sin DI (ej. AuthHelper en Desktop).
/// Implementa Microsoft.Extensions.Logging.ILogger<T> para compatibilidad total con MEL.
/// </summary>
internal sealed class CoreDomainLogger<T> : ILogger<T>
{
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var level = logLevel switch
        {
            LogLevel.Debug       => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning     => "WARN ",
            LogLevel.Error       => "ERROR",
            LogLevel.Critical    => "CRIT ",
            _                    => "TRACE"
        };

        Console.WriteLine($"[{level}] {formatter(state, exception)}");
        if (exception is not null)
            Console.WriteLine(exception);
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
