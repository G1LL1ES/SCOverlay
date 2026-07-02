using System.Text.Json;

namespace SCOverlay.Core.Application;

public sealed class FileAppSettingsStore
{
    private readonly string settingsPath;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public FileAppSettingsStore(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        AppPathProvider.EnsureCreated(paths);
        settingsPath = Path.Combine(paths.DataRoot, "settings.json");
    }

    public async ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath))
        {
            return new AppSettings();
        }

        await using FileStream stream = File.OpenRead(settingsPath);
        AppSettings? settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return settings is null || string.IsNullOrWhiteSpace(settings.ActiveProfileId)
            ? new AppSettings()
            : settings;
    }

    public async ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath) ?? string.Empty);

        await using FileStream stream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, jsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}
