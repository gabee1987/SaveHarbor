using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

public sealed partial class SupabaseCloudProvider
{
    private const int MinLockTtlSeconds = 60;
    private const int MaxLockTtlSeconds = 3600;

    public async Task<CloudSessionLock?> GetSessionLockAsync(GameId game, string worldId, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(game, cancellationToken)).LocksByWorldId.GetValueOrDefault(worldId);

    // The database decides: it stores the request when the world is free, the lock expired, or this device holds it,
    // and returns whichever lock holds the world afterwards.
    public async Task<CloudLockAttempt> TryAcquireSessionLockAsync(GameId game, CloudSessionLock requested, CancellationToken cancellationToken = default)
    {
        var worldUuid = await RequireWorldUuidAsync(game, requested.WorldId, cancellationToken);
        var row = await rest.AcquireLockAsync(
            worldUuid,
            requested.LockId,
            requested.DeviceId,
            requested.MachineName,
            requested.BasedOnVersionNumber,
            requested.BasedOnVersionId,
            TtlSeconds(requested.ExpiresAtUtc - requested.StartedAtUtc),
            cancellationToken);

        var current = SupabaseMapping.ToLock(row, requested.WorldId);
        var acquired = Guid.TryParse(current.LockId, out var holder) && Guid.TryParse(requested.LockId, out var mine) && holder == mine;
        return new CloudLockAttempt(acquired, current);
    }

    public async Task<CloudSessionLock?> HeartbeatSessionLockAsync(GameId game, string worldId, string lockId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var worldUuid = await RequireWorldUuidAsync(game, worldId, cancellationToken);
        try
        {
            return SupabaseMapping.ToLock(await rest.HeartbeatLockAsync(worldUuid, lockId, TtlSeconds(ttl), cancellationToken), worldId);
        }
        catch (SupabaseException exception) when (exception.Code == SupabaseErrorCodes.LockLost)
        {
            return null;
        }
    }

    public async Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken cancellationToken = default)
    {
        if (groups.GetActiveGroupId(game) is { } groupId
            && await ResolveWorldUuidAsync(game, groupId, worldId, cancellationToken) is { } worldUuid
            && !await rest.ReleaseLockAsync(worldUuid, lockId, cancellationToken))
        {
            logger.Debug(AppLogKeyword.CloudSession, "Session lock of world {WorldId} was already gone", worldId);
        }
    }

    private async Task<string> RequireWorldUuidAsync(GameId game, string worldId, CancellationToken cancellationToken)
    {
        var groupId = groups.GetActiveGroupId(game) ?? throw new SupabaseException(SupabaseErrorCodes.NotMember);
        return await ResolveWorldUuidAsync(game, groupId, worldId, cancellationToken)
            ?? throw new SupabaseException(SupabaseErrorCodes.WorldMissing);
    }

    // The database accepts 1 to 60 minutes.
    private static int TtlSeconds(TimeSpan ttl) => (int)Math.Clamp(ttl.TotalSeconds, MinLockTtlSeconds, MaxLockTtlSeconds);
}
