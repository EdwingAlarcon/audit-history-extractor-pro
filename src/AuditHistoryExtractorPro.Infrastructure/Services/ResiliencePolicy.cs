using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;

namespace AuditHistoryExtractorPro.Infrastructure.Services;

/// <summary>
/// Fábrica de políticas de resiliencia para Dataverse
/// Maneja: Throttling (429), Timeouts, Fallos transitorios
/// Enterprise-Grade: Exponential Backoff + Jitter + CircuitBreaker
/// </summary>
public static class ResiliencePolicy
{
    /// <summary>
    /// Política de reintentos con backoff exponencial y jitter
    /// Detecta específicamente 429 (Service Throttling Exception)
    /// y otros errores transitorios de Dataverse
    /// </summary>
    public static IAsyncPolicy<T> CreateThrottlingRetryPolicy<T>(
        ILogger<T> logger,
        int maxRetries = 5) where T : class
    {
        var jitter = new Random();

        return Policy<T>
            .Handle<ServiceProtocolException>(ex => 
                // Detectar 429 por el código de estado en el mensaje
                ex.Message.Contains("429") || 
                ex.Message.Contains("Too Many Requests") ||
                ex.InnerException?.Message.Contains("429") == true)
            .Or<TimeoutException>()
            .Or<FaultException>(ex => 
                // Otros errores transitorios de Dataverse
                ex.Message.Contains("ConcurrencyVersionMismatch") ||
                ex.Message.Contains("QueryTimeout") ||
                ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            .WaitAndRetryAsync(
                retryCount: maxRetries,
                sleepDurationProvider: (attempt, exception, context) =>
                {
                    // Extraer Retry-After si está disponible en la respuesta
                    var retryAfter = ExtractRetryAfter(exception);
                    if (retryAfter.HasValue)
                    {
                        logger.LogWarning(
                            "Throttling detected. Server requested retry after {Seconds}s",
                            retryAfter.Value.TotalSeconds);
                        return retryAfter.Value;
                    }

                    // Exponential backoff con jitter (random component)
                    // 2^1 + jitter(0-1000ms), 2^2 + jitter, 2^3 + jitter, etc.
                    var exponentialDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    var jitterDelay = TimeSpan.FromMilliseconds(jitter.Next(0, 1000));
                    var totalDelay = exponentialDelay.Add(jitterDelay);

                    logger.LogWarning(
                        "Retry {Attempt} after {DelayMs}ms due to {ExceptionType}: {Message}",
                        attempt,
                        totalDelay.TotalMilliseconds,
                        exception?.GetType().Name,
                        exception?.Message);

                    return totalDelay;
                },
                onRetry: (outcome, duration, retryCount, context) =>
                {
                    logger.LogWarning(
                        "Retry {RetryCount} in progress: Waiting {DurationMs}ms. " +
                        "Exception: {Exception}",
                        retryCount,
                        duration.TotalMilliseconds,
                        outcome.Exception?.Message);
                })
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 10,
                durationOfBreak: TimeSpan.FromMinutes(1),
                onBreak: (exception, duration) =>
                {
                    logger.LogError(
                        $"Circuit breaker opened. Will retry after {duration.TotalMinutes} minutes. " +
                        $"Last exception: {exception.Exception?.Message}");
                },
                onReset: () =>
                {
                    logger.LogInformation("Circuit breaker reset - resuming normal operations");
                });
    }

    /// <summary>
    /// Política de timeout con configuración específica para Dataverse
    /// </summary>
    public static IAsyncPolicy<T> CreateTimeoutPolicy<T>(
        ILogger<T> logger,
        TimeSpan? timeout = null) where T : class
    {
        timeout ??= TimeSpan.FromSeconds(120); // Default: 2 minutos

        return Policy.TimeoutAsync<T>(
            timeout.Value,
            onTimeoutAsync: (context, timespan, taskName, exception) =>
            {
                logger.LogError(
                    $"Operation timed out after {timespan.TotalSeconds}s. Task: {taskName}");
                return Task.CompletedTask;
            });
    }

    /// <summary>
    /// Política compuesta: Timeout + Throttle Retry + CircuitBreaker
    /// Orden: primero timeout, luego reintentos, luego circuit breaker
    /// </summary>
    public static IAsyncPolicy<T> CreateCompositePolicy<T>(
        ILogger<T> logger,
        TimeSpan? timeout = null,
        int maxThrottleRetries = 5) where T : class
    {
        var timeoutPolicy = CreateTimeoutPolicy<T>(logger, timeout);
        var retryPolicy = CreateThrottlingRetryPolicy<T>(logger, maxThrottleRetries);

        // El orden importa: Retry wrap Timeout para que los timeouts pueden ser reintentados
        return Policy.WrapAsync(retryPolicy, timeoutPolicy);
    }

    // ============ Métodos Privados ============

    /// <summary>
    /// Extrae el valor Retry-After del mensaje de excepción
    /// Dataverse retorna este valor cuando está siendo throttled
    /// </summary>
    private static TimeSpan? ExtractRetryAfter(Exception? exception)
    {
        if (exception == null) return null;

        var message = exception.Message;
        
        // Pattern: "Retry after {X} seconds"
        var match = System.Text.RegularExpressions.Regex.Match(
            message,
            @"[Rr]etry after (\d+) seconds",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        // Pattern alternativo: "Retry-After: 30" (header-like format)
        match = System.Text.RegularExpressions.Regex.Match(
            message,
            @"[Rr]etry-[Aa]fter:\s*(\d+)");

        if (match.Success && int.TryParse(match.Groups[1].Value, out seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }
}
