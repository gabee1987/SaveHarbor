using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Updates;
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
            DeveloperProviderOverride = ReadProviderOverride(section),
            LockTtlSeconds = ReadInt(section[nameof(CloudProviderOptions.LockTtlSeconds)], defaults.LockTtlSeconds),
            HeartbeatIntervalSeconds = ReadInt(section[nameof(CloudProviderOptions.HeartbeatIntervalSeconds)], defaults.HeartbeatIntervalSeconds),
            GoogleAppFolderName = string.IsNullOrWhiteSpace(section[nameof(CloudProviderOptions.GoogleAppFolderName)])
                ? defaults.GoogleAppFolderName
                : section[nameof(CloudProviderOptions.GoogleAppFolderName)]!,
            GoogleClientSecretsPath = section[nameof(CloudProviderOptions.GoogleClientSecretsPath)] ?? defaults.GoogleClientSecretsPath,
            Supabase = ReadSupabaseOptions(section.GetSection(nameof(CloudProviderOptions.Supabase)))
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

    // --update-source <folder> replaces GitHub with a folder of packed releases, for trying an update before publishing it.
    public static UpdateOptions LoadUpdateOptions(IReadOnlyList<string> args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var defaults = new UpdateOptions();
        var section = configuration.GetSection(UpdateOptions.SectionName);
        return new UpdateOptions
        {
            GitHubRepository = string.IsNullOrWhiteSpace(section[nameof(UpdateOptions.GitHubRepository)])
                ? defaults.GitHubRepository
                : section[nameof(UpdateOptions.GitHubRepository)]!.Trim(),
            CheckIntervalHours = ReadInt(section[nameof(UpdateOptions.CheckIntervalHours)], defaults.CheckIntervalHours),
            AutoDownload = ReadBool(section[nameof(UpdateOptions.AutoDownload)], defaults.AutoDownload),
            LocalSourceFolder = ReadArgument(args, "--update-source") ?? string.Empty
        };
    }

    private static SupabaseOptions ReadSupabaseOptions(IConfigurationSection section)
    {
        var defaults = new SupabaseOptions();
        return new SupabaseOptions
        {
            Url = section[nameof(SupabaseOptions.Url)]?.Trim().TrimEnd('/') ?? defaults.Url,
            PublishableKey = section[nameof(SupabaseOptions.PublishableKey)]?.Trim() ?? defaults.PublishableKey,
            Bucket = string.IsNullOrWhiteSpace(section[nameof(SupabaseOptions.Bucket)]) ? defaults.Bucket : section[nameof(SupabaseOptions.Bucket)]!.Trim(),
            MaxArchiveBytes = long.TryParse(section[nameof(SupabaseOptions.MaxArchiveBytes)], out var maxBytes) && maxBytes > 0 ? maxBytes : defaults.MaxArchiveBytes,
            HttpTimeoutSeconds = ReadInt(section[nameof(SupabaseOptions.HttpTimeoutSeconds)], defaults.HttpTimeoutSeconds),
            TransferTimeoutSeconds = ReadInt(section[nameof(SupabaseOptions.TransferTimeoutSeconds)], defaults.TransferTimeoutSeconds)
        };
    }

    private static string ReadProviderOverride(IConfigurationSection section)
    {
        var value = section[nameof(CloudProviderOptions.DeveloperProviderOverride)];
        if (string.IsNullOrWhiteSpace(value))
        {
            value = section["Provider"];
        }

        return string.Equals(value?.Trim(), CloudProviderKind.LocalTest, StringComparison.OrdinalIgnoreCase)
            ? CloudProviderKind.LocalTest
            : string.Empty;
    }

    public static IReadOnlyDictionary<GameId, GameOptions> LoadGameOptions()
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
                LaunchUri = FirstNonEmpty(section["LaunchUri"], legacy?["LaunchUri"], GameDefaults.LaunchUri(game)),
                ExecutablePath = FirstNonEmpty(section["ExecutablePath"], legacy?["ExecutablePath"], string.Empty),
                SaveRoot = section["SaveRoot"] ?? string.Empty
            };
        }

        return byGame;
    }

    public static GameId? ReadGameArgument(IReadOnlyList<string> args) =>
        GameIdExtensions.TryParseStorageKey(ReadArgument(args, "--game") ?? string.Empty, out var game) ? game : null;

    // "supabase" or "googledrive" (the names the app keeps its state under); null for anything else.
    public static string? ReadProviderArgument(IReadOnlyList<string> args) => ReadArgument(args, "--provider")?.Trim().ToLowerInvariant() switch
    {
        "supabase" => CloudProviderKind.Supabase,
        "googledrive" => CloudProviderKind.GoogleDrive,
        _ => null
    };

    private static string? ReadArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

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
