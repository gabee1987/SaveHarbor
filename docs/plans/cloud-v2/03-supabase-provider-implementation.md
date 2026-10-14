# 03 — Supabase Provider Implementation

The C# side of plan 01: a small REST client, an auth session store, the `ICloudProvider` implementation and the group service. No NuGet package for Supabase; one Microsoft package for DPAPI.

## 1. Packages and configuration

| Package | Version | Reason |
|---|---|---|
| `System.Security.Cryptography.ProtectedData` | 10.0.x (match `Microsoft.Extensions.*` patch) | DPAPI for the refresh token; not in the shared framework on .NET 10 **[verify]** with `dotnet build` (if it compiles without the package, drop it) |

`appsettings.json` additions (public values, safe to commit):

```json
"CloudProvider": {
  "DeveloperProviderOverride": "",
  "GoogleAppFolderName": "SaveHarbor",
  "GoogleClientSecretsPath": "Configuration\\google-client-secret.enc",
  "Supabase": {
    "Url": "https://<ref>.supabase.co",
    "PublishableKey": "sb_publishable_...",
    "Bucket": "saves",
    "MaxArchiveBytes": 47185920,
    "LockTtlSeconds": 1200,
    "HttpTimeoutSeconds": 30,
    "TransferTimeoutSeconds": 300
  }
}
```

`CloudProviderOptions` gets a nested `SupabaseOptions` class with these properties; `AppOptionsLoader.LoadCloudProviderOptions` binds it. A missing `Url` or key makes the Supabase provider report "not configured" (like the Drive secret check) instead of throwing.

## 2. Folder layout (`Infrastructure/Cloud/Supabase/`)

| File | Responsibility | Size target |
|---|---|---|
| `SupabaseHttp.cs` | One `HttpClient` (socket handler, `apikey` header, base address), request helpers `GetJsonAsync<T>`, `PostJsonAsync<T>`, `RpcAsync<T>(name, args)`, `DeleteAsync`, consistent error mapping to `SupabaseException(Code, HttpStatus, Message)` | ≤ 250 |
| `SupabaseSessionStore.cs` | Loads/saves `%LOCALAPPDATA%\SaveHarbor\supabase-session.bin` (DPAPI, `DataProtectionScope.CurrentUser`): `access_token`, `refresh_token`, `expires_at`, `email`, `user_id`. Never logs token contents | ≤ 120 |
| `SupabaseAuthClient.cs` | `RequestCodeAsync(email)`, `VerifyCodeAsync(email, code)`, `RefreshIfNeededAsync()` (refreshes 60 s before `expires_at`; single-flight with `SemaphoreSlim`), `SignOutAsync()`. Provides `GetAccessTokenAsync()` for the other clients | ≤ 220 |
| `SupabaseRest.cs` | PostgREST + RPC calls typed for our tables (`GetSnapshotAsync`, `PublishVersionAsync`, `AcquireLockAsync`, `HeartbeatAsync`, `ReleaseAsync`, `ListVersionsAsync`, `DeleteVersionRowAsync`, `RemoveWorldAsync`, `GetAppMetaAsync`, group functions) | ≤ 300, split `SupabaseRest.Groups.cs` if needed |
| `SupabaseStorage.cs` | `UploadAsync(path, filePath, progress)`, `DownloadAsync(path, targetPath, progress)`, `DeleteAsync(paths)`, `ListAsync(prefix)` | ≤ 200 |
| `SupabaseModels.cs` | DTOs with `[JsonPropertyName]` snake_case: `SnapshotDto`, `WorldDto`, `VersionDto`, `LockDto`, `MemberDto`, `GroupSummaryDto`, `SessionDto`, `AppMetaDto`. Mapping to/from `CloudWorldManifest`, `CloudVersionMetadata`, `CloudSessionLock` in `SupabaseMapping.cs` | ≤ 250 |
| `SupabaseCloudProvider.cs` (+ `.Versions.cs`, `.Locks.cs`) | `ICloudProvider` over the above | ≤ 300 each |
| `SupabaseGroupService.cs` | `ICloudGroupService` (plan 02 §4) | ≤ 250 |
| `SupabaseErrorMapper.cs` | RPC codes and HTTP statuses → `AppErrorCode` + user text | ≤ 120 |

All classes `sealed`, registered as singletons in `App.xaml.cs`. `SupabaseHttp` is the only `HttpClient` owner; it is created once (`SocketsHttpHandler { PooledConnectionLifetime = 10 min }`).

## 3. Endpoints used (all under the project URL)

| Purpose | Request |
|---|---|
| Request code | `POST /auth/v1/otp` `{ "email": "...", "create_user": true }` headers `apikey` |
| Verify code | `POST /auth/v1/verify` `{ "type": "email", "email": "...", "token": "123456" }` → session JSON (`access_token`, `refresh_token`, `expires_in`, `user.id`, `user.email`) |
| Refresh | `POST /auth/v1/token?grant_type=refresh_token` `{ "refresh_token": "..." }` |
| Sign out | `POST /auth/v1/logout` bearer |
| RPC | `POST /rest/v1/rpc/<fn>` JSON args, headers `apikey`, `Authorization: Bearer`, `Content-Profile: public`, `Prefer: return=representation` where a row is returned |
| Versions list | `GET /rest/v1/world_versions?select=...&world_uuid=eq.<id>&order=version_number.desc&limit=200` |
| Version row delete | `DELETE /rest/v1/world_versions?id=eq.<id>` |
| App meta | `GET /rest/v1/app_meta?select=key,value` (works with `apikey` only, before sign-in) |
| Upload | `POST /storage/v1/object/saves/<path>` body = file bytes, `Content-Type: application/zip`, `x-upsert: false`, bearer |
| Download | `GET /storage/v1/object/authenticated/saves/<path>` bearer, streamed to a temp file |
| Delete objects | `DELETE /storage/v1/object/saves` `{ "prefixes": ["<path>", ...] }` |
| List objects | `POST /storage/v1/object/list/saves` `{ "prefix": "<group>/<game>/<world>/", "limit": 500 }` (used only by the orphan check in §7) |

Error mapping: `401` → refresh once, then `NotSignedIn`; `400` with a known RPC code → that code; `413`/storage `Payload too large` → `ArchiveTooLarge`; `429` → `RateLimited` with retry-after; `5xx`, `540`, timeouts, DNS failure → `CloudUnreachable` (with the "project may be paused" hint when the host ends in `supabase.co`).

## 4. Upload (`UploadVersionAsync`)

Input: `CloudUploadRequest(World, ArchivePath, VersionMetadata, PreviousManifest)`. `CloudSyncService.Transfers` already computes `VersionNumber = latest + 1`, `VersionId`, SHA-256 and size.

1. Size guard: if `ArchiveSizeBytes > MaxArchiveBytes` → `CloudUploadResult(false, "This world's archive is N MB; the group's cloud allows up to 45 MB.")`. No request is made.
2. Resolve `groupId` for `World.Game` from `ICloudGroupService.GetActiveGroupId`; none → not connected result.
3. Object path: `<groupId>/<game>/<SafeSegment(worldId)>/<VersionId>.zip` (`VersionId` is already `yyyyMMdd_HHmmss_<player>_v<N>` and sanitised by `FileNameSanitizer`; the regex in plan 01 rejects anything else).
4. `SupabaseStorage.UploadAsync` with progress (reported through `IProgress<long>` to the busy text: "Uploading 4.2 MB of 12.8 MB").
5. `publish_world_version(p_expected_latest = PreviousManifest?.LatestVersion?.VersionNumber ?? 0, p_version = {...})`.
   - Success → map the returned row to `CloudVersionMetadata` (server `version_number` equals the client's expectation by construction) and return `CloudUploadResult(true, manifest-with-latest)`.
   - `version_conflict` → delete the just-uploaded object (best effort, logged), return `CloudUploadResult(false, "Someone uploaded a newer version while you were uploading. Download it first.")` with state `CloudNewer`.
6. Retention runs in `CloudSyncService.Retention` as today (`ListStoredVersionsAsync` → `RemoveVersionAsync`); Supabase's `RemoveVersionAsync` deletes the object, then the row. If the object delete fails, the row stays (no orphan rows); if the row delete fails after the object is gone, log and continue (the next retention pass removes the row; download of that version is impossible anyway and the UI shows "archive missing").

Orphan objects (upload succeeded, RPC failed, delete failed) are tolerated; §7 adds a Settings action "Clean up unused files" that lists objects under the group prefix and deletes those without a version row.

## 5. Session locks

`TryAcquireSessionLockAsync` → `acquire_session_lock(world_uuid, lockId, playerName, deviceId, deviceLabel, basedOnNumber, basedOnId, LockTtlSeconds)`; `Acquired = returned.lock_id == lockId`. `HeartbeatSessionLockAsync` → `heartbeat_session_lock`; `lock_lost` → returns null and `CloudSyncService` reports "Your session lock was lost (another player took over after it expired). Your upload will be checked against the newest version." `ClearSessionLockAsync` → `release_session_lock`. `GetSessionLockAsync` reads from the snapshot (one RPC) rather than a separate query.

World uuid lookup: the snapshot includes `id` per world; `SupabaseCloudProvider` caches `worldId → uuid` per group and game; an upload of a world the snapshot does not know yet goes through `publish_world_version` (which upserts the world) and then refreshes the cache.

## 6. Snapshot and status

`GetSnapshotAsync(game)` → `get_group_snapshot(groupId, gameKey)` → `CloudGroupSnapshot`. Mapping: world row → `CloudWorldManifest { SchemaVersion = 2, Provider = "Supabase", Game = key, WorldId, WorldName, LatestVersion = latest row or null, Retention.KeepLatestVersions = keep_latest_versions, UpdatedAtUtc }`; lock row → `CloudSessionLock { SchemaVersion = 2, LockId, WorldId, PlayerName, MachineName = device_label, DeviceId, StartedAtUtc, LastHeartbeatAtUtc, BasedOn*, ExpiresAtUtc, Status = "Playing" }`. `ServerTimeUtc` lets `CloudSyncService` evaluate lock expiry against server time (clock skew on a friend's PC no longer matters; today's `DateTimeOffset.UtcNow > ExpiresAtUtc` becomes `snapshot.ServerTimeUtc > ExpiresAtUtc` with a fallback to local time for Drive).

`GetConnectionStatusAsync`: configured? session present and refreshable? group chosen for this game? → `CloudConnectionStatus(true, "Supabase", email, "Signed in as <email> · group <name>")`. `ConnectAsync` for Supabase does **not** open a dialog (unlike Drive's OAuth); it returns a result with `NeedsSignIn = true` and the view model opens the sign-in dialog (plan 04 §2). Add `bool NeedsSetup` to `CloudConnectionResult` for this.

## 7. Removal, retention, orphan clean-up

- `RemoveWorldAsync`: `ListVersionsAsync` → delete all objects in one `DELETE /storage/v1/object/saves` call (chunks of 100) → `remove_world(uuid)`. `CloudRemovalResult` reports counts. Supabase has no recycle bin: `CloudSyncService.RemoveCloudWorldAsync` already keeps a local backup of the latest version first (keep that), and the confirm dialog text says "This cannot be undone" when `!Capabilities.HasRecycleBin`.
- `RemoveVersionAsync(game, worldId, archiveFileName)`: resolve the row by `version_id` (archive name without `.zip`), delete the object, delete the row.
- Orphan clean-up (Settings → Sharing → "Clean up unused files"): list objects under `<group>/<game>/`, subtract `archive_path`s of all versions in the group, delete the rest after confirmation. Also shows storage used (from `archive_size_bytes`) and the 1 GB free-plan figure.

## 8. Security rules in code

- Tokens: never logged, never in error texts, never in `CloudSyncStatus`. `SupabaseSessionStore` file is DPAPI-protected and `FileAttributes.Hidden | NotContentIndexed`.
- Every path segment that comes from the cloud (`archive_path`, `world_id`, `version_id`) passes `SafePath.IsSafeSegment` before being used in a local path or a request URL; the existing path-traversal rules from plan `dragonwilds/04 §7` apply unchanged.
- Downloads are verified by SHA-256 before restore (existing `CloudSyncService.Transfers` logic, unchanged).
- Request bodies are size-bounded; responses are deserialised with `MaxDepth = 16` and a 4 MB cap for JSON.
- The provider never calls `service_role` endpoints; there is no admin path in the app.

## 9. Logging

New `AppLogKeyword.CloudAuth` (sign-in flow, never with tokens or codes) and reuse `CloudProvider`, `CloudUpload`, `CloudDownload`, `CloudSession`. Add `CloudAuth: Information` to `appsettings.json` `KeywordMinimumLevels`. Each request logs method, path (no query values that contain ids of other users), status and elapsed ms at `Debug`.
