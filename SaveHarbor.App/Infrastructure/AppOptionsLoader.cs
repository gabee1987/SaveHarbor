using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Games;
using Serilog.Events;

namespace SaveHarbor.App.Infrastructure;

public static class AppOptionsLoader
{
    public static AppLoggingOptions LoadLoggingOptions()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var defaults = new AppLoggingOptions();
        var section = configuration.GetSection(AppLoggingOptions.SectionName);
        if (!section.Exists())
        {
            return defaults;
        }

        var options = new AppLoggingOptions
        {
            Enabled = ReadBool(section[nameof(AppLoggingOptions.Enabled)], defaults.Enabled),
            DefaultMinimumLevel = ReadLevel(
                section[nameof(AppLoggingOptions.DefaultMinimumLevel)],
                defaults.DefaultMinimumLevel),
            RetainedFileCountLimit = ReadInt(
                section[nameof(AppLoggingOptions.RetainedFileCountLimit)],
                defaults.RetainedFileCountLimit),
            KeywordMinimumLevels = new Dictionary<AppLogKeyword, LogEventLevel>(defaults.KeywordMinimumLevels)
        };

        foreach (var keywordSection in section.GetSection(nameof(AppLoggingOptions.KeywordMinimumLevels)).GetChildren())
        {
            if (Enum.TryParse<AppLogKeyword>(keywordSection.Key, ignoreCase: true, out var keyword))
            {
                options.KeywordMinimumLevels[keyword] = ReadLevel(
                    keywordSection.Value,
                    options.KeywordMinimumLevels.GetValueOrDefault(keyword, options.DefaultMinimumLevel));
            }
        }

        return options;
    }

    public static CloudProviderOptions LoadCloudProviderOptions()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var defaults = new CloudProviderOptions();
        var section = configuration.GetSection(CloudProviderOptions.SectionName);
        if (!section.Exists())
        {
            return defaults;
        }

        var options = new CloudProviderOptions
        {
            Provider = string.IsNullOrWhiteSpace(section[nameof(CloudProviderOptions.Provider)])
                ? defaults.Provider
                : section[nameof(CloudProviderOptions.Provider)]!,
            GoogleAppFolderName = string.IsNullOrWhiteSpace(section[nameof(CloudProviderOptions.GoogleAppFolderName)])
                ? defaults.GoogleAppFolderName
                : section[nameof(CloudProviderOptions.GoogleAppFolderName)]!,
            GoogleClientSecretsPath = section[nameof(CloudProviderOptions.GoogleClientSecretsPath)] ?? defaults.GoogleClientSecretsPath
        };

        // A shared folder in appsettings.json predates multi-game support and belongs to Windrose.
        var configuredFolderId = section["GoogleSharedFolderId"];
        if (!string.IsNullOrWhiteSpace(configuredFolderId))
        {
            options.SetSharedFolderId(GameId.Windrose, configuredFolderId);
        }

        var pathProvider = new AppDataPathProvider();
        var localSettingsPath = pathProvider.CloudProviderSettingsPath;
        if (!File.Exists(localSettingsPath))
        {
            return options;
        }

        try
        {
            var localSettings = JsonSerializer.Deserialize<LocalCloudProviderSettings>(File.ReadAllText(localSettingsPath));
            if (localSettings is null)
            {
                return options;
            }

            foreach (var (key, folderId) in localSettings.SharedFolders)
            {
                if (GameIdExtensions.TryParseStorageKey(key, out var game) && !string.IsNullOrWhiteSpace(folderId))
                {
                    options.SetSharedFolderId(game, folderId);
                }
            }

            if (!localSettings.SharedFolders.ContainsKey(GameId.Windrose.ToStorageKey())
                && !string.IsNullOrWhiteSpace(localSettings.GoogleSharedFolderId))
            {
                options.SetSharedFolderId(GameId.Windrose, localSettings.GoogleSharedFolderId);
            }
        }
        catch
        {
            // A broken local setup file should not stop the app from starting.
        }

        return options;
    }

    public static GameOptionsProvider LoadGameOptions()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var byGame = new Dictionary<GameId, GameOptions>();
        foreach (var game in Enum.GetValues<GameId>())
        {
            var section = configuration.GetSection($"SaveHarbor:Games:{game}");
            var legacy = game == GameId.Windrose ? configuration.GetSection("SaveHarbor:GameLauncher") : null;
            byGame[game] = new GameOptions
            {
                LaunchUri = FirstNonEmpty(section["LaunchUri"], legacy?["LaunchUri"], DefaultLaunchUri(game)),
                ExecutablePath = FirstNonEmpty(section["ExecutablePath"], legacy?["ExecutablePath"], string.Empty),
                SaveRoot = section["SaveRoot"] ?? string.Empty
            };
        }

        return new GameOptionsProvider(byGame);
    }

    public static GameId? ReadGameArgument(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], "--game", StringComparison.OrdinalIgnoreCase) &&
                GameIdExtensions.TryParseStorageKey(args[index + 1], out var game))
            {
                return game;
            }
        }

        return null;
    }

    private static string DefaultLaunchUri(GameId game) => game switch
    {
        GameId.Windrose => "steam://rungameid/3041230",
        GameId.Dragonwilds => "steam://rungameid/1374490",
        _ => string.Empty
    };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static LogEventLevel ReadLevel(string? value, LogEventLevel fallback)
    {
        return Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level)
            ? level
            : fallback;
    }

    private static bool ReadBool(string? value, bool fallback)
    {
        return bool.TryParse(value, out var result) ? result : fallback;
    }

    private static int ReadInt(string? value, int fallback)
    {
        return int.TryParse(value, out var result) && result > 0 ? result : fallback;
    }
}
