using AuditHistoryExtractorPro.Desktop.Models;
using System.IO;
using System.Text.Json;

namespace AuditHistoryExtractorPro.Desktop.Services;

public sealed class ConnectionManagerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _profilesFilePath;

    public ConnectionManagerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _profilesFilePath = Path.Combine(appData, "AuditHistoryExtractorPro", "connection-profiles.json");
    }

    public async Task<IReadOnlyList<ConnectionProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_profilesFilePath))
        {
            return Array.Empty<ConnectionProfile>();
        }

        await using var stream = File.OpenRead(_profilesFilePath);
        var profiles = await JsonSerializer.DeserializeAsync<List<ConnectionProfile>>(stream, JsonOptions, cancellationToken)
            ?? new List<ConnectionProfile>();

        return profiles
            .OrderByDescending(p => p.LastUsed)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveProfileAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var profiles = (await GetProfilesAsync(cancellationToken)).ToList();

        var existing = profiles.FirstOrDefault(p =>
            string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Url, profile.Url, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Name = profile.Name;
            existing.Url = profile.Url;
            existing.UserName = profile.UserName;
            existing.LastUsed = profile.LastUsed;
        }
        else
        {
            profiles.Add(profile);
        }

        await SaveProfilesInternalAsync(profiles, cancellationToken);
    }

    public async Task DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        var profiles = (await GetProfilesAsync(cancellationToken)).ToList();
        profiles.RemoveAll(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
        await SaveProfilesInternalAsync(profiles, cancellationToken);
    }

    private async Task SaveProfilesInternalAsync(List<ConnectionProfile> profiles, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_profilesFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_profilesFilePath);
        await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, cancellationToken);
    }
}
