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
    private readonly string _legacyProfilesFilePath;

    public ConnectionProvider()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _filePath = Path.Combine(appData, "AuditHistoryExtractorPro", "connections.json");
        _legacyProfilesFilePath = Path.Combine(appData, "AuditHistoryExtractorPro", "connection-profiles.json");
    }

    public async Task<IReadOnlyList<SavedConnection>> GetConnections(CancellationToken cancellationToken = default)
    {
        await MigrateLegacyConnectionsIfNeeded(cancellationToken);

        if (!File.Exists(_filePath))
        {
            return Array.Empty<SavedConnection>();
        }

        await using var stream = File.OpenRead(_filePath);
        var persisted = await JsonSerializer.DeserializeAsync<List<SavedConnection>>(stream, JsonOptions, cancellationToken)
            ?? new List<SavedConnection>();

        foreach (var connection in persisted)
        {
            connection.Url = SavedConnection.NormalizeServiceUrl(connection.Url);
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
            Url = SavedConnection.NormalizeServiceUrl(connection.Url),
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

    private async Task MigrateLegacyConnectionsIfNeeded(CancellationToken cancellationToken)
    {
        // Caso A: aún no existe connections.json moderno, pero sí existe el archivo
        // histórico connection-profiles.json → migrar.
        if (!File.Exists(_filePath) && File.Exists(_legacyProfilesFilePath))
        {
            var legacyProfiles = await LoadLegacyProfilesFromFile(_legacyProfilesFilePath, cancellationToken);
            if (legacyProfiles.Count > 0)
            {
                var migrated = legacyProfiles.Select(lp => new SavedConnection
                {
                    ConnectionName = lp.Name,
                    Url = SavedConnection.NormalizeServiceUrl(lp.Url),
                    User = lp.UserName,
                    Password = Protect(lp.Credential),
                    EnvironmentType = ResolveEnvironmentType(lp.Url, null),
                    LastUsed = lp.LastUsed
                }).ToList();

                await Persist(migrated, cancellationToken);
            }

            return;
        }

        // Caso B: existe connections.json pero podría estar en esquema legacy
        // (Name/ServiceUrl/Username/EncryptedPassword/EnvironmentColor).
        if (!File.Exists(_filePath))
        {
            return;
        }

        var modern = await LoadRawConnections(cancellationToken);
        if (modern.Any(c => !string.IsNullOrWhiteSpace(c.ConnectionName)))
        {
            return; // ya está en esquema nuevo
        }

        var legacySaved = await LoadLegacySavedConnectionsFromFile(_filePath, cancellationToken);
        if (legacySaved.Count == 0)
        {
            var legacyProfilesFromSameFile = await LoadLegacyProfilesFromFile(_filePath, cancellationToken);
            if (legacyProfilesFromSameFile.Count == 0)
            {
                return;
            }

            var migratedFromProfiles = legacyProfilesFromSameFile.Select(lp => new SavedConnection
            {
                ConnectionName = lp.Name,
                Url = SavedConnection.NormalizeServiceUrl(lp.Url),
                User = lp.UserName,
                Password = Protect(lp.Credential),
                EnvironmentType = ResolveEnvironmentType(lp.Url, null),
                LastUsed = lp.LastUsed
            }).ToList();

            await Persist(migratedFromProfiles, cancellationToken);
            return;
        }

        var migratedFromLegacySaved = legacySaved.Select(ls => new SavedConnection
        {
            ConnectionName = ls.Name,
            Url = SavedConnection.NormalizeServiceUrl(ls.ServiceUrl),
            User = ls.Username,
            // Si ya venía como EncryptedPassword, se conserva tal cual;
            // si viene vacío, se cifra el fallback Credential.
            Password = !string.IsNullOrWhiteSpace(ls.EncryptedPassword)
                ? ls.EncryptedPassword
                : Protect(ls.Credential),
            EnvironmentType = ResolveEnvironmentType(ls.ServiceUrl, ls.EnvironmentColor),
            LastUsed = ls.LastUsed
        }).ToList();

        await Persist(migratedFromLegacySaved, cancellationToken);
    }

    private static async Task<List<LegacyConnectionProfile>> LoadLegacyProfilesFromFile(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new List<LegacyConnectionProfile>();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<LegacyConnectionProfile>>(stream, JsonOptions, cancellationToken)
            ?? new List<LegacyConnectionProfile>();
    }

    private static async Task<List<LegacySavedConnection>> LoadLegacySavedConnectionsFromFile(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new List<LegacySavedConnection>();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<LegacySavedConnection>>(stream, JsonOptions, cancellationToken)
            ?? new List<LegacySavedConnection>();
    }

    private static EnvironmentType ResolveEnvironmentType(string? url, string? legacyColor)
    {
        var color = (legacyColor ?? string.Empty).Trim().ToUpperInvariant();
        if (color == "#107C10") return EnvironmentType.Dev;
        if (color == "#F7630C") return EnvironmentType.QA;
        if (color == "#0078D4" || color == "#00A4EF") return EnvironmentType.Prod;

        var normalizedUrl = (url ?? string.Empty).ToLowerInvariant();
        if (normalizedUrl.Contains("qa") || normalizedUrl.Contains("test")) return EnvironmentType.QA;
        if (normalizedUrl.Contains("dev") || normalizedUrl.Contains("sandbox")) return EnvironmentType.Dev;
        return EnvironmentType.Prod;
    }

    private sealed class LegacyConnectionProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Credential { get; set; } = string.Empty;
        public DateTime LastUsed { get; set; }
    }

    private sealed class LegacySavedConnection
    {
        public string Name { get; set; } = string.Empty;
        public string ServiceUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string EncryptedPassword { get; set; } = string.Empty;
        public string Credential { get; set; } = string.Empty;
        public string EnvironmentColor { get; set; } = string.Empty;
        public DateTime LastUsed { get; set; }
    }
}
