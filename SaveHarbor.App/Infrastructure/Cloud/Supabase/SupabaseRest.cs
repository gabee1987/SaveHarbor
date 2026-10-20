using System.Net.Http;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// Typed calls to the SaveHarbor Cloud database: the functions of supabase/migrations and the few table reads the
// app needs. Every call runs as the signed-in player; ids placed in a URL are checked to be UUIDs first.
public sealed partial class SupabaseRest(SupabaseHttp http, SupabaseAuthClient auth)
{
    public Task<SnapshotDto> GetSnapshotAsync(string groupId, string gameKey, CancellationToken cancellationToken = default) =>
        RpcAsync<SnapshotDto>("get_group_snapshot", new { p_group = groupId, p_game = gameKey }, cancellationToken);

    public Task<VersionDto> PublishVersionAsync(
        string groupId,
        string gameKey,
        string worldId,
        string worldName,
        int expectedLatest,
        PublishVersionDto version,
        CancellationToken cancellationToken = default) =>
        RpcAsync<VersionDto>(
            "publish_world_version",
            new
            {
                p_group = groupId,
                p_game = gameKey,
                p_world_id = worldId,
                p_world_name = worldName,
                p_expected_latest = expectedLatest,
                p_version = version
            },
            cancellationToken);

    // Returns the lock that holds the world afterwards: the caller's own when it won.
    public Task<LockDto> AcquireLockAsync(
        string worldUuid,
        string lockId,
        string deviceId,
        string deviceLabel,
        int basedOnNumber,
        string basedOnId,
        int ttlSeconds,
        CancellationToken cancellationToken = default) =>
        RpcAsync<LockDto>(
            "acquire_session_lock",
            new
            {
                p_world = worldUuid,
                p_lock_id = lockId,
                p_device_id = deviceId,
                p_device_label = deviceLabel,
                p_based_on_number = basedOnNumber,
                p_based_on_id = basedOnId,
                p_ttl_seconds = ttlSeconds
            },
            cancellationToken);

    public Task<LockDto> HeartbeatLockAsync(string worldUuid, string lockId, int ttlSeconds, CancellationToken cancellationToken = default) =>
        RpcAsync<LockDto>("heartbeat_session_lock", new { p_world = worldUuid, p_lock_id = lockId, p_ttl_seconds = ttlSeconds }, cancellationToken);

    // True when the caller's lock was removed, false when it was already gone.
    public Task<bool> ReleaseLockAsync(string worldUuid, string lockId, CancellationToken cancellationToken = default) =>
        RpcAsync<bool>("release_session_lock", new { p_world = worldUuid, p_lock_id = lockId }, cancellationToken);

    public Task RemoveWorldAsync(string worldUuid, CancellationToken cancellationToken = default) =>
        auth.WithAccessTokenAsync(
            (token, callToken) => http.RpcAsync("remove_world", new { p_world = worldUuid }, token, callToken),
            cancellationToken);

    // Newest first; a world keeps at most a few hundred versions.
    public async Task<IReadOnlyList<VersionDto>> ListVersionsAsync(string worldUuid, CancellationToken cancellationToken = default) =>
        await GetAsync<List<VersionDto>>(
            $"rest/v1/world_versions?select=id,group_id,version_number,version_id,archive_path&world_uuid=eq.{RequireUuid(worldUuid)}&order=version_number.desc&limit=500",
            cancellationToken);

    public Task DeleteVersionRowAsync(string versionRowId, CancellationToken cancellationToken = default) =>
        auth.WithAccessTokenAsync(
            (token, callToken) => http.SendAsync(HttpMethod.Delete, $"rest/v1/world_versions?id=eq.{RequireUuid(versionRowId)}", null, token, callToken),
            cancellationToken);

    // Public metadata such as the oldest app version the cloud still serves; readable before sign-in.
    public async Task<IReadOnlyList<AppMetaDto>> GetAppMetaAsync(CancellationToken cancellationToken = default) =>
        await http.SendJsonAsync<List<AppMetaDto>>(HttpMethod.Get, "rest/v1/app_meta?select=key,value", null, null, cancellationToken);

    private Task<T> RpcAsync<T>(string function, object args, CancellationToken cancellationToken) =>
        auth.WithAccessTokenAsync((token, callToken) => http.RpcAsync<T>(function, args, token, callToken), cancellationToken);

    private Task<T> GetAsync<T>(string path, CancellationToken cancellationToken) =>
        auth.WithAccessTokenAsync((token, callToken) => http.SendJsonAsync<T>(HttpMethod.Get, path, null, token, callToken), cancellationToken);

    private static string RequireUuid(string value) =>
        Guid.TryParse(value, out var uuid) ? uuid.ToString("D") : throw new SupabaseException(SupabaseErrorCodes.InvalidData);
}
