using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

// The app's ICloudProvider: forwards every call to the provider chosen on this PC, so the services above it never hold
// a provider the user has switched away from.
public sealed class CloudProviderSelector : ICloudProvider, ISharedFolderCloudProvider, ICloudStorageMaintenance, ICloudProviderSelector
{
    private readonly CloudProviderOptions options;
    private readonly IAppSettingsStore settings;
    private readonly FolderCloudProvider folder;
    private readonly GoogleDriveCloudProvider drive;
    private readonly Lazy<ICloudProvider> supabase;
    private readonly IAppLogger logger;
    private readonly ICloudProvider notChosen = new NotConfiguredCloudProvider(
        "Not chosen",
        "Choose a cloud provider to share worlds with your friends.",
        needsSetup: true);

    public CloudProviderSelector(
        CloudProviderOptions options,
        IAppSettingsStore settings,
        FolderCloudProvider folder,
        GoogleDriveCloudProvider drive,
        Func<ICloudProvider> supabaseFactory,
        IAppLogger logger)
    {
        this.options = options;
        this.settings = settings;
        this.folder = folder;
        this.drive = drive;
        this.logger = logger;
        supabase = new Lazy<ICloudProvider>(supabaseFactory);

        // Installs from before the choice existed used Google Drive; one with a shared folder keeps it.
        if (!options.ForcesLocalTest
            && string.IsNullOrWhiteSpace(settings.Current.CloudProvider)
            && Enum.GetValues<GameId>().Any(options.HasSharedFolder))
        {
            settings.Update(current => current.CloudProvider = CloudProviderKind.GoogleDrive);
            logger.Information(AppLogKeyword.CloudProvider, "Existing Google Drive setup found; Google Drive stays the cloud provider");
        }
    }

    public event EventHandler? ActiveChanged;

    public string ActiveKind => options.ForcesLocalTest
        ? CloudProviderKind.LocalTest
        : NormalizeChoice(settings.Current.CloudProvider);

    public IReadOnlyList<string> AvailableKinds => options.Supabase.IsConfigured
        ? [CloudProviderKind.Supabase, CloudProviderKind.GoogleDrive]
        : [CloudProviderKind.GoogleDrive];

    public string ProviderName => Active.ProviderName;

    public CloudProviderCapabilities Capabilities => Active.Capabilities;

    private ICloudProvider Active => ActiveKind switch
    {
        CloudProviderKind.LocalTest => folder,
        CloudProviderKind.GoogleDrive => drive,
        CloudProviderKind.Supabase => supabase.Value,
        _ => notChosen
    };

    public Task SwitchAsync(string kind, CancellationToken cancellationToken = default)
    {
        if (options.ForcesLocalTest)
        {
            throw new InvalidOperationException("The cloud provider is fixed to the local test cloud by appsettings.json.");
        }

        var choice = NormalizeChoice(kind);
        if (choice.Length == 0)
        {
            throw new ArgumentException($"Unknown cloud provider '{kind}'.", nameof(kind));
        }

        if (choice == ActiveKind)
        {
            return Task.CompletedTask;
        }

        settings.Update(current => current.CloudProvider = choice);
        logger.Information(AppLogKeyword.CloudProvider, "Cloud provider switched to {Provider}", choice);
        ActiveChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default) =>
        Active.GetConnectionStatusAsync(game, cancellationToken);

    public Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default) =>
        Active.ConnectAsync(game, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        Active.DisconnectAsync(cancellationToken);

    public Task<CloudWorldManifest?> GetWorldManifestAsync(GameId game, string worldId, CancellationToken cancellationToken = default) =>
        Active.GetWorldManifestAsync(game, worldId, cancellationToken);

    public Task<IReadOnlyList<CloudWorldManifest>> ListWorldManifestsAsync(GameId game, CancellationToken cancellationToken = default) =>
        Active.ListWorldManifestsAsync(game, cancellationToken);

    public Task<CloudSessionLock?> GetSessionLockAsync(GameId game, string worldId, CancellationToken cancellationToken = default) =>
        Active.GetSessionLockAsync(game, worldId, cancellationToken);

    public Task<CloudGroupSnapshot> GetSnapshotAsync(GameId game, CancellationToken cancellationToken = default) =>
        Active.GetSnapshotAsync(game, cancellationToken);

    public Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken = default) =>
        Active.UploadVersionAsync(request, cancellationToken);

    public Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken = default) =>
        Active.DownloadVersionAsync(request, cancellationToken);

    public Task<IReadOnlyList<CloudStoredVersion>> ListStoredVersionsAsync(GameId game, string worldId, CancellationToken cancellationToken = default) =>
        Active.ListStoredVersionsAsync(game, worldId, cancellationToken);

    public Task RemoveVersionAsync(GameId game, string worldId, string archiveFileName, CancellationToken cancellationToken = default) =>
        Active.RemoveVersionAsync(game, worldId, archiveFileName, cancellationToken);

    public Task<CloudRemovalResult> RemoveWorldAsync(GameId game, string worldId, CancellationToken cancellationToken = default) =>
        Active.RemoveWorldAsync(game, worldId, cancellationToken);

    public Task<CloudLockAttempt> TryAcquireSessionLockAsync(GameId game, CloudSessionLock requested, CancellationToken cancellationToken = default) =>
        Active.TryAcquireSessionLockAsync(game, requested, cancellationToken);

    public Task<CloudSessionLock?> HeartbeatSessionLockAsync(GameId game, string worldId, string lockId, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        Active.HeartbeatSessionLockAsync(game, worldId, lockId, ttl, cancellationToken);

    public Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken cancellationToken = default) =>
        Active.ClearSessionLockAsync(game, worldId, lockId, cancellationToken);

    public Task<CloudSetupTestResult> TestSharedFolderAsync(GameId game, string sharedFolderId, CancellationToken cancellationToken = default) =>
        Active is ISharedFolderCloudProvider sharedFolderProvider
            ? sharedFolderProvider.TestSharedFolderAsync(game, sharedFolderId, cancellationToken)
            : Task.FromResult(new CloudSetupTestResult(false, "The active cloud provider does not support shared folder setup."));

    public Task<CloudStorageUsage?> GetStorageUsageAsync(GameId game, CancellationToken cancellationToken = default) =>
        Active is ICloudStorageMaintenance maintenance
            ? maintenance.GetStorageUsageAsync(game, cancellationToken)
            : Task.FromResult<CloudStorageUsage?>(null);

    public Task<CloudUnusedFiles> FindUnusedFilesAsync(GameId game, CancellationToken cancellationToken = default) =>
        Active is ICloudStorageMaintenance maintenance
            ? maintenance.FindUnusedFilesAsync(game, cancellationToken)
            : Task.FromResult(CloudUnusedFiles.None);

    public Task RemoveUnusedFilesAsync(GameId game, CloudUnusedFiles files, CancellationToken cancellationToken = default) =>
        Active is ICloudStorageMaintenance maintenance
            ? maintenance.RemoveUnusedFilesAsync(game, files, cancellationToken)
            : Task.CompletedTask;

    // Only the providers a user can choose; LocalTest comes from the developer override alone.
    private static string NormalizeChoice(string? kind) =>
        string.Equals(kind, CloudProviderKind.GoogleDrive, StringComparison.OrdinalIgnoreCase) ? CloudProviderKind.GoogleDrive
        : string.Equals(kind, CloudProviderKind.Supabase, StringComparison.OrdinalIgnoreCase) ? CloudProviderKind.Supabase
        : string.Empty;
}
