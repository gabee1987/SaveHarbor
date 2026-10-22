# Plan 04 — Per-Game Data and Cloud Scoping, Including Migration

**Branch:** `feature/dragonwilds-support`
**Depends on:** 02. **Blocks:** shipping Dragonwilds (03 may be developed in parallel against LocalTest).
**Skills:** `multi-game-separation`, `cloud-sync-and-locking`, `coding-standards`.

## 1. Objective

Guarantee that Windrose and Dragonwilds data can never mix, locally or in the cloud, and migrate existing Windrose users without data loss or a cloud migration.

## 2. Local layout

```
%LOCALAPPDATA%\SaveHarbor\
  app-settings.json                      (plan 02)
  cloud-provider-settings.json           (per-game shared folders, §3)
  google-drive-token\                    (shared – account, not game data)
  logs\                                  (shared files; every line enriched with {Game})
  backups\windrose\*.zip
  backups\dragonwilds\*.zip
  sync-state\windrose\<worldId>.json
  sync-state\dragonwilds\<worldId>.json
  cloud-test\windrose\...                (LocalTest provider root per game)
  cloud-test\dragonwilds\...
```

Changes:
- `IAppDataPathProvider`: add `GetBackupRoot(GameId)`, `GetSyncStateRoot(GameId)`, `GetLocalTestCloudRoot(GameId)`. Remove the hardcoded roots in `ZipBackupService` (lines 14–17) and `LocalJsonSyncStateService` (lines 15–18), which duplicate the path provider today.
- `LocalJsonSyncStateService.GetStatePath(world)` → `Path.Combine(GetSyncStateRoot(world.Game), safeId + ".json")`.
- `ZipBackupService.ListBackupsAsync(GameId)` lists only that game's folder.
- Serilog: push a `Game` property via `LogContext.PushProperty` from the VM operation runner, and add `[{Game}]` to the output template.

## 3. Cloud scoping

### 3.1 One shared folder per game

`cloud-provider-settings.json` v2:
```json
{
  "schemaVersion": 2,
  "sharedFolders": {
    "windrose": "TEST_FOLDER_ID_A",
    "dragonwilds": "TEST_FOLDER_ID_B"
  }
}
```
- `CloudProviderOptions.GoogleSharedFolderId` → `GetSharedFolderId(GameId)`.
- `ICloudSetupService` methods take a `GameId`. The setup dialog title includes the game name.
- `ICloudProvider` gains a scoping call: either `UseGame(IGameDefinition)`, or each method takes the game. **Recommendation:** pass `GameId` explicitly in the request records and lookups (`GetWorldManifestAsync(GameId, worldId)`). This is explicit and stateless, with no hidden "current" state in a singleton provider.
- `GoogleDriveCloudProvider` caches the root folder id **per game** (dictionary), not as a single field.
- Inside each folder the layout is unchanged: `worlds/<worldId>/manifest.json | versions/ | locks/`.

### 3.2 Folder identity marker

At the shared root: `saveharbor-game.json`
```json
{ "schemaVersion": 1, "game": "dragonwilds", "createdBy": "Player1", "createdAtUtc": "2026-10-05T12:00:00Z" }
```
- `TestSharedFolderAsync(game, id)`:
  - Marker for another game → fail with "This folder is used for Windrose. Choose a separate folder for Dragonwilds."
  - No marker, and the folder contains `worlds/` → accept for Windrose only (legacy) and write the marker on the first successful save. For Dragonwilds → fail ("folder already contains another game's worlds").
  - No marker, empty folder → accept and write the marker.
- On every `GetConnectionStatusAsync`, the marker is validated (cached per session) so that a misconfigured settings file cannot cause cross-game writes.
- Same rule in `FolderCloudProvider`.

### 3.3 Manifest and archive validation

- `CloudWorldManifest.Game` = storage key. Reading a manifest whose `Game` differs from the requested game throws `InvalidDataException` → `AppErrorCode.InvalidData`.
- Legacy manifests contain `"Game": "Windrose"` (capitalised). Compare **case-insensitively**.
- `BackupManifest.Game` is validated the same way in `ReadManifestAsync` and in import.

## 4. Migration (Windrose, one-time, idempotent)

New `Infrastructure/Migrations/LegacyLayoutMigrator.cs`, run in `App.OnStartup` before the VM initialises. It is synchronous, fast, and logs each action.

| Legacy | Target | Rule |
|---|---|---|
| `backups\*.zip` (top level only) | `backups\windrose\` | `File.Move` within the same volume; skip if the target exists (log) |
| `sync-state\*.json` (top level) | `sync-state\windrose\` | same; additionally rewrite `LocalWorldPath` only if it does not exist (no-op normally) |
| `cloud-provider-settings.json` v1 (`GoogleSharedFolderId`) | v2 `sharedFolders.windrose` | write a temp file and then `File.Replace` with a `.bak` backup |
| `cloud-test\worlds\` | `cloud-test\windrose\worlds\` | directory move |

- Marker: `%LOCALAPPDATA%\SaveHarbor\migrations.json` `{ "legacyLayoutV1": "2026-10-05T12:00:00Z" }`. If it is present, skip.
- Any failure → log an error, continue starting, show a single warning toast, and do **not** delete anything.
- Friends on old builds: the cloud layout is unchanged and old manifests stay readable. New builds write `Game: "windrose"` (lowercase). An old build compares `"Windrose"` case-insensitively when reading backups, but **must be checked**: `ZipBackupService.ReadManifestAsync` uses `OrdinalIgnoreCase` (good). The cloud manifest's `Game` is not validated by old builds (good). Conclusion: mixed versions inside a Windrose group stay compatible. Still, recommend that everyone updates.

## 5. Step order

1. Path provider methods, plus refactoring the backup and sync-state services to use them, with the per-game roots.
2. Migrator plus tests on a temp directory (legacy fixture → migrated layout; second run is a no-op).
3. Cloud settings v2, setup service per game, and the setup dialog per game.
4. Provider `GameId` parameters, per-game root cache, and the marker logic in both providers.
5. Manifest and backup game validation.
6. Logging `{Game}` enrichment.

## 6. Acceptance criteria

- [ ] With a legacy fixture layout, the first start migrates; the second start does nothing; no files are lost (file counts and hashes compared in the test).
- [ ] Configuring the Windrose folder for Dragonwilds is refused with a clear message, and vice versa.
- [ ] A Dragonwilds zip cannot be imported or restored while Windrose is active (error, no file changes).
- [ ] The cloud-status query for game A never issues Drive requests against game B's folder (verified with a fake provider in tests).
- [ ] An existing Windrose user's Drive folder works without reconfiguration.

## 7. Security and privacy notes

- The marker file and manifests contain player display names (`Environment.UserName`) and machine names. These are already present today. Do not add more personal data (no emails in cloud files).
- **Path traversal (existing defect, fix here or on a separate `fix/` branch first):** values from shared JSON are combined into local paths without validation:
  - `CloudSyncService.Transfers.cs` `DownloadLatestAvailableAsync`: `Path.Combine(profile.WorldsPath, manifest.WorldId)`.
  - `CloudSyncService.Transfers.cs` (both downloads): the temp path contains `LatestVersion.ArchiveFileName`.
  - `ZipBackupService.ImportBackupAsNewWorldAsync`: `Path.Combine(profile.WorldsPath, manifest.WorldId)` from the zip's manifest.

  Any member of the shared folder (or a tampered zip) could set `WorldId = "..\\..\\..\\Something"` and make SaveHarbor write outside the save directory. Fix this with a single helper, `SafePath.CombineUnderRoot(root, untrustedSegment)`. It rejects segments that change under `FileNameSanitizer`, contain separators, or are `.`/`..`, and it asserts that `Path.GetFullPath(result)` starts with `Path.GetFullPath(root) + separator`. Note that `ZipFile.ExtractToDirectory` already rejects zip-slip entries on .NET Core, so archive *contents* are safe; only these path *names* are at risk.
