using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using AuditHistoryExtractorPro.Models;

namespace AuditHistoryExtractorPro.Services.Resilience;

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

        var retryPolicy = Policy<T>
            .Handle<FaultException>(ex =>
                ex.Message.Contains("429") ||
                ex.Message.Contains("Too Many Requests") ||
                ex.InnerException?.Message.Contains("429") == true)
            .Or<TimeoutException>()
            .Or<FaultException>(ex =>
                ex.Message.Contains("ConcurrencyVersionMismatch") ||
                ex.Message.Contains("QueryTimeout") ||
                ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            .WaitAndRetryAsync(
                retryCount: maxRetries,
                sleepDurationProvider: (attempt, outcome, context) =>
                {
                    var retryAfter = ExtractRetryAfter(outcome.Exception);
                    if (retryAfter.HasValue)
                    {
                        logger.LogWarning(
                            "Throttling detected. Retry after {Seconds}s",
                            retryAfter.Value.TotalSeconds);
                        return retryAfter.Value;
                    }

                    var exponentialDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    var jitterDelay = TimeSpan.FromMilliseconds(jitter.Next(0, 1000));
                    return exponentialDelay.Add(jitterDelay);
                },
                onRetryAsync: (outcome, duration, retryCount, context) =>
                {
                    logger.LogWarning(
                        "Retry {RetryCount} in progress: Waiting {DurationMs}ms. Exception: {Exception}",
                        retryCount,
                        duration.TotalMilliseconds,
                        outcome.Exception?.Message ?? "unknown");
                    return Task.CompletedTask;
                });

        var circuitBreakerPolicy = Policy<T>
            .Handle<FaultException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 10,
                durationOfBreak: TimeSpan.FromMinutes(1),
                onBreak: (outcome, duration) =>
                {
                    logger.LogError(null,
                        "Circuit breaker opened for {Duration}min. Last exception: {Message}",
                        duration.TotalMinutes,
                        outcome.Exception?.Message ?? "unknown");
                },
                onReset: () =>
                {
                    logger.LogInformation("Circuit breaker reset - resuming normal operations");
                });

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
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
                    null,
                    "Operation timed out after {Seconds}s. Task: {Task}",
                    timespan.TotalSeconds,
                    taskName?.ToString() ?? "unknown");
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

    /// <summary>
    /// Política compuesta no-genérica para uso en repositorios con múltiples tipos de retorno
    /// </summary>
    public static IAsyncPolicy CreateCompositePolicyBase<T>(
        ILogger<T> logger,
        TimeSpan? timeout = null,
        int maxThrottleRetries = 5) where T : class
    {
        var jitter = new Random();
        timeout ??= TimeSpan.FromSeconds(120);

        var retryPolicy = Policy
            .Handle<FaultException>(ex =>
                ex.Message.Contains("429") ||
                ex.Message.Contains("Too Many Requests") ||
                ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: maxThrottleRetries,
                sleepDurationProvider: attempt =>
                {
                    var exponentialDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    var jitterDelay = TimeSpan.FromMilliseconds(jitter.Next(0, 1000));
                    return exponentialDelay.Add(jitterDelay);
                },
                onRetry: (exception, duration, retryCount, context) =>
                {
                    logger.LogWarning(
                        "Retry {RetryCount} after {DurationMs}ms: {Message}",
                        retryCount,
                        duration.TotalMilliseconds,
                        exception.Message);
                });

        var timeoutPolicy = Policy.TimeoutAsync(
            timeout.Value,
            onTimeoutAsync: (context, timespan, task) =>
            {
                logger.LogError(null, "Operation timed out after {Seconds}s", timespan.TotalSeconds);
                return Task.CompletedTask;
            });

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
