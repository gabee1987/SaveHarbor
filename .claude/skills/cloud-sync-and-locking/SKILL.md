---
name: cloud-sync-and-locking
description: SaveHarbor cloud versioning model, manifest/lock JSON contracts, sync-state comparison rules, and known locking weaknesses with their planned fixes. Use when touching CloudSyncService, ICloudProvider implementations, session locks, or upload/download logic.
---

# Cloud Sync and Session Locking

## Model

- One **manifest** per world: `CloudWorldManifest` (`SchemaVersion`, `Provider`, `Game`,
  `WorldId`, `WorldName`, `LatestVersion`, `Retention.KeepLatestVersions=20`, `UpdatedAtUtc`).
- Each upload creates an immutable **version**: zip + sidecar json (`CloudVersionMetadata`:
  monotonically increasing `VersionNumber`, `VersionId = yyyyMMdd_HHmmss_<player>_v<N>`,
  `ArchiveSha256`, `BasedOnVersionNumber/Id`).
- **Local sync state** (`LocalSyncState`, `%LOCALAPPDATA%\SaveHarbor\sync-state\<game>\<provider>\<worldId>.json`,
  provider `googledrive` / `supabase` / `localtest`, because version numbers differ per provider)
  records `LocalBaseVersionNumber` = the cloud version the local files were last downloaded
  from or uploaded as.
- **Session lock** (`CloudSessionLock`, `worlds/<id>/locks/active-session.json`, schema 2): `LockId`,
  `PlayerName`, `MachineName`, `DeviceId` (empty in pre-2.3 locks), `StartedAtUtc`, `LastHeartbeatAtUtc`,
  `BasedOnVersion*`, `ExpiresAtUtc` (= now + `LockTtlSeconds`, 20 min, renewed by the heartbeat), `Status`.

## State resolution (`CloudSyncService.RefreshStatusAsync`)

Order matters:
0. Connection reason `UpdateRequired` (SaveHarbor Cloud's minimum app version), or a manifest/lock whose
   `SchemaVersion` is above `CloudSchema.MaxKnownManifestVersion`/`MaxKnownLockVersion` → `UpdateRequired`. Upload,
   download and starting a session refuse in that state (`UpdateRequiredResult`); ending a session still works.
1. Not connected → `NotConnected`.
2. No manifest/latest → `ConnectedNoCloudSave`.
3. Unexpired lock from another machine → `SomeonePlaying`.
4. Local base null or < latest → `CloudNewer`.
5. Local base > latest → `Conflict`.
6. Else `UpToDate`.

`LocalNewerUploadSafe` exists in the enum but is never produced. Local file modification after
the last sync is **not** detected (only version numbers are compared).

## Invariants (must hold after any change)

- Never restore over local files without a fresh local zip backup first (`RestoreBackupAsync` does this).
- Verify `ArchiveSha256` after download before touching local files.
- Never upload or restore while the game process is running.
- Upload only when `LocalBaseVersionNumber == LatestVersion.VersionNumber` (or no cloud version).
- Only the lock owner may clear a lock: same `DeviceId`, or same `MachineName` for a lock without one
  (`CloudSessionLock.IsHeldBy`). Upload clears own lock.
- Version numbers only increase; never overwrite an existing version file.

## Known weaknesses (fix in `fix/` or within the feature plan, see `docs/plans/dragonwilds/05-*`)

1. **Shared-folder lock acquire is not atomic.** `TryAcquireSessionLockAsync` on Drive and LocalTest
   (`SharedFolderSessionLocks`) writes, waits 1.5 s, and re-reads; the `LockId` left in the file wins. This narrows
   the race but cannot close it. SaveHarbor Cloud acquires atomically on the server.
2. **Heartbeat only while the game runs.** The game-monitor tick calls `HeartbeatSessionAsync`, which reaches the
   cloud once per `HeartbeatIntervalSeconds` (5 min). A session started without launching the game expires after
   20 min.
3. **Pre-2.3 locks are matched by MachineName.** Two Windows users on one PC are told apart only once both run 2.3.
4. **Game close does not upload**: auto-end only clears the lock, so progress is not shared unless
   the user uploads manually. The planned flow uploads then releases.
5. **Stale lock UX**: expired foreign locks are silently ignored; the UI should say
   "<player>'s session expired at <time>, you can take over".

## Multi-game

All cloud paths and local sync-state must be scoped per game. Windrose keeps the legacy
`worlds/` root for backward compatibility; Dragonwilds uses `games/dragonwilds/worlds/`.
See `multi-game-separation`.

## Provider notes

- `GoogleDriveCloudProvider` uses Drive v3 with `SupportsAllDrives`; folder lookups by name.
  Drive has no compare-and-swap for file content — hence the write-then-verify lock strategy.
- `SupabaseCloudProvider` ("SaveHarbor Cloud", `Infrastructure/Cloud/Supabase/`): hand-written client over one
  `SupabaseHttp`; `SupabaseAuthClient` (e-mail code, DPAPI session in `supabase-session.bin`, refresh 60 s before
  expiry, one 401 retry); `SupabaseRest` (RPCs of `supabase/migrations`); `SupabaseStorage` (bucket `saves`, objects
  `<group>/<game>/<worldId>/<versionId>.zip`). `ArchiveFileName` stays `<versionId>.zip`; the provider builds the object
  path from the active group, and `SupabaseMapping` rejects rows whose `archive_path` differs. Upload = storage then
  `publish_world_version` (compare-and-set; refused archive deleted again). Locks are atomic on the server, which also
  refuses a lock whose base is not the latest version. Status reasons: `NotConfigured`, `SignIn`, `Group`,
  `UpdateRequired` (`app_meta.min_supported_app_version`, cached 1 h), `Unreachable`. Group per game in
  `cloud-provider-settings.json` `ActiveGroups`; sign-out clears it.
- Sharing UI: the Sharing page of Settings (`Views/Sharing/SharingView`, `ViewModels/Sharing`), opened by `OpenSharingAsync` in
  `MainWindowViewModel.CloudSetup.cs` (first start, a game without a group once per run, the Sharing view's action,
  Settings › Sharing). `CloudOnboardingViewModel` owns the sign-in steps (e-mail, code, name, group, invite code after
  creating); `SharingGroupViewModel` the group screen (members via `ListMembersAsync`, invite code, storage);
  `SharingProfileViewModel` the one player name (also sent to every group with `SetDisplayNameAsync`), the PC name and
  versions kept. The Sharing view's top line comes from `MainWindowViewModel.Sharing.cs`.
- Storage maintenance (`ICloudStorageMaintenance`, SaveHarbor Cloud only): storage used is summed in the app (the API
  has aggregates switched off); unused archives are objects no `world_versions.archive_path` names and older than one
  hour, so a friend's upload that is still publishing is never removed.
- Version ids use `CloudSyncService.VersionIdPart(player)` (ASCII letters, digits, `_ . -`), valid on every provider.
- `FolderCloudProvider` (`DeveloperProviderOverride: LocalTest`) mirrors the layout under
  `%LOCALAPPDATA%\SaveHarbor\cloud-test` — use it for manual and automated tests.
- Google Drive is an external cloud service; for any use beyond this hobby group it would need
  a privacy/compliance review (personal data: player names, machine names, emails).
