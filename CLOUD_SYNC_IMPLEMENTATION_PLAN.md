# SaveHarbor Cloud Sync Implementation Plan

This plan describes the robust cloud sync design for SaveHarbor. The goal is a simple user experience with strong protection against save corruption, accidental overwrites, and confusing multi-player timing problems.

The first real cloud provider will be Google Drive, but the sync logic must not depend directly on Google Drive. Provider-specific code should sit behind interfaces so a different storage backend can be added later.

## Product Goals

- Keep one shared cloud world as the source of truth.
- Make the normal flow easy:
  - check latest before playing
  - download if needed
  - play
  - upload after closing the game
- Prevent accidental overwrite if another player uploaded while someone was playing.
- Warn users when another player appears to be in an active session.
- Never merge saves automatically. Windrose world data is a RocksDB database snapshot, not mergeable text.
- Always preserve a local backup before replacing a local world.
- Keep cloud backups/version history, but keep the normal UI centered on one current cloud save.

## Core Decision

SaveHarbor syncs complete ZIP snapshots, not loose RocksDB files.

Reason:

- RocksDB files belong together.
- Partial file upload/download can produce broken database state.
- ZIP + manifest + SHA-256 gives a clean integrity boundary.
- Upload can be made publish-safe by updating the cloud manifest only after the archive is fully uploaded and verified.

## Cloud Storage Model

The cloud provider stores a SaveHarbor root folder.

Suggested Google Drive structure:

```text
SaveHarbor/
  manifest.json
  worlds/
    C8320961717B4D7C459CB58190797ECC/
      manifest.json
      current/
        current.zip
        current.json
      versions/
        2026-05-26T201530Z_Gabee_v11.zip
        2026-05-26T201530Z_Gabee_v11.json
      locks/
        active-session.json
      backups/
        manual/
        conflict/
```

Important rule:

- `current/manifest` or world `manifest.json` is the published state.
- A new upload is not considered available until the manifest points to it.
- Old versions stay available for recovery.

## Cloud Manifest

Each world has one cloud manifest.

Suggested `manifest.json`:

```json
{
  "schemaVersion": 1,
  "provider": "GoogleDrive",
  "game": "Windrose",
  "worldId": "C8320961717B4D7C459CB58190797ECC",
  "worldName": "Broodwake",
  "latestVersion": {
    "versionNumber": 11,
    "versionId": "2026-05-26T201530Z_Gabee_v11",
    "uploadedAtUtc": "2026-05-26T20:15:30Z",
    "uploadedBy": "Gabee",
    "uploaderMachine": "GABEE-PC",
    "archiveFileName": "2026-05-26T201530Z_Gabee_v11.zip",
    "archiveSha256": "...",
    "archiveSizeBytes": 8401234,
    "sourceWorldModifiedAtUtc": "2026-05-26T20:10:00Z",
    "basedOnVersionNumber": 10,
    "basedOnVersionId": "2026-05-26T184455Z_Peti_v10"
  },
  "retention": {
    "keepLatestVersions": 20
  },
  "updatedAtUtc": "2026-05-26T20:15:35Z"
}
```

Version numbers are simple increasing integers. Version IDs are unique names used for files and logs.

## Local Sync State

SaveHarbor must store local sync state per world.

Suggested path:

```text
%LocalAppData%\SaveHarbor\sync-state\{worldId}.json
```

Suggested contents:

```json
{
  "schemaVersion": 1,
  "worldId": "C8320961717B4D7C459CB58190797ECC",
  "worldName": "Broodwake",
  "localWorldPath": "C:\\Users\\Gabee\\AppData\\Local\\R5\\Saved\\...",
  "lastKnownCloudVersionNumber": 11,
  "lastKnownCloudVersionId": "2026-05-26T201530Z_Gabee_v11",
  "localBaseVersionNumber": 11,
  "localBaseVersionId": "2026-05-26T201530Z_Gabee_v11",
  "lastDownloadedAtUtc": "2026-05-26T20:20:00Z",
  "lastUploadedAtUtc": null,
  "lastLocalBackupPath": "C:\\Users\\Gabee\\AppData\\Local\\SaveHarbor\\backups\\...",
  "lastCloudCheckAtUtc": "2026-05-26T20:21:00Z"
}
```

The critical field is `localBaseVersionNumber`.

Upload is only safe when:

```text
localBaseVersionNumber == cloud.latestVersion.versionNumber
```

If not equal, the app must enter conflict state and block normal upload.

## Session Lock

Session lock is a UX safety signal. It warns that someone probably started playing from a specific cloud version.

It is not the final authority. The final authority is still the based-on-version check.

Suggested `active-session.json`:

```json
{
  "schemaVersion": 1,
  "lockId": "6df0f07f-2f4a-4c59-9fd9-8ff0cf2ec987",
  "worldId": "C8320961717B4D7C459CB58190797ECC",
  "playerName": "Gabee",
  "machineName": "GABEE-PC",
  "startedAtUtc": "2026-05-26T19:30:00Z",
  "lastHeartbeatAtUtc": "2026-05-26T20:05:00Z",
  "basedOnVersionNumber": 10,
  "basedOnVersionId": "2026-05-26T184455Z_Peti_v10",
  "expiresAtUtc": "2026-05-27T01:30:00Z",
  "status": "Playing"
}
```

Lock behavior:

- Created when user starts a play session from SaveHarbor.
- Also created when user downloads latest and chooses `Start playing`.
- Updated by heartbeat while SaveHarbor is open.
- Expired automatically after a fixed timeout if heartbeat stops.
- Cleared after successful upload.
- Cleared when user clicks `End session without upload`.
- If stale, shown as stale instead of blocking forever.

Recommended initial timeout:

- Heartbeat every 60 seconds.
- Active if last heartbeat is under 10 minutes old.
- Stale warning from 10 minutes to 6 hours.
- Expired after 6 hours.

## UX States

The UI should always show one clear cloud state.

### Not Connected

Meaning:

- No Google Drive connection exists yet.

Actions:

- `Connect Google Drive`

Toast examples:

- Info: `Connect cloud sync to check for shared saves.`

### Connected, No Cloud Save

Meaning:

- Google Drive is connected but this world has no cloud manifest yet.

Actions:

- `Upload current as first cloud save`

Toast examples:

- Info: `No cloud save found for Broodwake yet.`

### Up To Date

Meaning:

- Local base version equals cloud latest version.
- Local files have no newer local changes detected, or user has not played since download/upload.

Actions:

- `Check latest`
- `Upload current` if local modified after base
- `Create cloud backup`

Toast examples:

- Success: `Broodwake is up to date.`

### Cloud Newer

Meaning:

- Cloud latest version is newer than local base version.
- Local world has not changed since its base version, or the app can safely treat it as replaceable after backup.

Actions:

- `Download latest`

Safety:

- Always create local backup before restore.

Toast examples:

- Info: `Peti uploaded a newer save 24 minutes ago.`

### Local Newer, Upload Safe

Meaning:

- Local world changed after the last downloaded/uploaded base.
- Cloud latest version is still the same base version.

Actions:

- `Upload current`

Toast examples:

- Info: `Local save has changes ready to upload.`
- Success: `Uploaded Broodwake version 12.`

### Someone Playing

Meaning:

- Active lock exists from another player.

Actions:

- `Check latest`
- `Download latest` can be allowed only with a clear warning.
- `Upload current` follows version rules.

Recommended UX:

- Show status chip: `Peti is playing`
- Show detail: `Started 18 minutes ago from version 11`
- If user tries to download:
  - show confirmation: `Peti appears to be playing. This cloud save may not include their current progress yet.`

Toast examples:

- Warning: `Peti appears to be playing Broodwake.`

### Conflict

Meaning:

- Local save is based on older cloud version.
- Cloud changed while this user likely played or had local changes.

Example:

```text
Local base: version 10
Cloud latest: version 11 uploaded by Peti
```

Actions:

- `Back up my local save`
- `Download cloud latest`
- `Cancel`

No `Upload mine as alternate save` in the main UX.

Safety:

- Normal upload disabled.
- Download creates a named conflict backup first.
- Conflict backup must be easy to find in the backups section.

Toast examples:

- Error/Warning: `Conflict detected. Peti uploaded a newer save while your local save was based on version 10.`

### Auth Error

Meaning:

- Saved Google token no longer works.

Actions:

- `Reconnect Google Drive`

Toast examples:

- Warning: `Google Drive connection expired. Please reconnect.`

## Main User Workflows

### First Cloud Setup

1. User opens SaveHarbor.
2. UI shows `Cloud sync not connected`.
3. User clicks `Connect Google Drive`.
4. Browser opens Google OAuth.
5. User chooses personal Google account.
6. SaveHarbor stores refresh token for this Windows user.
7. SaveHarbor creates or finds the `SaveHarbor` Drive folder.
8. UI shows connected account email.
9. User uploads current world as first cloud save.

Done when:

- App restart does not ask for login again.
- `Check latest` works after restart.

### Normal Before Playing

1. User opens SaveHarbor.
2. App checks cloud automatically if connected.
3. If cloud newer, user clicks `Download latest`.
4. App downloads ZIP to temp.
5. App verifies size and SHA-256.
6. App creates local pre-download backup.
7. App restores downloaded world.
8. App updates local sync state.
9. User clicks `Start playing` or starts Windrose manually.
10. App creates session lock.

Done when:

- Friend opening the app sees this player as active.

### Normal After Playing

1. User closes Windrose.
2. SaveHarbor detects game is closed or user clicks refresh.
3. User clicks `Upload current`.
4. App checks latest cloud manifest first.
5. If cloud latest still equals local base version, upload continues.
6. App creates local backup.
7. App creates ZIP.
8. App computes SHA-256.
9. App uploads ZIP and metadata.
10. App updates cloud manifest last.
11. App updates local sync state.
12. App clears session lock.

Done when:

- Other players see the new cloud version.

### Player B Opens While Player A Plays

1. Player A downloads version 11.
2. Player A starts playing.
3. SaveHarbor creates lock for Player A based on version 11.
4. Player B opens SaveHarbor.
5. App reads cloud manifest and lock.
6. UI says `Player A is playing`.
7. Download action warns that latest cloud save may not include Player A's current progress.

Expected behavior:

- No confusing block if Player B only wants to inspect status.
- Clear warning if Player B wants to play anyway.
- If Player B downloads and plays anyway, the later upload conflict is still caught by version checks.

### Conflict Scenario

1. Cloud latest is version 10.
2. Player A downloads version 10 and plays.
3. Player B downloads version 10 and plays.
4. Player B uploads version 11.
5. Player A tries to upload.
6. App checks cloud manifest.
7. App sees Player A local base is version 10 but cloud latest is version 11.
8. App blocks upload.
9. App shows conflict panel/dialog.
10. Player A can create conflict backup and download version 11.

Done when:

- Player A's local progress is preserved in a conflict backup.
- Cloud current save remains version 11.
- No automatic overwrite occurs.

## Cloud Abstraction Design

Do not put Google Drive logic into the view model.

Suggested interfaces:

```csharp
public interface ICloudProvider
{
    string ProviderName { get; }
    Task<CloudConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken);
    Task<CloudConnectionResult> ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task<CloudWorldManifest?> GetWorldManifestAsync(string worldId, CancellationToken cancellationToken);
    Task<CloudSessionLock?> GetSessionLockAsync(string worldId, CancellationToken cancellationToken);
    Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken cancellationToken);
    Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken cancellationToken);
    Task WriteSessionLockAsync(CloudSessionLock sessionLock, CancellationToken cancellationToken);
    Task ClearSessionLockAsync(string worldId, string lockId, CancellationToken cancellationToken);
}
```

Suggested higher-level service:

```csharp
public interface ICloudSyncService
{
    Task<CloudSyncStatus> RefreshStatusAsync(WindroseWorld world, CancellationToken cancellationToken);
    Task<CloudSyncResult> DownloadLatestAsync(WindroseWorld world, CancellationToken cancellationToken);
    Task<CloudSyncResult> UploadCurrentAsync(WindroseWorld world, CancellationToken cancellationToken);
    Task<CloudSyncResult> StartSessionAsync(WindroseWorld world, CancellationToken cancellationToken);
    Task<CloudSyncResult> EndSessionAsync(WindroseWorld world, CancellationToken cancellationToken);
}
```

Responsibility split:

- `ICloudProvider`: raw cloud storage/auth operations.
- `ICloudSyncService`: SaveHarbor sync rules, conflicts, lock handling, manifest comparison.
- View model: UI state and commands only.

## Google Drive Provider Plan

### Authentication

- Use OAuth installed-app flow.
- Request Drive permissions only as broad as needed.
- Store refresh token for current Windows user.
- Do not require login on every startup.
- Reconnect only when token is missing/revoked.

Open question to decide during implementation:

- Use full Drive scope for simpler folder/file management, or app-specific scope if it fits the UX.

### Folder Discovery

On first connection:

- Find or create root folder `SaveHarbor`.
- Find or create world folder by `worldId`.
- Store Drive folder IDs in settings.

Settings should store:

```json
{
  "cloudProvider": "GoogleDrive",
  "googleDrive": {
    "accountEmail": "gabee@example.com",
    "rootFolderId": "...",
    "worldFolderIds": {
      "C8320961717B4D7C459CB58190797ECC": "..."
    }
  }
}
```

### Upload Safety

Upload sequence:

1. Refresh cloud manifest.
2. Validate upload is safe.
3. Create local backup.
4. Create ZIP in temp folder.
5. Compute SHA-256.
6. Upload archive as version file.
7. Upload sidecar metadata.
8. Re-read uploaded metadata if practical.
9. Update manifest last.
10. Clear matching session lock.
11. Update local sync state.

If any step before manifest update fails:

- Cloud latest remains unchanged.
- Show error toast.
- Leave temp/local backup for recovery.

If manifest update succeeds but local state update fails:

- On next refresh, app can recover by comparing cloud manifest and latest local upload metadata.

### Download Safety

Download sequence:

1. Refresh cloud manifest.
2. If active lock from another user exists, warn.
3. Download archive to temp.
4. Verify size.
5. Compute and verify SHA-256.
6. Block if Windrose is running.
7. Create pre-download local backup.
8. Replace local world folder.
9. Update local sync state to downloaded cloud version.
10. Show success toast.

If verification fails:

- Delete temp archive.
- Do not touch local world.
- Show error toast.

## UI Plan

Add a compact cloud status section with clear state.

Fields:

- Provider: `Google Drive`
- Account: `gabee@example.com`
- Latest cloud version: `v11`
- Uploaded by: `Peti`
- Uploaded at: `Today 20:15`
- Local base: `v11`
- Session: `Gabee is playing` or `No active session`
- State: `Up to date`, `Cloud newer`, `Upload ready`, `Conflict`

Buttons:

- `Connect Google Drive`
- `Check latest`
- `Download latest`
- `Upload current`
- `Start session`
- `End session`
- `Create cloud backup`

Button behavior:

- Disable dangerous buttons while Windrose is running.
- Disable `Upload current` in conflict state.
- Show a specific tooltip for disabled buttons.
- Use toasts for success/info/errors.
- Use dialogs only for confirmation, conflict resolution, and reconnect.

## Toast Messaging Rules

Use toast for:

- Cloud connected
- Cloud check completed
- New cloud save found
- Download completed
- Upload completed
- Session lock created
- Session lock cleared
- Someone appears to be playing
- Conflict detected
- Google connection expired
- Network error
- Hash verification failure

Use modal dialog for:

- First Google connection browser prompt explanation.
- Download while another player is active.
- Conflict resolution.
- Restore/download overwrite confirmation if needed.
- Serious error with recovery instructions.

Toast text should name the world and player where useful.

Examples:

- `Cloud checked: Broodwake is up to date.`
- `Peti appears to be playing Broodwake since 20:15.`
- `Download complete: Broodwake v12 restored. Local backup created first.`
- `Upload blocked: cloud is now v12 from Peti, but your save is based on v11.`
- `Upload complete: Broodwake v13 is now the latest cloud save.`

## Implementation Phases

### Phase 1 - Cloud Domain Models

- [x] Add cloud manifest models.
- [x] Add cloud version metadata model.
- [x] Add cloud session lock model.
- [x] Add local sync state model.
- [x] Add cloud sync status enum.
- [ ] Add conflict result model.

Done when:

- Models serialize/deserialize cleanly.
- No Google Drive references exist in these domain models.

### Phase 2 - Local Sync State Service

- [x] Create `ILocalSyncStateService`.
- [x] Save per-world sync state under `%LocalAppData%\SaveHarbor\sync-state`.
- [x] Load missing state gracefully.
- [ ] Update state after backup/download/upload.
- [x] Display local base version in UI.

Done when:

- Restarting SaveHarbor preserves local base version.

### Phase 3 - Cloud Provider Interfaces

- [x] Create `ICloudProvider`.
- [x] Create `ICloudSyncService`.
- [x] Create request/result types.
- [x] Register interfaces in DI.
- [x] Keep UI connected to `ICloudSyncService`, not Google Drive.

Done when:

- The app compiles with cloud abstractions but no real cloud provider yet.

### Phase 4 - Test Provider

- [ ] Implement a folder-backed provider for development tests.
- [ ] Store manifest/versions/locks in a normal local folder.
- [ ] Use this provider to test sync rules without Google auth.
- [ ] Hide this provider from normal users or mark it as developer-only.

Done when:

- Two local test profiles/folders can simulate two players.
- Conflict detection can be tested without Google Drive.

### Phase 5 - Sync Rule Engine

- [ ] Implement `RefreshStatusAsync`.
- [ ] Implement safe upload check:
  - [ ] allow first upload if no cloud manifest
  - [ ] allow upload if local base equals cloud latest
  - [ ] block upload if cloud latest changed
- [ ] Implement safe download check.
- [ ] Implement active/stale/expired lock interpretation.
- [ ] Implement conflict status.

Done when:

- Unit or manual test matrix proves state transitions are correct.

### Phase 6 - UI Cloud State

- [x] Replace cloud placeholder actions with real state panel.
- [x] Show connected/not connected state.
- [x] Show latest version/player/time.
- [x] Show active session lock.
- [x] Show local base version.
- [x] Add disabled-state tooltips.
- [ ] Add toasts for all major cloud events.

Done when:

- User can understand what is safe before pressing a button.

### Phase 7 - Download Latest

- [ ] Download ZIP through provider.
- [ ] Verify SHA-256.
- [ ] Create pre-download backup.
- [ ] Restore world folder.
- [ ] Update local sync state.
- [ ] Toast success/failure.
- [ ] Block while Windrose is running.

Done when:

- Download cannot corrupt or overwrite without a backup.

### Phase 8 - Upload Current

- [ ] Refresh cloud manifest immediately before upload.
- [ ] Block conflict uploads.
- [ ] Create local backup.
- [ ] Create ZIP.
- [ ] Compute SHA-256.
- [ ] Upload archive and metadata.
- [ ] Publish manifest last.
- [ ] Update local sync state.
- [ ] Clear own lock.

Done when:

- Upload cannot silently overwrite a newer cloud save.

### Phase 9 - Session Lock

- [ ] Add `Start session`.
- [ ] Add `End session`.
- [ ] Auto-create lock after download if user chooses to play.
- [ ] Add heartbeat timer while SaveHarbor is open.
- [ ] Show other player's active/stale lock.
- [ ] Clear own lock after successful upload.
- [ ] Handle app close gracefully by leaving lock to expire.

Done when:

- Player B sees that Player A appears to be playing.
- Stale lock does not trap everyone forever.

### Phase 10 - Google Drive Provider

- [ ] Add Google Drive API packages.
- [ ] Implement OAuth connect.
- [ ] Store token securely for current Windows user.
- [ ] Find/create root folder.
- [ ] Find/create world folder.
- [ ] Implement manifest read/write.
- [ ] Implement version upload/download.
- [ ] Implement lock read/write/clear.
- [ ] Handle auth/network/rate-limit errors.

Done when:

- Same SaveHarbor build syncs between two machines through one personal Google Drive account or shared Drive access.

### Phase 11 - Cloud Backups

- [ ] Keep all normal uploaded versions initially.
- [ ] Add visible version count/storage estimate.
- [ ] Add manual `Create cloud backup` action if useful.
- [ ] Add retention setting later, default keep latest 20.
- [ ] Never delete latest version.

Done when:

- User can recover a previous cloud version without normal sync becoming confusing.

### Phase 12 - Multi-PC Test Pass

- [ ] Test PC A first upload.
- [ ] Test PC B download.
- [ ] Test PC B upload.
- [ ] Test PC A sees cloud newer.
- [ ] Test active lock warning.
- [ ] Test stale lock behavior.
- [ ] Test conflict when both play from same base.
- [ ] Test game running blocks download/upload.
- [ ] Test network failure during upload before manifest publish.
- [ ] Test bad hash blocks restore.

Done when:

- No test path silently loses local progress.

## Manual Test Matrix

### Basic

- [ ] No cloud connection.
- [ ] Connect Google Drive.
- [ ] Restart app, no login required.
- [ ] First upload.
- [ ] Check latest.
- [ ] Download latest.
- [ ] Upload current.

### Lock

- [ ] Player A starts session.
- [ ] Player B sees active lock.
- [ ] Player A uploads and lock clears.
- [ ] Player A closes app without upload and lock becomes stale.
- [ ] Expired lock does not block normal sync.

### Conflict

- [ ] Both players start from v1.
- [ ] Player B uploads v2.
- [ ] Player A upload is blocked.
- [ ] Player A conflict backup is created.
- [ ] Player A downloads v2.
- [ ] Cloud remains v2.

### Failure

- [ ] Windrose running blocks restore/download.
- [ ] Windrose running blocks upload.
- [ ] Network disconnect during upload does not publish manifest.
- [ ] Missing archive in cloud gives clear error.
- [ ] Hash mismatch blocks restore.
- [ ] Token revoked shows reconnect state.

## Non-Goals For First Cloud Version

- No automatic merge.
- No alternate branch upload in normal UI.
- No dedicated server behavior.
- No real-time collaboration.
- No fully automatic restore/upload without user confirmation.
- No support for multiple cloud providers in the UI until Google Drive works well.

## Recommended First Build Scope

The first useful cloud build should include:

- Google Drive connect with saved login.
- Cloud manifest.
- Upload current.
- Download latest.
- Local base version tracking.
- Conflict blocking.
- Active session lock warning.
- Robust toasts and disabled-state tooltips.
- Local backup before every restore/download.

Cloud backups/history can be basic at first: keep uploaded versions and show latest. A nicer backup browser can come after the core sync flow is proven with two PCs.
