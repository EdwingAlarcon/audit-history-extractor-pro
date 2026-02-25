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
            // Desencriptado para uso en UI/conexión. El nombre del campo se mantiene
            // como EncryptedPassword por contrato de modelo solicitado.
            connection.EncryptedPassword = Unprotect(connection.EncryptedPassword);
            if (string.IsNullOrWhiteSpace(connection.EnvironmentColor))
            {
                connection.EnvironmentColor = "#00A4EF";
            }
        }

        return persisted
            .OrderByDescending(c => c.LastUsed)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveConnection(SavedConnection connection, CancellationToken cancellationToken = default)
    {
        var all = (await LoadRawConnections(cancellationToken)).ToList();

        var encryptedPassword = Protect(connection.EncryptedPassword);
        var normalized = new SavedConnection
        {
            Name = connection.Name,
            ServiceUrl = connection.ServiceUrl,
            Username = connection.Username,
            EncryptedPassword = encryptedPassword,
            EnvironmentColor = string.IsNullOrWhiteSpace(connection.EnvironmentColor)
                ? "#00A4EF"
                : connection.EnvironmentColor,
            LastUsed = connection.LastUsed
        };

        var existing = all.FirstOrDefault(c =>
            string.Equals(c.Name, normalized.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.ServiceUrl, normalized.ServiceUrl, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            all.Add(normalized);
        }
        else
        {
            existing.Name = normalized.Name;
            existing.ServiceUrl = normalized.ServiceUrl;
            existing.Username = normalized.Username;
            existing.EncryptedPassword = normalized.EncryptedPassword;
            existing.EnvironmentColor = normalized.EnvironmentColor;
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
        all.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
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
