using System.Text.Json;

namespace AuditHistoryExtractorPro.UI.Services;

public class UserConfigService : IUserConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public UserConfigService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "AuditHistoryExtractorPro");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "user-settings.json");
    }

    public async Task<UserConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new UserConfig();
            }

            await using var stream = File.OpenRead(_settingsPath);
            var config = await JsonSerializer.DeserializeAsync<UserConfig>(stream, JsonOptions, cancellationToken);
            return config ?? new UserConfig();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(UserConfig config, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.Create(_settingsPath);
            await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
