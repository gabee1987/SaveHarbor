using System.Text.RegularExpressions;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// Turns SaveHarbor Cloud rows into the app's cloud model. Rows come from friends' apps, so every name that later
// becomes part of a local path or a request URL is checked here; a row that fails throws invalid_data.
public static partial class SupabaseMapping
{
    public const string ProviderName = "Supabase";

    // The database's own rules for world and version ids (supabase/migrations).
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 _.()-]{0,127}$")]
    public static partial Regex WorldIdPattern();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,160}$")]
    public static partial Regex VersionIdPattern();

    public static bool IsValidWorldId(string? worldId) =>
        worldId is not null && WorldIdPattern().IsMatch(worldId) && SafePath.IsSafeSegment(worldId);

    // Archives live at <group>/<game>/<world>/<version>.zip; the app never reads one from anywhere else.
    public static string ObjectPath(string groupId, string gameKey, string worldId, string archiveFileName) =>
        $"{groupId}/{gameKey}/{worldId}/{archiveFileName}";

    public static CloudWorldManifest ToManifest(WorldDto world, string gameKey)
    {
        RequireWorld(world);
        return new CloudWorldManifest
        {
            SchemaVersion = 2,
            Provider = ProviderName,
            Game = gameKey,
            WorldId = world.WorldId,
            WorldName = string.IsNullOrWhiteSpace(world.WorldName) ? world.WorldId : world.WorldName,
            LatestVersion = world.Latest is null ? null : ToVersion(world.Latest, gameKey, world.WorldId),
            Retention = new CloudRetentionSettings { KeepLatestVersions = world.KeepLatestVersions },
            UpdatedAtUtc = world.UpdatedAt
        };
    }

    public static CloudVersionMetadata ToVersion(VersionDto version, string gameKey, string worldId)
    {
        if (!VersionIdPattern().IsMatch(version.VersionId)
            || version.VersionNumber <= 0
            || !Guid.TryParse(version.GroupId, out _)
            || version.ArchivePath != ObjectPath(version.GroupId, gameKey, worldId, version.VersionId + ".zip"))
        {
            throw new SupabaseException(SupabaseErrorCodes.InvalidData);
        }

        return new CloudVersionMetadata
        {
            VersionNumber = version.VersionNumber,
            VersionId = version.VersionId,
            UploadedAtUtc = version.CreatedAt,
            UploadedBy = version.UploaderName,
            UploaderMachine = version.UploaderDevice,
            ArchiveFileName = version.VersionId + ".zip",
            ArchiveSha256 = version.ArchiveSha256,
            ArchiveSizeBytes = version.ArchiveSizeBytes,
            SourceWorldModifiedAtUtc = version.SourceWorldModifiedAt ?? default,
            BasedOnVersionNumber = version.BasedOnVersionNumber,
            BasedOnVersionId = version.BasedOnVersionId
        };
    }

    public static CloudSessionLock ToLock(LockDto sessionLock, string worldId) => new()
    {
        SchemaVersion = 2,
        LockId = sessionLock.LockId,
        WorldId = worldId,
        PlayerName = sessionLock.PlayerName,
        MachineName = sessionLock.DeviceLabel,
        DeviceId = sessionLock.DeviceId,
        StartedAtUtc = sessionLock.StartedAt,
        LastHeartbeatAtUtc = sessionLock.LastHeartbeatAt,
        BasedOnVersionNumber = sessionLock.BasedOnVersionNumber,
        BasedOnVersionId = sessionLock.BasedOnVersionId,
        ExpiresAtUtc = sessionLock.ExpiresAt,
        Status = "Playing"
    };

    public static CloudMember ToMember(MemberDto member) => new(member.UserId, member.DisplayName, member.Role);

    public static CloudGroupSummary ToGroup(GroupSummaryDto group)
    {
        if (!Guid.TryParse(group.Id, out _))
        {
            throw new SupabaseException(SupabaseErrorCodes.InvalidData);
        }

        return new CloudGroupSummary(group.Id, group.Name, group.MyRole ?? "member", group.MemberCount, group.CreatedAt);
    }

    // A world that fails the checks is left out (and reported through skipped), so one bad row cannot hide the
    // group's other worlds.
    public static CloudGroupSnapshot ToSnapshot(SnapshotDto snapshot, string gameKey, Action<string>? skipped = null)
    {
        var manifests = new List<CloudWorldManifest>(snapshot.Worlds.Count);
        var locks = new Dictionary<string, CloudSessionLock>(StringComparer.OrdinalIgnoreCase);
        foreach (var world in snapshot.Worlds)
        {
            try
            {
                manifests.Add(ToManifest(world, gameKey));
                if (world.Lock is not null)
                {
                    locks[world.WorldId] = ToLock(world.Lock, world.WorldId);
                }
            }
            catch (SupabaseException)
            {
                skipped?.Invoke(world.Id);
            }
        }

        return new CloudGroupSnapshot(snapshot.ServerTime, manifests, locks, snapshot.Members.Select(ToMember).ToArray());
    }

    private static void RequireWorld(WorldDto world)
    {
        if (!Guid.TryParse(world.Id, out _) || !IsValidWorldId(world.WorldId))
        {
            throw new SupabaseException(SupabaseErrorCodes.InvalidData);
        }
    }
}
