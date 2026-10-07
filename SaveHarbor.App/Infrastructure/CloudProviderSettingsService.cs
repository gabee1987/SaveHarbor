using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class CloudProviderSettingsService : ICloudSetupService
{
    private const int MaxSavedFoldersPerGame = 20;
    private const int MaxFolderNameLength = 100;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly CloudProviderOptions options;
    private readonly ICloudProvider cloudProvider;
    private readonly IAppDataPathProvider pathProvider;
    private readonly IAppLogger logger;

    public CloudProviderSettingsService(
        CloudProviderOptions options,
        ICloudProvider cloudProvider,
        IAppDataPathProvider pathProvider,
        IAppLogger logger)
    {
        this.options = options;
        this.cloudProvider = cloudProvider;
        this.pathProvider = pathProvider;
        this.logger = logger;
    }

    // Google Drive folder IDs use only letters, digits, '-' and '_'.
    [GeneratedRegex("^[A-Za-z0-9_-]{10,128}$")]
    private static partial Regex FolderIdPattern();

    public string GetCurrentSharedFolderId(GameId game) => options.GetSharedFolderId(game);

    public bool HasSharedFolderConfigured(GameId game) => options.HasSharedFolder(game);

    public IReadOnlyList<SavedCloudFolder> GetSavedFolders(GameId game)
    {
        var key = game.ToStorageKey();
        var current = options.GetSharedFolderId(game);
        var saved = ReadExistingSettings().SavedFolders
            .Where(folder => string.Equals(folder.Game, key, StringComparison.OrdinalIgnoreCase) && FolderIdPattern().IsMatch(folder.FolderId))
            .ToList();

        // A folder set up before folders were remembered is still offered, without a name.
        if (FolderIdPattern().IsMatch(current) && !saved.Any(folder => folder.FolderId == current))
        {
            saved.Add(new SavedCloudFolder { Game = key, FolderId = current });
        }

        return saved
            .OrderByDescending(folder => folder.FolderId == current)
            .ThenByDescending(folder => folder.LastUsedUtc)
            .ToArray();
    }

    public async Task<CloudSetupTestResult> TestSharedFolderAsync(GameId game, string input, CancellationToken cancellationToken = default)
    {
        var folderId = CloudProviderOptions.NormalizeSharedFolderInput(input);
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return new CloudSetupTestResult(false, "Paste a Google Drive shared folder link or folder ID first.");
        }

        if (!FolderIdPattern().IsMatch(folderId))
        {
            return new CloudSetupTestResult(false, "This does not look like a Google Drive folder link or folder ID. Copy the link from the folder's Share dialog.");
        }

        if (GameUsingFolder(game, folderId) is { } otherGame)
        {
            return new CloudSetupTestResult(false, $"This folder is already set up for {otherGame}. Use a separate folder for each game.");
        }

        if (cloudProvider is not ISharedFolderCloudProvider sharedFolderProvider)
        {
            return new CloudSetupTestResult(false, "The active cloud provider does not support shared folder setup.");
        }

        try
        {
            return await sharedFolderProvider.TestSharedFolderAsync(game, folderId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Error(AppLogKeyword.CloudProvider, exception, "Shared folder setup test failed");
            return new CloudSetupTestResult(false, $"Shared folder test failed: {exception.Message}");
        }
    }

    public async Task SaveSharedFolderAsync(GameId game, string input, string? folderName, CancellationToken cancellationToken = default)
    {
        var folderId = CloudProviderOptions.NormalizeSharedFolderInput(input);
        if (!FolderIdPattern().IsMatch(folderId))
        {
            throw new InvalidOperationException("The shared folder ID is empty or invalid.");
        }

        if (GameUsingFolder(game, folderId) is { } otherGame)
        {
            throw new InvalidOperationException($"This folder is already set up for {otherGame}. Use a separate folder for each game.");
        }

        var settings = ReadExistingSettings();
        settings.SchemaVersion = 2;
        settings.SharedFolders[game.ToStorageKey()] = folderId;
        if (game == GameId.Windrose)
        {
            settings.GoogleSharedFolderId = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(settings.GoogleSharedFolderId))
        {
            settings.SharedFolders.TryAdd(GameId.Windrose.ToStorageKey(), settings.GoogleSharedFolderId);
            settings.GoogleSharedFolderId = string.Empty;
        }

        RememberFolder(settings, game, folderId, folderName);
        await WriteSettingsAsync(settings, cancellationToken);

        options.SetSharedFolderId(game, folderId);
        logger.Information(AppLogKeyword.CloudProvider, "Saved Google Drive shared folder setup for {Game}", game);
    }

    // Each game keeps its worlds in its own Drive folder. The marker written into the folder enforces this once the
    // first world is uploaded; this check also stops two games sharing a folder that is still empty.
    private GameId? GameUsingFolder(GameId game, string folderId) =>
        Enum.GetValues<GameId>().Where(other => other != game && options.GetSharedFolderId(other) == folderId).Select(other => (GameId?)other).FirstOrDefault();

    // Only removes the folder from the list on this PC; nothing in Google Drive changes.
    public async Task ForgetSavedFolderAsync(GameId game, string folderId, CancellationToken cancellationToken = default)
    {
        var settings = ReadExistingSettings();
        var removed = settings.SavedFolders.RemoveAll(folder =>
            string.Equals(folder.Game, game.ToStorageKey(), StringComparison.OrdinalIgnoreCase) && folder.FolderId == folderId);
        if (removed > 0)
        {
            await WriteSettingsAsync(settings, cancellationToken);
        }
    }

    private static void RememberFolder(LocalCloudProviderSettings settings, GameId game, string folderId, string? folderName)
    {
        var key = game.ToStorageKey();
        var existing = settings.SavedFolders.FirstOrDefault(folder =>
            string.Equals(folder.Game, key, StringComparison.OrdinalIgnoreCase) && folder.FolderId == folderId);
        if (existing is null)
        {
            existing = new SavedCloudFolder { Game = key, FolderId = folderId };
            settings.SavedFolders.Add(existing);
        }

        var name = folderName?.Trim() ?? string.Empty;
        if (name.Length > 0)
        {
            existing.Name = name.Length > MaxFolderNameLength ? name[..MaxFolderNameLength] : name;
        }

        existing.LastUsedUtc = DateTimeOffset.UtcNow;

        var surplus = settings.SavedFolders
            .Where(folder => string.Equals(folder.Game, key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(folder => folder.LastUsedUtc)
            .Skip(MaxSavedFoldersPerGame)
            .ToHashSet();
        settings.SavedFolders.RemoveAll(surplus.Contains);
    }

    private async Task WriteSettingsAsync(LocalCloudProviderSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(pathProvider.AppDataRoot);
        var tempPath = pathProvider.CloudProviderSettingsPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(settings, JsonOptions), cancellationToken);
        File.Move(tempPath, pathProvider.CloudProviderSettingsPath, overwrite: true);
    }

    private LocalCloudProviderSettings ReadExistingSettings()
    {
        try
        {
            if (File.Exists(pathProvider.CloudProviderSettingsPath))
            {
                var existing = JsonSerializer.Deserialize<LocalCloudProviderSettings>(File.ReadAllText(pathProvider.CloudProviderSettingsPath));
                if (existing is not null)
                {
                    existing.SharedFolders = new Dictionary<string, string>(existing.SharedFolders, StringComparer.OrdinalIgnoreCase);
                    existing.SavedFolders ??= [];
                    return existing;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            logger.Warning(AppLogKeyword.CloudProvider, "Existing cloud settings could not be read and will be replaced: {Message}", exception.Message);
        }

        return new LocalCloudProviderSettings();
    }
}
