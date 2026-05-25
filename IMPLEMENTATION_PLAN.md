# SaveHarbor Implementation Plan

SaveHarbor is a small Windows WPF helper for sharing a Windrose co-op world between friends without running a paid dedicated server. The app should keep the manual workflow simple: check who has the newest world, download before playing, upload after playing, and protect everyone from overwriting newer progress by accident.

## Project Goals

- Detect the local Windrose save location.
- Read and display world metadata from `WorldDescription.json`.
- Package the complete world save safely into a portable archive.
- Restore a downloaded world archive safely with backups.
- Sync one shared world through cloud storage.
- Minimize manual steps while keeping overwrite and corruption risks visible.
- Keep the app small, understandable, and maintainable.

## Current Technical Baseline

- App type: WPF desktop app.
- Target framework: `.NET 8`, `net8.0-windows`.
- MVVM package: `CommunityToolkit.Mvvm`.
- Dependency injection/configuration packages are already referenced.
- Logging package: `Serilog` with file sink.
- Existing folders:
  - `Application`
  - `Configuration`
  - `Domain`
  - `Infrastructure`
  - `Logs`
  - `Models`
  - `Services`
  - `ViewModels`
  - `Views`

## Windrose Save Structure Notes

Known local world path example:

```text
C:\Users\Gabee\AppData\Local\R5\Saved\SaveProfiles\76561197970909442\RocksDB\0.10.0\Worlds\C8320961717B4D7C459CB58190797ECC\
```

Observed files indicate the world is stored as a RocksDB database. The `.sst` files are not certificates; they are RocksDB sorted string table data files and must be treated as core save data. The `MANIFEST`, `CURRENT`, `OPTIONS`, identity, log, and other RocksDB files are also part of the database state.

Initial sharing rule:

- Share the whole world directory as one unit.
- Do not cherry-pick only `.json` or `.sst` files.
- Do not copy while the game/server is actively writing to the database.
- Exclude only files proven to be transient and unsafe to copy, after testing.
- Assume RocksDB files belong together unless evidence shows otherwise.

Files likely important:

- `WorldDescription.json`: readable world metadata.
- `*.sst`: main RocksDB persisted table files.
- `MANIFEST-*`: RocksDB database metadata.
- `CURRENT`: pointer to active manifest.
- `OPTIONS-*`: RocksDB options snapshot.
- `IDENTITY`: database identity.
- `LOG*`: RocksDB log/info files. Include at first; later we can decide if these are optional.
- `*.log` or WAL-like files if present: include at first because they may be needed for recovery.

Files to handle carefully:

- `LOCK`: indicates the database may be open. Do not include it in archives unless testing proves it is harmless. More importantly, if `LOCK` exists and the game process is running, block backup/restore.

## Safety Principles

- Never restore over a local save without creating a timestamped local backup first.
- Never upload if the local save appears older than the cloud manifest unless the user explicitly confirms.
- Never download/restore while Windrose or its server process is running.
- Never upload while Windrose or its server process is running.
- Store enough metadata with every cloud version to identify who uploaded it, when, from which world, and what files/hash were included.
- Prefer clear blocking messages over clever automatic conflict resolution.

## Planned Local App Data

SaveHarbor should keep its own settings and history outside the game save folder.

Suggested app data path:

```text
%LocalAppData%\SaveHarbor\
```

Suggested contents:

- `settings.json`
- `logs\saveharbor-.log`
- `backups\`
- `temp\`
- `sync-cache\`

## Cloud Storage Strategy

Start with a provider abstraction even if Google Drive is the first implementation.

Initial cloud layout:

```text
SaveHarbor/
  Windrose/
    worlds/
      C8320961717B4D7C459CB58190797ECC/
        manifest.json
        versions/
          2026-05-25T181530Z_Gabee.zip
```

Cloud `manifest.json` should describe the newest known uploaded version:

```json
{
  "schemaVersion": 1,
  "game": "Windrose",
  "worldId": "C8320961717B4D7C459CB58190797ECC",
  "worldName": "Broodwake",
  "versionId": "2026-05-25T181530Z_Gabee",
  "uploadedAtUtc": "2026-05-25T18:15:30Z",
  "uploadedBy": "Gabee",
  "archiveFileName": "2026-05-25T181530Z_Gabee.zip",
  "archiveSha256": "...",
  "archiveSizeBytes": 123456,
  "sourceSavePath": "C:\\Users\\Gabee\\AppData\\Local\\R5\\Saved\\...",
  "note": ""
}
```

Conflict behavior:

- If cloud has no manifest, allow first upload.
- If cloud manifest is newer than local last-known cloud version, warn before upload.
- If local and cloud both changed since last sync, show conflict and require manual choice.
- Do not auto-merge worlds. The save is a database, not mergeable source text.

## Main User Workflow

### First Run

1. App starts.
2. Detect possible Windrose save profile folders under:

```text
%LocalAppData%\R5\Saved\SaveProfiles\
```

3. Find worlds under:

```text
...\RocksDB\0.10.0\Worlds\
```

4. Read `WorldDescription.json`.
5. Let user select the world if multiple are found.
6. Ask for display name used in sync metadata.
7. Ask user to configure cloud provider.
8. Save settings.

### Before Playing

1. User opens SaveHarbor.
2. App checks game/server process state.
3. App checks cloud manifest.
4. If cloud version is newer, show `Download latest`.
5. On download:
   - Download zip to temp.
   - Verify SHA-256.
   - Create local backup of current world.
   - Replace world directory.
   - Update local sync state.
6. User launches Windrose manually or via app if launch support is added.

### After Playing

1. User closes Windrose/server.
2. App detects the game is closed or user clicks `Refresh`.
3. App validates the world folder is not locked.
4. App creates a zip archive from the world folder.
5. App computes SHA-256.
6. App compares local state with cloud manifest.
7. If safe, upload archive and update cloud manifest.
8. App records upload history locally.

## UI Plan

Keep the first version as one practical dashboard, not a multi-page app.

Main window sections:

- Current world
  - World name
  - World ID
  - Local save path
  - Last local modified time
- Cloud status
  - Connected provider
  - Latest cloud version
  - Uploaded by
  - Uploaded at
- Safety status
  - Game running or closed
  - Save lock status
  - Last backup
- Primary actions
  - `Check Cloud`
  - `Download Latest`
  - `Upload Current`
  - `Create Backup`
  - `Restore Backup`
- Activity log
  - Recent operations and errors.

UX rules:

- Disable dangerous actions while the game appears to be running.
- Show the exact world name and upload owner before restore/upload.
- Keep confirmation dialogs short but explicit.
- Make safe actions one-click; make destructive actions two-step.

## Architecture Plan

### Domain

Suggested files:

- `Domain/WindroseWorld.cs`
- `Domain/WorldDescription.cs`
- `Domain/WorldSettings.cs`
- `Domain/WorldArchiveManifest.cs`
- `Domain/CloudManifest.cs`
- `Domain/SyncState.cs`
- `Domain/BackupInfo.cs`
- `Domain/OperationResult.cs`

Responsibilities:

- Represent worlds, manifests, backup entries, and sync status.
- Keep domain classes free from WPF dependencies.

### Services

Suggested interfaces:

- `IWindroseSaveDiscoveryService`
- `IWorldDescriptionReader`
- `IProcessDetectionService`
- `IWorldArchiveService`
- `IBackupService`
- `IHashService`
- `ICloudStorageService`
- `ISyncService`
- `ISettingsService`
- `IDialogService`

Responsibilities:

- Put filesystem, zip, process, cloud, and settings work behind interfaces.
- Keep view models focused on state and commands.

### Infrastructure

Suggested implementations:

- `Infrastructure/WindroseSaveDiscoveryService.cs`
- `Infrastructure/WorldDescriptionReader.cs`
- `Infrastructure/WindowsProcessDetectionService.cs`
- `Infrastructure/ZipWorldArchiveService.cs`
- `Infrastructure/FileBackupService.cs`
- `Infrastructure/Sha256HashService.cs`
- `Infrastructure/LocalJsonSettingsService.cs`
- `Infrastructure/GoogleDriveCloudStorageService.cs`

### ViewModels

Suggested files:

- `ViewModels/MainWindowViewModel.cs`
- `ViewModels/WorldSummaryViewModel.cs`
- `ViewModels/CloudStatusViewModel.cs`
- `ViewModels/ActivityLogItemViewModel.cs`

Use `CommunityToolkit.Mvvm`:

- `ObservableObject`
- `[ObservableProperty]`
- `[RelayCommand]`

### Views

Suggested files:

- `Views/MainView.xaml`
- `Views/SettingsDialog.xaml`
- `Views/BackupRestoreDialog.xaml`
- `Views/ConflictDialog.xaml`

The current `MainWindow.xaml` can host `Views/MainView.xaml`, or we can keep the first version directly in `MainWindow.xaml` until the UI grows.

## Implementation Phases

### Phase 0 - Repository and Build Housekeeping

- [ ] Confirm `.git` exists in `C:\Coding\SaveHarbor`.
- [ ] If not, initialize Git and commit the current scaffold.
- [ ] Confirm `bin` and `obj` are ignored.
- [ ] Run `dotnet build`.
- [ ] Add this implementation plan to source control.

Done when:

- Build succeeds.
- `git status` is clean except intentional working changes.

### Phase 1 - Local Save Discovery

- [ ] Add models for `WindroseWorld` and `WorldDescription`.
- [ ] Implement save profile discovery under `%LocalAppData%\R5\Saved\SaveProfiles`.
- [ ] Detect world folders under `RocksDB\0.10.0\Worlds`.
- [ ] Parse `WorldDescription.json`.
- [ ] Display discovered world name, ID, and path in the UI.
- [ ] Add useful logging for discovery failures.

Done when:

- The app can find and display the `Broodwake` world without hardcoding the full path.

### Phase 2 - Local Backup and Restore

- [ ] Implement complete world folder zip creation.
- [ ] Exclude `LOCK` from the archive.
- [ ] Include a SaveHarbor archive manifest inside the zip.
- [ ] Compute SHA-256 for archives.
- [ ] Create backups under `%LocalAppData%\SaveHarbor\backups`.
- [ ] Implement restore from backup.
- [ ] Always create a pre-restore backup.
- [ ] Block backup/restore when Windrose appears to be running.

Done when:

- User can create a backup zip and restore it successfully.
- Restore creates a safety backup before replacing files.

### Phase 3 - Local Sync State

- [ ] Add `settings.json` for user display name, selected world, and cloud settings.
- [ ] Add `sync-state.json` for last downloaded/uploaded version.
- [ ] Show local sync state in the UI.
- [ ] Track last successful local backup.
- [ ] Add conflict detection logic independent of any cloud provider.

Done when:

- The app can tell whether local state is aligned with the last known cloud version.

### Phase 4 - Cloud Provider Abstraction

- [ ] Define `ICloudStorageService`.
- [ ] Add operations:
  - [ ] Connect/authenticate.
  - [ ] Read manifest.
  - [ ] Upload archive.
  - [ ] Download archive.
  - [ ] Update manifest.
  - [ ] List versions.
- [ ] Add a local-folder cloud provider for testing before Google Drive.
- [ ] Test sync behavior against a normal folder.

Done when:

- The app can upload/download through the interface using a local test folder.

### Phase 5 - Google Drive Sync

- [ ] Choose Google Drive API package and authentication flow.
- [ ] Store tokens securely enough for a small personal tool.
- [ ] Create or locate the SaveHarbor app folder in Drive.
- [ ] Upload version zip.
- [ ] Download version zip.
- [ ] Read and update cloud manifest.
- [ ] Handle network/auth errors clearly.

Done when:

- Two Windows machines can exchange the same world through Google Drive.

### Phase 6 - Safety and Conflict Handling

- [ ] Detect common Windrose process names after testing.
- [ ] Detect save folder lock/write state.
- [ ] Compare local archive hash with manifest where possible.
- [ ] Add clear conflict dialog:
  - [ ] Download cloud version.
  - [ ] Upload anyway after confirmation.
  - [ ] Create local backup only.
  - [ ] Cancel.
- [ ] Keep old cloud versions instead of overwriting the only archive.
- [ ] Add a retention setting later if storage becomes a problem.

Done when:

- The app prevents accidental overwrite during normal two-player conflict scenarios.

### Phase 7 - UX Polish

- [ ] Replace default `MainWindow` title.
- [ ] Add clean dashboard layout.
- [ ] Add progress state for long operations.
- [ ] Add cancellation where practical.
- [ ] Add status badges for safe/blocked actions.
- [ ] Add tray notification or Windows toast only if useful.
- [ ] Add optional button to open local save folder.
- [ ] Add optional button to open backup folder.

Done when:

- A friend can use the app without understanding RocksDB or folder paths.

### Phase 8 - Packaging

- [ ] Add publish profile or script.
- [ ] Decide framework-dependent vs self-contained publish.
- [ ] Produce a portable zip build.
- [ ] Verify app starts on another Windows machine.
- [ ] Document first-run setup.

Done when:

- The app can be sent to friends as a small usable package.

## Testing Plan

Manual test worlds:

- [ ] Empty or fresh world.
- [ ] Real `Broodwake` world.
- [ ] Missing `WorldDescription.json`.
- [ ] Multiple save profiles.
- [ ] Multiple worlds.
- [ ] Game running during backup.
- [ ] Game running during restore.
- [ ] Cloud version newer than local.
- [ ] Local version newer than cloud.
- [ ] Both local and cloud changed.

Automated tests to add when practical:

- [ ] Parse `WorldDescription.json`.
- [ ] Discover worlds from a fake directory tree.
- [ ] Create archive with expected files.
- [ ] Exclude `LOCK`.
- [ ] Verify hash mismatch blocks restore/download.
- [ ] Conflict detection matrix.
- [ ] Settings read/write.

## Open Questions

- What exact Windrose process names should block sync?
- Does Windrose keep the RocksDB `LOCK` file after clean shutdown?
- Are log/WAL files needed for a valid restore in all cases?
- Does copying the world folder while the game is at the main menu corrupt anything?
- Should SaveHarbor launch Windrose, or should it only manage saves?
- Should every friend upload under their own display name or machine name?
- Should Google Drive authentication be per user, or should all players use one shared folder/account?

## Early Development Order

Recommended next tasks:

1. Fix repository state if `.git` is missing.
2. Implement local world discovery.
3. Display `WorldDescription.json` data in the WPF UI.
4. Implement local zip backup.
5. Implement restore with pre-restore backup.
6. Add local-folder cloud provider.
7. Add sync manifest and conflict checks.
8. Add Google Drive provider.

## First Version Definition

Version `0.1` should be considered useful when it can:

- Detect the selected Windrose world.
- Show the current world name and path.
- Create a local backup.
- Restore from a backup.
- Upload/download through a local test cloud folder.
- Prevent sync actions while the game is running.

Google Drive can come after this because local-folder sync lets us validate the save packaging and conflict rules first.
