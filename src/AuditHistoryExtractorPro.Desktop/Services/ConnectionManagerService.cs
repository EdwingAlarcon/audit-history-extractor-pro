using AuditHistoryExtractorPro.Desktop.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuditHistoryExtractorPro.Desktop.Services;

public sealed class ConnectionManagerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("AuditHistoryExtractorPro::Connections::v1");

    private readonly string _profilesFilePath;

    public ConnectionManagerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _profilesFilePath = Path.Combine(appData, "AuditHistoryExtractorPro", "connections.json");
    }

    public async Task<IReadOnlyList<ConnectionProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_profilesFilePath))
        {
            return Array.Empty<ConnectionProfile>();
        }

        await using var stream = File.OpenRead(_profilesFilePath);
        var storedProfiles = await JsonSerializer.DeserializeAsync<List<StoredConnectionProfile>>(stream, JsonOptions, cancellationToken)
            ?? new List<StoredConnectionProfile>();

        var profiles = storedProfiles.Select(stored => new ConnectionProfile
        {
            Name = stored.Name,
            Url = stored.Url,
            UserName = UnprotectText(stored.UserNameProtected),
            Credential = UnprotectText(stored.CredentialProtected),
            LastUsed = stored.LastUsed
        });

        return profiles
            .OrderByDescending(p => p.LastUsed)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var profiles = (await LoadStoredProfilesAsync(cancellationToken)).ToList();

        var existing = profiles.FirstOrDefault(p =>
            string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Url, profile.Url, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Name = profile.Name;
            existing.Url = profile.Url;
            existing.UserNameProtected = ProtectText(profile.UserName);
            existing.CredentialProtected = ProtectText(profile.Credential);
            existing.LastUsed = profile.LastUsed;
        }
        else
        {
            profiles.Add(new StoredConnectionProfile
            {
                Name = profile.Name,
                Url = profile.Url,
                UserNameProtected = ProtectText(profile.UserName),
                CredentialProtected = ProtectText(profile.Credential),
                LastUsed = profile.LastUsed
            });
        }

        await SaveProfilesInternalAsync(profiles, cancellationToken);
    }

    public async Task DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        var profiles = (await LoadStoredProfilesAsync(cancellationToken)).ToList();
        profiles.RemoveAll(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
        await SaveProfilesInternalAsync(profiles, cancellationToken);
    }

    private async Task<IReadOnlyList<StoredConnectionProfile>> LoadStoredProfilesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_profilesFilePath))
        {
            return Array.Empty<StoredConnectionProfile>();
        }

        await using var stream = File.OpenRead(_profilesFilePath);
        return await JsonSerializer.DeserializeAsync<List<StoredConnectionProfile>>(stream, JsonOptions, cancellationToken)
            ?? new List<StoredConnectionProfile>();
    }

    private async Task SaveProfilesInternalAsync(List<StoredConnectionProfile> profiles, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_profilesFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_profilesFilePath);
        await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, cancellationToken);
    }

    private static string ProtectText(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain))
        {
            return string.Empty;
        }

        var data = Encoding.UTF8.GetBytes(plain);
        var encrypted = ProtectedData.Protect(data, DpapiEntropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string UnprotectText(string encryptedBase64)
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

    private sealed class StoredConnectionProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string UserNameProtected { get; set; } = string.Empty;
        public string CredentialProtected { get; set; } = string.Empty;
        public DateTime LastUsed { get; set; }
    }
}
