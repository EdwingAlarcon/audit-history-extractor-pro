using AuditHistoryExtractorPro.Desktop.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuditHistoryExtractorPro.Desktop.Services;

public sealed class ConnectionProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("AuditHistoryExtractorPro::SavedConnections::v1");

    private readonly string _filePath;

    public ConnectionProvider()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _filePath = Path.Combine(appData, "AuditHistoryExtractorPro", "connections.json");
    }

    public async Task<IReadOnlyList<SavedConnection>> GetConnections(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<SavedConnection>();
        }

        await using var stream = File.OpenRead(_filePath);
        var persisted = await JsonSerializer.DeserializeAsync<List<SavedConnection>>(stream, JsonOptions, cancellationToken)
            ?? new List<SavedConnection>();

        foreach (var connection in persisted)
        {
            connection.Password = Unprotect(connection.Password);
        }

        return persisted
            .OrderByDescending(c => c.LastUsed)
            .ThenBy(c => c.ConnectionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveConnection(SavedConnection connection, CancellationToken cancellationToken = default)
    {
        var all = (await LoadRawConnections(cancellationToken)).ToList();

        var encryptedPassword = Protect(connection.Password);
        var normalized = new SavedConnection
        {
            ConnectionName = connection.ConnectionName,
            Url = connection.Url,
            User = connection.User,
            Password = encryptedPassword,
            EnvironmentType = connection.EnvironmentType,
            LastUsed = connection.LastUsed
        };

        var existing = all.FirstOrDefault(c =>
            string.Equals(c.ConnectionName, normalized.ConnectionName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Url, normalized.Url, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            all.Add(normalized);
        }
        else
        {
            existing.ConnectionName = normalized.ConnectionName;
            existing.Url = normalized.Url;
            existing.User = normalized.User;
            existing.Password = normalized.Password;
            existing.EnvironmentType = normalized.EnvironmentType;
            existing.LastUsed = normalized.LastUsed;
        }

        await Persist(all, cancellationToken);
    }

    public async Task DeleteConnection(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var all = (await LoadRawConnections(cancellationToken)).ToList();
        all.RemoveAll(c => string.Equals(c.ConnectionName, name, StringComparison.OrdinalIgnoreCase));
        await Persist(all, cancellationToken);
    }

    private async Task<IReadOnlyList<SavedConnection>> LoadRawConnections(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<SavedConnection>();
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<SavedConnection>>(stream, JsonOptions, cancellationToken)
            ?? new List<SavedConnection>();
    }

    private async Task Persist(IReadOnlyList<SavedConnection> connections, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, connections, JsonOptions, cancellationToken);
    }

    private static string Protect(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plain);
        var encrypted = ProtectedData.Protect(bytes, DpapiEntropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Unprotect(string encryptedBase64)
    {
        if (string.IsNullOrWhiteSpace(encryptedBase64))
        {
            return string.Empty;
        }

        try
        {
            var encrypted = Convert.FromBase64String(encryptedBase64);
            var plain = ProtectedData.Unprotect(encrypted, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return string.Empty;
        }
    }
}
