using System.Text.Json.Serialization;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// JSON shapes of the SaveHarbor Cloud API (supabase/migrations). Unknown fields are ignored, so the server may add
// columns without breaking older apps.

public sealed class SnapshotDto
{
    [JsonPropertyName("server_time")] public DateTimeOffset ServerTime { get; set; }
    [JsonPropertyName("members")] public List<MemberDto> Members { get; set; } = [];
    [JsonPropertyName("worlds")] public List<WorldDto> Worlds { get; set; } = [];
}

public sealed class MemberDto
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
}

public sealed class WorldDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("group_id")] public string GroupId { get; set; } = string.Empty;
    [JsonPropertyName("game")] public string Game { get; set; } = string.Empty;
    [JsonPropertyName("world_id")] public string WorldId { get; set; } = string.Empty;
    [JsonPropertyName("world_name")] public string WorldName { get; set; } = string.Empty;
    [JsonPropertyName("latest_version_number")] public int LatestVersionNumber { get; set; }
    [JsonPropertyName("keep_latest_versions")] public int KeepLatestVersions { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("latest")] public VersionDto? Latest { get; set; }
    [JsonPropertyName("lock")] public LockDto? Lock { get; set; }
}

public sealed class VersionDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("world_uuid")] public string WorldUuid { get; set; } = string.Empty;
    [JsonPropertyName("group_id")] public string GroupId { get; set; } = string.Empty;
    [JsonPropertyName("version_number")] public int VersionNumber { get; set; }
    [JsonPropertyName("version_id")] public string VersionId { get; set; } = string.Empty;
    [JsonPropertyName("uploader_name")] public string UploaderName { get; set; } = string.Empty;
    [JsonPropertyName("uploader_device")] public string UploaderDevice { get; set; } = string.Empty;
    [JsonPropertyName("archive_path")] public string ArchivePath { get; set; } = string.Empty;
    [JsonPropertyName("archive_sha256")] public string ArchiveSha256 { get; set; } = string.Empty;
    [JsonPropertyName("archive_size_bytes")] public long ArchiveSizeBytes { get; set; }
    [JsonPropertyName("source_world_modified_at")] public DateTimeOffset? SourceWorldModifiedAt { get; set; }
    [JsonPropertyName("based_on_version_number")] public int BasedOnVersionNumber { get; set; }
    [JsonPropertyName("based_on_version_id")] public string BasedOnVersionId { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LockDto
{
    [JsonPropertyName("world_uuid")] public string WorldUuid { get; set; } = string.Empty;
    [JsonPropertyName("lock_id")] public string LockId { get; set; } = string.Empty;
    [JsonPropertyName("user_id")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("player_name")] public string PlayerName { get; set; } = string.Empty;
    [JsonPropertyName("device_id")] public string DeviceId { get; set; } = string.Empty;
    [JsonPropertyName("device_label")] public string DeviceLabel { get; set; } = string.Empty;
    [JsonPropertyName("started_at")] public DateTimeOffset StartedAt { get; set; }
    [JsonPropertyName("last_heartbeat_at")] public DateTimeOffset LastHeartbeatAt { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    [JsonPropertyName("based_on_version_number")] public int BasedOnVersionNumber { get; set; }
    [JsonPropertyName("based_on_version_id")] public string BasedOnVersionId { get; set; } = string.Empty;
}

public sealed class GroupSummaryDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("member_count")] public int MemberCount { get; set; }
    [JsonPropertyName("my_role")] public string? MyRole { get; set; }
}

public sealed class AppMetaDto
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
}

// The version fields publish_world_version reads from p_version.
public sealed class PublishVersionDto
{
    [JsonPropertyName("version_id")] public string VersionId { get; set; } = string.Empty;
    [JsonPropertyName("archive_path")] public string ArchivePath { get; set; } = string.Empty;
    [JsonPropertyName("archive_sha256")] public string ArchiveSha256 { get; set; } = string.Empty;
    [JsonPropertyName("archive_size_bytes")] public long ArchiveSizeBytes { get; set; }
    [JsonPropertyName("uploader_device")] public string UploaderDevice { get; set; } = string.Empty;
    [JsonPropertyName("source_world_modified_at")] public DateTimeOffset? SourceWorldModifiedAt { get; set; }
    [JsonPropertyName("based_on_version_number")] public int BasedOnVersionNumber { get; set; }
    [JsonPropertyName("based_on_version_id")] public string BasedOnVersionId { get; set; } = string.Empty;
}

public sealed class AuthSessionDto
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("expires_at")] public long? ExpiresAt { get; set; }
    [JsonPropertyName("user")] public AuthUserDto? User { get; set; }
}

public sealed class AuthUserDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string? Email { get; set; }
}
