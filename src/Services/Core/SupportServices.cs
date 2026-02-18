using AuditHistoryExtractorPro.Domain.Entities;
using AuditHistoryExtractorPro.Domain.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Azure.Security.KeyVault.Secrets;
using Azure.Identity;

namespace AuditHistoryExtractorPro.Infrastructure.Services;

/// <summary>
/// Servicio de caché en memoria
/// </summary>
public class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCacheService> _logger;

    public MemoryCacheService(IMemoryCache cache, ILogger<MemoryCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        _cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var options = new MemoryCacheEntryOptions();
        
        if (expiration.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = expiration.Value;
        }
        else
        {
            options.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
        }

        _cache.Set(key, value, options);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // MemoryCache no tiene un método Clear, pero se puede implementar tracking
        _logger.LogWarning("MemoryCache does not support full clear operation");
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var exists = _cache.TryGetValue(key, out _);
        return Task.FromResult(exists);
    }
}

/// <summary>
/// Gestor de secretos con Azure Key Vault
/// </summary>
public class AzureKeyVaultSecretManager : ISecretManager
{
    private readonly SecretClient? _client;
    private readonly ILogger<AzureKeyVaultSecretManager> _logger;
    private readonly bool _isEnabled;

    public AzureKeyVaultSecretManager(
        string? vaultUrl,
        ILogger<AzureKeyVaultSecretManager> logger)
    {
        _logger = logger;
        
        if (!string.IsNullOrWhiteSpace(vaultUrl))
        {
            try
            {
                _client = new SecretClient(new Uri(vaultUrl), new DefaultAzureCredential());
                _isEnabled = true;
                _logger.LogInformation("Azure Key Vault initialized: {VaultUrl}", vaultUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Key Vault");
                _isEnabled = false;
            }
        }
        else
        {
            _isEnabled = false;
            _logger.LogInformation("Azure Key Vault is not configured");
        }
    }

    public async Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled || _client == null)
        {
            throw new InvalidOperationException("Azure Key Vault is not configured");
        }

        try
        {
            var secret = await _client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            _logger.LogInformation("Secret retrieved: {SecretName}", secretName);
            return secret.Value.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret: {SecretName}", secretName);
            throw;
        }
    }

    public async Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled || _client == null)
        {
            throw new InvalidOperationException("Azure Key Vault is not configured");
        }

        try
        {
            await _client.SetSecretAsync(secretName, secretValue, cancellationToken);
            _logger.LogInformation("Secret set: {SecretName}", secretName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set secret: {SecretName}", secretName);
            throw;
        }
    }

    public async Task<bool> SecretExistsAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled || _client == null)
        {
            return false;
        }

        try
        {
            await _client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Procesador de registros de auditoría
/// </summary>
public class AuditProcessor : IAuditProcessor
{
    private readonly ILogger<AuditProcessor> _logger;

    public AuditProcessor(ILogger<AuditProcessor> logger)
    {
        _logger = logger;
    }

    public Task<List<AuditRecord>> NormalizeRecordsAsync(
        List<AuditRecord> records,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Normalizing {Count} records", records.Count);

        foreach (var record in records)
        {
            // Normalizar nombres de usuario
            if (string.IsNullOrEmpty(record.UserName) && !string.IsNullOrEmpty(record.UserId))
            {
                record.UserName = $"User-{record.UserId}";
            }

            // Normalizar valores nulos en cambios
            foreach (var change in record.Changes.Values)
            {
                change.OldValue ??= "[Empty]";
                change.NewValue ??= "[Empty]";
            }
        }

        return Task.FromResult(records);
    }

    public Task<RecordComparison> CompareRecordVersionsAsync(
        AuditRecord previous,
        AuditRecord current,
        CancellationToken cancellationToken = default)
    {
        var comparison = new RecordComparison
        {
            RecordId = current.RecordId,
            EntityName = current.EntityName,
            PreviousVersion = previous,
            CurrentVersion = current,
            ComparisonDate = DateTime.UtcNow
        };

        var allFields = previous.Changes.Keys
            .Union(current.Changes.Keys)
            .Distinct();

        foreach (var field in allFields)
        {
            var hasPrevious = previous.Changes.TryGetValue(field, out var prevChange);
            var hasCurrent = current.Changes.TryGetValue(field, out var currChange);

            DifferenceType diffType;
            object? oldValue = null;
            object? newValue = null;

            if (hasPrevious && hasCurrent)
            {
                oldValue = prevChange!.NewValue;
                newValue = currChange!.NewValue;
                diffType = oldValue?.ToString() == newValue?.ToString() 
                    ? DifferenceType.Unchanged 
                    : DifferenceType.Modified;
            }
            else if (hasCurrent)
            {
                newValue = currChange!.NewValue;
                diffType = DifferenceType.Added;
            }
            else
            {
                oldValue = prevChange!.NewValue;
                diffType = DifferenceType.Removed;
            }

            comparison.Differences.Add(new FieldDifference
            {
                FieldName = field,
                OldValue = oldValue,
                NewValue = newValue,
                Type = diffType,
                Description = GetDifferenceDescription(diffType, oldValue, newValue)
            });
        }

        return Task.FromResult(comparison);
    }

    public Task<List<AuditRecord>> FilterRelevantChangesAsync(
        List<AuditRecord> records,
        List<string>? relevantFields = null,
        CancellationToken cancellationToken = default)
    {
        if (relevantFields == null || !relevantFields.Any())
        {
            return Task.FromResult(records);
        }

        _logger.LogInformation(
            "Filtering records for relevant fields: {Fields}",
            string.Join(", ", relevantFields));

        var filtered = records.Where(record =>
            record.Changes.Keys.Any(field => 
                relevantFields.Contains(field, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        _logger.LogInformation(
            "Filtered {Original} records to {Filtered} records",
            records.Count,
            filtered.Count);

        return Task.FromResult(filtered);
    }

    public Task<List<AuditRecord>> EnrichRecordsAsync(
        List<AuditRecord> records,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Enriching {Count} records", records.Count);

        foreach (var record in records)
        {
            // Agregar metadata adicional
            record.AdditionalData["ProcessedAt"] = DateTime.UtcNow;
            record.AdditionalData["ChangeCount"] = record.Changes.Count;
            record.AdditionalData["HasChanges"] = record.Changes.Any(c => c.Value.HasChanged);
        }

        return Task.FromResult(records);
    }

    private string GetDifferenceDescription(DifferenceType type, object? oldValue, object? newValue)
    {
        return type switch
        {
            DifferenceType.Added => $"Field added with value: {newValue}",
            DifferenceType.Modified => $"Changed from '{oldValue}' to '{newValue}'",
            DifferenceType.Removed => $"Field removed (was: {oldValue})",
            DifferenceType.Unchanged => "No change",
            _ => "Unknown"
        };
    }
}
