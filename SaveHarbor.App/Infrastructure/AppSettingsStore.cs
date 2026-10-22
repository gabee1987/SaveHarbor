using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using Serilog;

namespace SaveHarbor.App.Infrastructure;

// Single owner of app-settings.json. Does not take IAppLogger: the logger depends on the active-game context,
// which depends on this store (DI cycle).
public sealed class AppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IAppDataPathProvider _pathProvider;
    private readonly Lock _gate = new();

    public AppSettingsStore(IAppDataPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public void Update(Action<AppSettings> change)
    {
        lock (_gate)
        {
            change(Current);
            Save(Current);
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_pathProvider.AppSettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_pathProvider.AppSettingsPath));
                if (settings is not null)
                {
                    settings.SaveRootOverrides = new Dictionary<string, string>(settings.SaveRootOverrides ?? [], StringComparer.OrdinalIgnoreCase);
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.ForContext("Keyword", "App").Warning(ex, "Could not read app settings; using defaults");
        }

        return new AppSettings();
    }

    private void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_pathProvider.AppDataRoot);
            var temporaryPath = _pathProvider.AppSettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _pathProvider.AppSettingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.ForContext("Keyword", "App").Warning(ex, "Could not save app settings");
        }
    }
}
