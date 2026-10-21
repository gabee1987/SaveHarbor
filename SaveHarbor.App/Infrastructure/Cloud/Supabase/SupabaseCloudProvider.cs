using System.Collections.Concurrent;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// SaveHarbor Cloud: worlds shared within a group on the Supabase project. Versions are compare-and-set and locks are
// taken atomically by the database, so two players can never both win.
public sealed partial class SupabaseCloudProvider : ICloudProvider
{
    public const string DisplayName = "SaveHarbor Cloud";
    private const string MinimumVersionKey = "min_supported_app_version";
    private static readonly TimeSpan AppMetaLifetime = TimeSpan.FromHours(1);

    private readonly SupabaseOptions options;
    private readonly SupabaseAuthClient auth;
    private readonly SupabaseRest rest;
    private readonly SupabaseStorage storage;
    private readonly ICloudGroupService groups;
    private readonly IAppLogger logger;
    private readonly TimeProvider time;
    private readonly Version appVersion;

    // World row ids by group, game and world id, filled from every snapshot and upload.
    private readonly ConcurrentDictionary<string, string> worldUuids = new(StringComparer.OrdinalIgnoreCase);
    private (DateTimeOffset FetchedAt, Version? Minimum)? appMeta;

    // appVersion: tests pass one; the app uses its own assembly version.
    public SupabaseCloudProvider(
        SupabaseOptions options,
        SupabaseAuthClient auth,
        SupabaseRest rest,
        SupabaseStorage storage,
        ICloudGroupService groups,
        IAppLogger logger,
        TimeProvider time,
        Version? appVersion = null)
    {
        this.options = options;
        this.auth = auth;
        this.rest = rest;
        this.storage = storage;
        this.groups = groups;
        this.logger = logger;
        this.time = time;
        this.appVersion = appVersion ?? typeof(SupabaseCloudProvider).Assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    public string ProviderName => DisplayName;

    public CloudProviderCapabilities Capabilities => new(
        HasSharedFolders: false,
        HasGroups: true,
        HasAtomicLocks: true,
        SupportsHeartbeat: true,
        SupportsRealtime: true,
        HasRecycleBin: false,
        MaxArchiveBytes: options.MaxArchiveBytes,
        DefaultRetentionCount: 10);

    // Checked in the order a player fixes them: configured, signed in, group chosen, app new enough, token usable.
    public async Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured)
        {
            return Status("SaveHarbor Cloud is not set up in this version of the app.", CloudConnectionReason.NotConfigured);
        }

        if (!auth.IsSignedIn)
        {
            return Status("Sign in to share worlds with your group.", CloudConnectionReason.SignIn, needsSetup: true);
        }

        if (groups.GetActiveGroupId(game) is not { } groupId)
        {
            return Status($"Choose or create a group to share {game} worlds.", CloudConnectionReason.Group, needsSetup: true);
        }

        try
        {
            if (await GetMinimumVersionAsync(cancellationToken) is { } minimum && Normalize(appVersion) < minimum)
            {
                return Status($"SaveHarbor Cloud needs SaveHarbor {minimum} or newer. Update the app to keep sharing.", CloudConnectionReason.UpdateRequired);
            }

            await auth.GetAccessTokenAsync(cancellationToken);
        }
        catch (SupabaseException exception) when (exception.Code == SupabaseErrorCodes.NotSignedIn)
        {
            return Status("Your sign-in expired. Sign in again to continue.", CloudConnectionReason.SignIn, needsSetup: true);
        }
        catch (SupabaseException exception)
        {
            logger.Warning(AppLogKeyword.CloudProvider, "SaveHarbor Cloud status check failed: {Code}", exception.Code);
            return Status(SupabaseErrorMapper.Describe(exception).Message, CloudConnectionReason.Unreachable);
        }

        var groupName = groups.KnownGroups.FirstOrDefault(group => group.Id == groupId)?.Name;
        return new CloudConnectionStatus(
            true,
            DisplayName,
            auth.Email,
            groupName is null ? $"Signed in as {auth.Email}." : $"Signed in as {auth.Email} · group {groupName}.");
    }

    // Signing in needs the sign-in dialog, which the caller opens when the status asks for setup.
    public async Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken cancellationToken = default)
    {
        var status = await GetConnectionStatusAsync(game, cancellationToken);
        return new CloudConnectionResult(status.IsConnected, status, status.IsConnected ? "SaveHarbor Cloud connected." : status.Message);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => groups.SignOutAsync(cancellationToken);

    public async Task<CloudGroupSnapshot> GetSnapshotAsync(GameId game, CancellationToken cancellationToken = default)
    {
        if (groups.GetActiveGroupId(game) is not { } groupId)
        {
            return new CloudGroupSnapshot(null, [], new Dictionary<string, CloudSessionLock>(), []);
        }

        var gameKey = game.ToStorageKey();
        var dto = await rest.GetSnapshotAsync(groupId, gameKey, cancellationToken);
        var snapshot = SupabaseMapping.ToSnapshot(dto, gameKey, worldUuid =>
            logger.Warning(AppLogKeyword.CloudSync, "SaveHarbor Cloud world {WorldUuid} has invalid data and is not shown", worldUuid));

        var valid = snapshot.Manifests.Select(manifest => manifest.WorldId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var world in dto.Worlds.Where(world => valid.Contains(world.WorldId)))
        {
            worldUuids[UuidKey(groupId, gameKey, world.WorldId)] = world.Id;
        }

        return snapshot;
    }

    public async Task<CloudWorldManifest?> GetWorldManifestAsync(GameId game, string worldId, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(game, cancellationToken)).Manifests
            .FirstOrDefault(manifest => string.Equals(manifest.WorldId, worldId, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<CloudWorldManifest>> ListWorldManifestsAsync(GameId game, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(game, cancellationToken)).Manifests;

    private CloudConnectionStatus Status(string message, string reason, bool needsSetup = false) =>
        new(false, DisplayName, auth.Email, message, needsSetup, reason);

    private async Task<Version?> GetMinimumVersionAsync(CancellationToken cancellationToken)
    {
        if (appMeta is { } cached && time.GetUtcNow() - cached.FetchedAt < AppMetaLifetime)
        {
            return cached.Minimum;
        }

        var value = (await rest.GetAppMetaAsync(cancellationToken)).FirstOrDefault(entry => entry.Key == MinimumVersionKey)?.Value;
        var minimum = Version.TryParse(value, out var parsed) ? Normalize(parsed) : null;
        appMeta = (time.GetUtcNow(), minimum);
        return minimum;
    }

    // 2.3 and 2.3.0.0 compare equal to 2.3.0.
    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    // The world's row id, from the cache or a fresh snapshot. Null when the world is not in the group's cloud.
    private async Task<string?> ResolveWorldUuidAsync(GameId game, string groupId, string worldId, CancellationToken cancellationToken)
    {
        var key = UuidKey(groupId, game.ToStorageKey(), worldId);
        if (worldUuids.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await GetSnapshotAsync(game, cancellationToken);
        return worldUuids.GetValueOrDefault(key);
    }

    private static string UuidKey(string groupId, string gameKey, string worldId) => $"{groupId}|{gameKey}|{worldId}";
}
