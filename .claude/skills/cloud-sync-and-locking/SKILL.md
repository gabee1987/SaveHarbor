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
- **Local sync state** (`LocalSyncState`, `%LOCALAPPDATA%\SaveHarbor\sync-state\<worldId>.json`)
  records `LocalBaseVersionNumber` = the cloud version the local files were last downloaded
  from or uploaded as.
- **Session lock** (`CloudSessionLock`, `worlds/<id>/locks/active-session.json`): `LockId`,
  `PlayerName`, `MachineName`, `StartedAtUtc`, `LastHeartbeatAtUtc`, `BasedOnVersion*`,
  `ExpiresAtUtc` (= start + 6 h), `Status`.

## State resolution (`CloudSyncService.RefreshStatusAsync`)

Order matters:
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
- Only the lock owner (same `MachineName`) may clear a lock. Upload clears own lock.
- Version numbers only increase; never overwrite an existing version file.

## Known weaknesses (fix in `fix/` or within the feature plan, see `docs/plans/dragonwilds/05-*`)

1. **Non-atomic lock acquire** (read → write). Two friends pressing Play within seconds can both
   "win". Mitigation without server CAS: write lock with new `LockId`, wait ~1.5–3 s, re-read; if
   `LockId` differs, the other writer won → back off and report. Tie-break deterministically
   (earliest `StartedAtUtc`, then lowest `LockId`).
2. **No heartbeat**: `LastHeartbeatAtUtc` is never refreshed; sessions > 6 h appear expired and
   can be stolen. Refresh heartbeat from the existing game-monitor tick (throttled, e.g. every
   5 min) and extend `ExpiresAtUtc` to now + short TTL (e.g. 20 min) once heartbeats exist.
3. **Lock identity = MachineName**: same user on two PCs is fine, but two users on one PC collide.
   Acceptable for this group; document, don't over-engineer.
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
- `FolderCloudProvider` (`Provider: LocalTest`) mirrors the layout under
  `%LOCALAPPDATA%\SaveHarbor\cloud-test` — use it for manual and automated tests.
- Google Drive is an external cloud service; for any use beyond this hobby group it would need
  a privacy/compliance review (personal data: player names, machine names, emails).
