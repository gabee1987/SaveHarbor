# 11 — Tasks: Scoping, Path Safety, Dragonwilds (Plans 04, 03)

Read [09-implementation-handoff.md](09-implementation-handoff.md) first. Prerequisite: CP2 (T02) is done and green. Paths relative to repo root; `App/` = `SaveHarbor.App/`.

---

## T04 — Per-game data and cloud scoping (CP3) · plan 04

### T04.0 Split `GoogleDriveCloudProvider.cs` (pure move, no behaviour change)
The file is 686 lines. Make the class `public sealed partial class GoogleDriveCloudProvider` and split by moving members **verbatim**:

| New file | Members |
|---|---|
| `GoogleDriveCloudProvider.cs` | fields, ctor, `ProviderName`, all public `ICloudProvider`/`ISharedFolderCloudProvider` methods, `HasClientSecrets`, `HasSharedFolder`, `RequireConnectedService` |
| `GoogleDriveCloudProvider.Auth.cs` | `GetOrCreateServiceAsync`, `CreateInteractiveCredentialAsync`, `CreateSilentCredentialAsync`, `LoadAccountEmailAsync` (+ any helper used only by them) |
| `GoogleDriveCloudProvider.Folders.cs` | `EnsureSharedRootFolderAsync`, `GetAndValidateSharedRootFolderAsync`, `FindFolderPathAsync`, `FindWorldFolderIdAsync`, `CreateFolderAsync`, `FindFolderIdAsync`, `FindFileIdByNameAsync`, `FindFileIdAsync`, `ListChildFoldersAsync`, `EscapeQueryValue` |
| `GoogleDriveCloudProvider.Files.cs` | `DownloadJsonByNameAsync`, `UploadJsonByNameAsync`, `UploadFileByNameAsync`, `UploadStreamByNameAsync` |

Each partial file repeats the needed `using`s. **Done when:** build green; `git diff --stat` shows only moves (sum of lines roughly unchanged); no file > 330 lines.

### T04.1 `SafePath` (new, `App/Utilities/SafePath.cs`) + tests

```csharp
using System.IO;

namespace SaveHarbor.App.Utilities;

public static class SafePath
{
    private static readonly string[] ReservedDeviceNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    public static bool IsSafeSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
        {
            return false;
        }

        if (segment.EndsWith('.') || segment.EndsWith(' ') || segment.StartsWith(' '))
        {
            return false;
        }

        if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(segment);
        return !ReservedDeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    public static string CombineUnderRoot(string root, string untrustedSegment)
    {
        if (!IsSafeSegment(untrustedSegment))
        {
            throw new InvalidDataException("A name received from shared data is not a valid file name.");
        }

        var rootFull = Path.GetFullPath(root);
        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(rootFull, untrustedSegment));

        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A name received from shared data resolves outside its folder.");
        }

        return combined;
    }
}
```
`Path.GetInvalidFileNameChars()` on Windows includes `\ / : * ? " < > |` and control chars, so separators and drive colons are rejected.

**Tests** (`SaveHarbor.Tests/Utilities/SafePathTests.cs`, `[Theory]`): safe: `"C8320961717B4D7C459CB58190797ECC"`, `"My World"`, `"My·World"`, `"world.sav"`. Unsafe: `""`, `" "`, `"."`, `".."`, `"..\\evil"`, `"../evil"`, `"a/b"`, `"C:\\x"`, `"name."`, `"name "`, `"CON"`, `"nul.txt"`, `"a|b"`. `CombineUnderRoot` returns a path under the root for safe input and throws `InvalidDataException` for unsafe input.

**Apply at the untrusted boundaries** (each: validate, and on failure return a failed result / throw `InvalidDataException`; do not sanitise silently):
1. `CloudSyncService.Transfers.cs` `DownloadLatestAvailableAsync`: `targetWorldPath = SafePath.CombineUnderRoot(profile.WorldsPath, manifest.WorldId)` (T03 changes this line again for FileSet; keep it correct for Directory now). Wrap in try/catch `InvalidDataException` → `new CloudSyncResult(false, CloudSyncState.Error, "The cloud manifest contains an invalid world id. Nothing was changed.")`.
2. Both download methods: before building `tempPath`, `if (!SafePath.IsSafeSegment(version.ArchiveFileName)) return new CloudSyncResult(false, CloudSyncState.Error, "The cloud version has an invalid archive name. Nothing was changed.");`
3. `ZipBackupService.ImportBackupAsNewWorldAsync`: `targetWorldPath = SafePath.CombineUnderRoot(profile.WorldsPath, manifest.WorldId)`.
4. `FolderCloudProvider.GetWorldPath(worldId)`: `SafePath.CombineUnderRoot(Path.Combine(rootPath, "worlds"), worldId)`; and in `DownloadVersionAsync`, `SafePath.CombineUnderRoot(<versions dir>, request.Version.ArchiveFileName)`.
5. `LocalJsonSyncStateService.GetStatePath` already sanitises — leave it.
(Drive queries already escape values via `EscapeQueryValue`; do not change.)

**Done when:** tests pass; `rg -n "Path.Combine\(.*(WorldId|ArchiveFileName)" App` shows no remaining unvalidated use at those boundaries.

### T04.2 Per-game local roots

1. `IAppDataPathProvider` (+ `AppDataPathProvider`, + `TestPathProvider`): **remove** `LocalTestCloudRoot`; **add**
```csharp
string GetBackupRoot(GameId game);          // AppDataRoot\backups\<key>
string GetSyncStateRoot(GameId game);       // AppDataRoot\sync-state\<key>
string GetLocalTestCloudRoot(GameId game);  // AppDataRoot\cloud-test\<key>
string LegacyBackupRoot { get; }            // AppDataRoot\backups            (migration only)
string LegacySyncStateRoot { get; }         // AppDataRoot\sync-state         (migration only)
string LegacyLocalTestCloudRoot { get; }    // AppDataRoot\cloud-test         (migration only)
```
`CloudLogsPath` becomes `Path.Combine(LegacyLocalTestCloudRoot, "logs")` (unchanged location).
2. `IBackupService`: replace `string BackupRoot { get; }` with `string GetBackupRoot(GameId game);` and `ListBackupsAsync(GameId game, ct)`. `ZipBackupService` gets `IAppDataPathProvider` in its constructor and uses `GetBackupRoot(world.Game)` in `CreateBackupAsync`; `ListBackupsAsync(game)` lists that folder only. Remove its hardcoded root.
3. `LocalJsonSyncStateService`: constructor takes `IAppDataPathProvider`; `GetStatePath(game, worldId)` = `Path.Combine(GetSyncStateRoot(game), safeId + ".json")`; `LoadAsync/SaveAsync` use `world.Game` / `state.Game` — add `public string Game { get; set; } = string.Empty;` to `LocalSyncState` (StorageKey; `CreateNew` sets it). On `LoadAsync` treat a state whose `Game` is non-empty and differs as "not found".
4. `FolderCloudProvider`: `rootPath` is no longer a field; every method derives `GetLocalTestCloudRoot(game)` (game comes from T04.5 signatures). Do this together with T04.5.
5. VM: `BackupRoot` computed property → `_backupService.GetBackupRoot(_activeGame.Current.Id)`; replace `_backupService.BackupRoot` usages (LocalCommands lines ~95/134/216–217) the same way; `RefreshBackupStatsAsync` → `ListBackupsAsync(_activeGame.Current.Id)`.

### T04.3 Legacy migration (`App/Infrastructure/Migrations/LegacyLayoutMigrator.cs`)

```csharp
public sealed record MigrationReport(int MovedItems, IReadOnlyList<string> Errors);

public sealed class LegacyLayoutMigrator(IAppDataPathProvider paths, IAppLogger logger)
{
    private const string MarkerFileName = "migrations.json";

    public MigrationReport Run() { ... }
}
```
Behaviour (all synchronous, all steps individually try/caught, errors collected, never deletes anything that was not successfully moved):
1. If `Path.Combine(paths.AppDataRoot, MarkerFileName)` exists and contains `"LegacyLayoutV1"` → return `new(0, [])`.
2. Move each top-level `*.zip` in `LegacyBackupRoot` → `GetBackupRoot(GameId.Windrose)` (create dir). `File.Move`; if the target exists, skip and log a warning (do not overwrite).
3. Move each top-level `*.json` in `LegacySyncStateRoot` → `GetSyncStateRoot(GameId.Windrose)` (same rules).
4. If `Path.Combine(LegacyLocalTestCloudRoot, "worlds")` exists and `Path.Combine(GetLocalTestCloudRoot(GameId.Windrose), "worlds")` does not → `Directory.Move`.
5. Only if `Errors.Count == 0`: write the marker `{ "LegacyLayoutV1": "<UTC ISO timestamp>" }`. With errors, no marker → it retries next start (idempotent because every step skips what is already moved).
6. Log counts with `AppLogKeyword.App`.
**Settings v1 → v2 is NOT done here** (T04.4's reader handles both formats).

Call site: `App.OnStartup`, right after `await _host.StartAsync();` and before `GetRequiredService<MainWindow>()`: `var report = host.Services.GetRequiredService<LegacyLayoutMigrator>().Run();` (register as singleton). If `report.Errors.Count > 0`, after the window is shown call `IToastService.Warning("Data migration incomplete", "Some older files could not be moved. Nothing was deleted. See the log.")`.

**Tests** (`Migrations/LegacyLayoutMigratorTests.cs`, using `TestPathProvider`): legacy fixture (2 zips, 1 sync-state json, `cloud-test/worlds/TEST_WORLD_ID/manifest.json`) → after `Run()` files exist only under the `windrose` locations with identical bytes (compare SHA-256); second `Run()` returns 0 moved; pre-existing target → skipped, source untouched, error list empty but a warning logged; marker written only when no errors; a locked source file (open a `FileStream` with `FileShare.None`) → error reported, no marker.

### T04.4 Cloud options and setup per game

1. `CloudProviderOptions`: replace `GoogleSharedFolderId` with
```csharp
public Dictionary<GameId, string> SharedFolderIds { get; } = [];
public string GetSharedFolderId(GameId game) => NormalizeSharedFolderInput(SharedFolderIds.GetValueOrDefault(game, string.Empty));
public bool HasSharedFolder(GameId game) => !string.IsNullOrWhiteSpace(GetSharedFolderId(game));
public void SetSharedFolderId(GameId game, string input) => SharedFolderIds[game] = NormalizeSharedFolderInput(input);
public static string NormalizeSharedFolderInput(string input) { /* the existing URL/ID parsing from ResolveGoogleSharedFolderId, verbatim, on the argument */ }
```
Remove `HasGoogleSharedFolder` and `ResolveGoogleSharedFolderId`. Keep `ResolveGoogleClientSecretsPath` unchanged.
2. `LocalCloudProviderSettings` (move it to `Domain/LocalCloudProviderSettings.cs`):
```csharp
public sealed class LocalCloudProviderSettings
{
    public int SchemaVersion { get; set; } = 2;
    public Dictionary<string, string> SharedFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string GoogleSharedFolderId { get; set; } = string.Empty;   // v1 legacy: read, treated as Windrose, never written
}
```
3. `AppOptionsLoader.LoadCloudProviderOptions`: after reading `appsettings.json` (`GoogleSharedFolderId` there → Windrose, as today), read the local settings file: for each `SharedFolders` entry with a parseable key set the game's id; if `SharedFolders` has no `windrose` and legacy `GoogleSharedFolderId` is non-empty → Windrose. Local settings override appsettings (as today).
4. `ICloudSetupService` becomes
```csharp
string GetCurrentSharedFolderId(GameId game);
bool HasSharedFolderConfigured(GameId game);
Task<CloudSetupTestResult> TestSharedFolderAsync(GameId game, string input, CancellationToken ct = default);
Task SaveSharedFolderAsync(GameId game, string input, CancellationToken ct = default);
```
(`NormalizeSharedFolderInput` instance method is removed — the mutate-and-restore hack goes away; call the static helper.) `ISharedFolderCloudProvider.TestSharedFolderAsync(GameId game, string sharedFolderId, ct)`.
`SaveSharedFolderAsync`: load the existing settings file (or new), set `SharedFolders[game.ToStorageKey()] = id`, clear legacy field, `SchemaVersion = 2`, write with `File.WriteAllTextAsync` to a `.tmp` then `File.Move(tmp, path, overwrite: true)`; update `options.SetSharedFolderId`.
5. Dialog: `IDialogService.ConfigureCloudFolder(string gameDisplayName, string currentFolderId, Func<...> testAccessAsync)`; `CloudFolderSetupWindow` gets `gameDisplayName` ctor arg and sets `Title = $"{gameDisplayName} · " + existing title` (use string.Format on a new UiText key `"CloudSetup.TitleFormat": "{0} – Google Drive folder setup"`; the existing XAML title TextBlock is bound to `CloudSetup.Title`; set its `Text` from code-behind via a named element `TitleText`). Add one sentence to `CloudSetup.Help`: "Use a separate folder for each game."
6. VM: `PromptCloudFolderSetupIfNeededAsync`, `SetupCloudFolderAsync` use the active game: `_cloudSetupService.HasSharedFolderConfigured(_activeGame.Current.Id)`, etc. `OnActiveGameChanged` (T02.8) additionally calls `await PromptCloudFolderSetupIfNeededAsync()` after `RefreshAsync()`.

### T04.5 `ICloudProvider` becomes game-aware

New signatures (account-level calls keep no game; anything touching the folder gets `GameId`):
```csharp
Task<CloudConnectionStatus> GetConnectionStatusAsync(GameId game, CancellationToken ct = default);
Task<CloudConnectionResult> ConnectAsync(GameId game, CancellationToken ct = default);
Task DisconnectAsync(CancellationToken ct = default);
Task<CloudWorldManifest?> GetWorldManifestAsync(GameId game, string worldId, CancellationToken ct = default);
Task<IReadOnlyList<CloudWorldManifest>> ListWorldManifestsAsync(GameId game, CancellationToken ct = default);
Task<CloudSessionLock?> GetSessionLockAsync(GameId game, string worldId, CancellationToken ct = default);
Task<CloudUploadResult> UploadVersionAsync(CloudUploadRequest request, CancellationToken ct = default);   // game = request.World.Game
Task<CloudDownloadResult> DownloadVersionAsync(CloudDownloadRequest request, CancellationToken ct = default); // same
Task WriteSessionLockAsync(GameId game, CloudSessionLock sessionLock, CancellationToken ct = default);
Task ClearSessionLockAsync(GameId game, string worldId, string lockId, CancellationToken ct = default);
```
`ICloudSyncService`: `ConnectAsync(GameId game, ct)`; everything else already takes `GameWorld`/`GameSaveRoot` (use `.Game`). `CloudSyncService`: every provider call passes `world.Game` / `profile.Game`. `NotConnected` branch in `RefreshStatusAsync` passes `world.Game`.
Update `NotConfiguredCloudProvider` to the new signatures (same behaviour).

**GoogleDriveCloudProvider:** thread a `GameId game` parameter into `FindFolderPathAsync` (first parameter after `service`), `FindWorldFolderIdAsync`, `EnsureSharedRootFolderAsync`; replace `options.ResolveGoogleSharedFolderId()` with `options.GetSharedFolderId(game)`; `HasSharedFolder(game, out message)` uses `options.HasSharedFolder(game)` and the message `$"Google Drive shared folder for {game} is not configured. Use Setup to paste the folder link."`. Compile-driven.
**FolderCloudProvider:** root = `pathProvider.GetLocalTestCloudRoot(game)`; world path = `SafePath.CombineUnderRoot(Path.Combine(root, "worlds"), worldId)`.

### T04.6 Folder identity marker

`App/Domain/GameFolderMarker.cs`:
```csharp
public sealed class GameFolderMarker
{
    public const string FileName = "saveharbor-game.json";
    public int SchemaVersion { get; set; } = 1;
    public string Game { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
```
Decision function (pure, unit-tested) `App/Infrastructure/GameFolderMarkerPolicy.cs`:
```csharp
public enum MarkerDecision { Accept, AcceptAndWriteMarker, Reject }

public static MarkerDecision Evaluate(GameId requested, GameFolderMarker? marker, bool folderHasWorldsSubfolder, bool folderIsEmpty)
```
| marker | other conditions | result |
|---|---|---|
| present, `Game` equals requested | — | Accept |
| present, different game | — | Reject |
| absent | folder empty | AcceptAndWriteMarker |
| absent | has `worlds/` and requested == Windrose | AcceptAndWriteMarker |
| absent | has `worlds/` and requested != Windrose | Reject |
| absent | not empty, no `worlds/` | Reject (message: "This folder already contains other files. Use an empty folder.") |

Reject messages: `$"This folder is used for {markerGame}. Choose a separate folder for {requested}."` (use `DisplayName`-like text: capitalise via a small switch-free helper `requested.ToString()`).

Provider integration:
- Drive: add `GoogleDriveCloudProvider.Marker.cs` (partial) with `EnsureGameFolderAsync(service, game, ct)`: reads marker via `DownloadJsonByNameAsync<GameFolderMarker>(service, rootId, GameFolderMarker.FileName, ct)`, computes `folderHasWorldsSubfolder` via `FindFolderIdAsync(service, rootId, "worlds", ct)` and `folderIsEmpty` by listing 1 child (`ListChildFoldersAsync` is folders only — add a small `HasAnyChildAsync`), applies the policy, writes the marker with `UploadJsonByNameAsync` on `AcceptAndWriteMarker` (only when the account has edit access, already validated), throws `InvalidOperationException(message)` on `Reject`. Cache validated games in a `HashSet<GameId> verifiedGames` (cleared by `DisconnectAsync`).
- Call it from: `TestSharedFolderAsync` **in validate-only mode** (do not write; a flag `writeMarker: false` → on `AcceptAndWriteMarker` just return success), `ConnectAsync`, `GetConnectionStatusAsync` (after the service exists; catch `InvalidOperationException` → status `IsConnected=false` with the message), and `UploadVersionAsync`/`WriteSessionLockAsync` (before the first write).
- `FolderCloudProvider`: identical logic against `<root>/saveharbor-game.json` and `<root>/worlds`.

**Tests:** `GameFolderMarkerPolicyTests` — one test per table row (6) + Dragonwilds-on-legacy-folder rejected.

### T04.7 Manifest/backup game validation
- Providers: in `GetWorldManifestAsync`/`ListWorldManifestsAsync`, ignore (list) or throw `InvalidDataException` (get) manifests whose non-empty `Game` ≠ `game.ToStorageKey()` (case-insensitive). `CloudWorldManifest.Game` legacy default `"Windrose"` already covers old manifests.
- `ZipBackupService.ReadManifestAsync` already validates (T02.7).
- `CloudSyncService` downloads: after the archive is verified, call `backupService.ReadManifestAsync(path, world.Game)` is performed by restore/import paths (they call it) — add it to `RestoreBackupAsync` as well so a wrong-game zip is rejected *before* the pre-restore backup/overwrite: first statement after the `File.Exists` check: `var manifest = await ReadManifestAsync(backupPath, targetWorld.Game, ct);` (discard value).

### T04.8 Logging
- `ConfigureLogging` (App.xaml.cs): add `.Enrich.WithProperty("Game", "-")` before the sinks, and change the template to `"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Game}] [{Keyword}] {Message:lj}{NewLine}{Exception}"`.
- `SerilogAppLogger` constructor takes `IActiveGameContext`; every call uses `Log.ForContext("Game", activeGame.Current.StorageKey).ForContext("Keyword", keyword.ToString())`. (Create the base `ILogger` property per call — cheap.)
- DI order: `SerilogAppLogger` depends on `IActiveGameContext`; `ActiveGameContext` therefore must not depend on `IAppLogger` (already the case per T02.5).

### T04.9 `FakeCloudProvider` + tests
`SaveHarbor.Tests/Support/FakeCloudProvider.cs`: implements `ICloudProvider`; in-memory `Dictionary<(GameId, string), CloudWorldManifest>`, locks dictionary, versions dictionary; public hooks `Func<Task>? BeforeLockWrite`, `bool ThrowOnUpload`; records calls in `List<string> Calls`. It is needed from here on (also T05).
Tests: `CloudSyncService.RefreshStatusAsync` per state branch (NotConnected, NoCloudSave, SomeonePlaying, CloudNewer ×2, Conflict, UpToDate) with a `TestPathProvider`-backed `LocalJsonSyncStateService`, a stub `IBackupService`, `NullAppLogger`; game isolation (a Windrose manifest invisible when querying Dragonwilds).

### T04.10 Verification (CP3)
- Build + tests green.
- Smoke test Windrose with a **copy** of real legacy data if available (do not test the migrator on the owner's real folders without asking; the unit test covers it).
- Report: list of every signature change, and confirm `rg -n "LocalTestCloudRoot\b|BackupRoot\b" App` has no stale uses.

### CHECKPOINT CP3 → stop, report.

---

## T03 — Dragonwilds integration (CP4) · plan 03

### T03.0 Phase 0 — owner-only verification (blocking for release, **not** for coding)
Create `docs/plans/dragonwilds/PHASE0-RESULTS.md` from this template and ask the owner to fill it. Do not fill it yourself.
```markdown
# Phase 0 results (Dragonwilds, owner-filled)
| ID | Question | Result | Date |
|----|----------|--------|------|
| V1 | Contents of %LOCALAPPDATA%\RSDragonwilds\Saved\SaveGames (subfolders? exact names? backup files?) | | |
| V2 | First 4 bytes of a world .sav (GVAS?) | | |
| V3 | Process names in Task Manager > Details while in-game | | |
| V4 | Steam Cloud: contents of Steam\userdata\<id>\1374490\remote | | |
| V5 | Can a second Steam account host a copied world? | | |
| V6 | Does Steam show a cloud-conflict dialog after SaveHarbor restores a world? | | |
| V7 | Does the game rely on file modified time for the world list? | | |
| V8 | Where is the game build id (appmanifest_1374490.acf)? | | |
```
**Assumptions used until results exist (isolate each behind a named constant or option so it can change in one place):** worlds are top-level `*.sav` in `SaveGames`; no subfolders; game backup files end with `.bak` or `.backup` or contain `.bak.` (ignored by discovery); no header validation (`DragonwildsSaveAdapter.ValidateGvasHeader = false` const); processes `RSDragonwilds-Win64-Shipping`, `RSDragonwilds`, `RSDragonwildsServer`.
If V5 fails, **stop** and report; do not continue with T05's Dragonwilds behaviour.

### T03.1 Interface additions and definition
- `IGameDefinition`: add `string PlayHint { get; }`. Windrose returns `"Starts a cloud session first, then launches Windrose through Steam."` (move the current text out of `Actions.PlayHint`).
- `IGameSaveAdapter`: add `string GetExpectedWorldPath(GameSaveRoot root, string worldId);`. Windrose: `SafePath.CombineUnderRoot(root.WorldsPath, worldId)`.
- `App/Infrastructure/Games/Dragonwilds/DragonwildsGameDefinition.cs`:
```csharp
public sealed class DragonwildsGameDefinition(GameOptionsProvider optionsProvider, DragonwildsSaveAdapter saveAdapter) : IGameDefinition
{
    public GameId Id => GameId.Dragonwilds;
    public string StorageKey => Id.ToStorageKey();
    public string DisplayName => "RuneScape: Dragonwilds";
    public string LaunchUri => optionsProvider.Get(Id).LaunchUri;
    public string ExecutablePath => optionsProvider.Get(Id).ExecutablePath;
    public IReadOnlyList<string> ProcessNames { get; } = ["RSDragonwilds-Win64-Shipping", "RSDragonwilds", "RSDragonwildsServer"];
    public ProcessMatch ProcessMatch => ProcessMatch.Exact;
    public TimeSpan SaveSettleDelay => TimeSpan.FromSeconds(15);
    public Uri ThemeDictionary { get; } = new("pack://application:,,,/Resources/Styles/DarkTheme.xaml");
    public string? PostRestoreHint => "If Steam asks about a cloud conflict, choose Local files.";
    public string PlayHint => "Hosting tonight? Press Play. Just joining? Start Dragonwilds normally and join your host.";
    public IGameSaveAdapter SaveAdapter => saveAdapter;
}
```
(`ThemeDictionary` is switched in T06.)
- Register: `services.AddSingleton<DragonwildsSaveAdapter>(); services.AddSingleton<IGameDefinition, DragonwildsGameDefinition>();`
- XAML: `ActionsSectionView.xaml` Play hint `TextBlock` binds `{Binding PlayHintText}` (new VM property = `_activeGame.Current.PlayHint`, raised in `OnActiveGameChanged`); remove the `Actions.PlayHint` key from the json if no longer referenced (`rg Actions.PlayHint`).
- After a successful download in the VM (both `DownloadCloudAsync` paths), if `_activeGame.Current.PostRestoreHint` is not null → `AddActivity("Info", hint)`.

### T03.2 `DragonwildsSaveAdapter` (`Infrastructure/Games/Dragonwilds/`, target < 200 lines)
```csharp
public sealed class DragonwildsSaveAdapter(GameOptionsProvider optionsProvider) : IGameSaveAdapter
{
    internal const bool ValidateGvasHeader = false;
    private static readonly string[] IgnoredSuffixes = [".bak", ".backup"];

    public WorldPayloadKind PayloadKind => WorldPayloadKind.FileSet;
    public string SaveRootPath => ResolveRoot();
    ...
}
```
- `ResolveRoot()`: `SaveRoot` option (env-expanded) if set, else `Path.Combine(LocalApplicationData, "RSDragonwilds", "Saved", "SaveGames")`.
- `DiscoverSaveRootsAsync`: if the folder exists → one `GameSaveRoot(GameId.Dragonwilds, "steam", root, root, $"{n} world(s)", Directory.GetLastWriteTimeUtc(root))`; else empty.
- `DiscoverWorldsAsync`: `Directory.EnumerateFiles(root, "*.sav", SearchOption.TopDirectoryOnly)`, skipping names that contain an ignored suffix (`name.Contains(".bak", OrdinalIgnoreCase)` or ends with `.backup`); each → `ReadWorldAsync`. Order: `LastModifiedAt` desc, then name.
- `ReadWorldAsync(path)`: returns null if the file does not exist or has a non-`.sav` extension; `stem = Path.GetFileNameWithoutExtension(path)`; `WorldId = FileNameSanitizer.MakeSafeFileName(stem)` — if that is empty/unsafe (`!SafePath.IsSafeSegment`) return null; `WorldName = stem`; `Subtitle = string.Empty`; `SavePath = file.FullName`; `CreatedAt = file.CreationTime`; `LastModifiedAt = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime()`; `SizeBytes`; `FileCount = 1`.
- `GetPayloadFiles(world)` = `[world.SavePath]`.
- `GetExpectedWorldPath(root, worldId)` = `SafePath.CombineUnderRoot(root.WorldsPath, worldId + ".sav")`.
- `SaveCharacters` and `Config` are never enumerated or touched anywhere (no code path should reference them).

### T03.3 Payload-based backup/restore (the main refactor)
Goal: `ZipBackupService` handles `Directory` and `FileSet` payloads; Windrose output stays **byte-compatible in structure** (`saveharbor-manifest.json` + `world/…`).

1. `BackupManifest` (v2, additive): add
```csharp
public string PayloadKind { get; set; } = "Directory";            // "Directory" | "FileSet"
public List<BackupFileEntry> Files { get; set; } = [];             // empty for v1 manifests
```
and `public sealed class BackupFileEntry { public string RelativePath { get; set; } = ""; public string Sha256 { get; set; } = ""; public long SizeBytes { get; set; } }`. New archives write `SchemaVersion = 2`; the reader accepts 1 and 2 (anything else → `InvalidOperationException("Unsupported backup version.")`). Replace the anonymous manifest object in `CreateArchive` with a `BackupManifest` instance serialised with the existing `JsonOptions`.
2. `IPayloadStrategy` (`Infrastructure/Backup/`):
```csharp
internal interface IPayloadStrategy
{
    IReadOnlyList<BackupFileEntry> Stage(GameWorld world, IGameSaveAdapter adapter, string payloadRoot, CancellationToken ct);
    void Restore(string payloadRoot, GameWorld target, BackupManifest manifest, CancellationToken ct);
    string Import(string payloadRoot, BackupManifest manifest, GameSaveRoot root, IGameSaveAdapter adapter, bool overwriteExisting, CancellationToken ct);
}
```
   - `DirectoryPayloadStrategy`: **move** `CopyDirectory` and `ReplaceDirectory` here unchanged; `Stage` = `CopyDirectory(world.SavePath, payloadRoot, ct)` then returns an empty list (v1-compatible); `Restore` = `ReplaceDirectory(payloadRoot, target.SavePath, ct)`; `Import` = existing logic from `ImportBackupAsNewWorldAsync` (target = `adapter.GetExpectedWorldPath(root, manifest.WorldId)`; if exists and !overwrite → `IOException`-style existing message; delete + move staging as today).
   - `FileSetPayloadStrategy`: `Stage` copies each `adapter.GetPayloadFiles(world)` to `payloadRoot/<file name>` and returns entries (`RelativePath = file name`, SHA-256 via `FileHashCalculator`-equivalent sync hash, size). `Restore`: for each entry validate `SafePath.IsSafeSegment(entry.RelativePath)`, target path = `Path.Combine(Path.GetDirectoryName(target.SavePath)!, entry.RelativePath)`, then `ReplaceFile(...)` below. `Import`: target dir = `root.WorldsPath` (create if missing), each entry's target via `SafePath.CombineUnderRoot(root.WorldsPath, entry.RelativePath)`; if any target exists and `!overwriteExisting` → throw `IOException($"A local world file named '{name}' already exists.")`; returns the path of the first entry.
   - `ReplaceFile(string source, string target)` (private static in the file-set strategy):
```csharp
var temp = target + ".saveharbor-tmp";
var previous = target + ".saveharbor-prev";
File.Copy(source, temp, overwrite: true);
try
{
    if (File.Exists(target)) File.Replace(temp, target, previous); else File.Move(temp, target);
    File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
}
catch
{
    if (File.Exists(temp)) File.Delete(temp);
    if (File.Exists(previous)) File.Move(previous, target, overwrite: true);
    throw;
}
```
     Restore collects the `previous` paths and deletes them only after **all** files succeeded; on any failure it restores every `previous` that exists, then rethrows. Hash-verify each written file against the manifest entry before the swap (`Sha256` mismatch → `InvalidDataException`, nothing replaced).
3. `ZipBackupService`: inject `IGameRegistry`; `var adapter = registry.Get(world.Game).SaveAdapter;` selects the strategy by `adapter.PayloadKind` for create, and by `manifest.PayloadKind` for restore/import (a mismatch between manifest kind and the active adapter's kind → `InvalidOperationException("This backup was created for a different save layout.")`). `PayloadSha256` is still `DirectoryHashCalculator.ComputeSha256(payloadRoot)`. Keep each file < 300 lines.
4. `CloudSyncService.Transfers.cs` `DownloadLatestAvailableAsync`: replace the `Directory.Exists(targetWorldPath)` pre-check with `File.Exists(path) || Directory.Exists(path)` where `path = adapter.GetExpectedWorldPath(profile, manifest.WorldId)` (adapter from `IGameRegistry.Get(profile.Game).SaveAdapter`; inject the registry into `CloudSyncService`). Failure message unchanged apart from `world file or folder`.

**Tests** (`SaveHarbor.Tests/Backup/`):
- `FileSetRoundTripTests`: create `My World.sav` (random 4 KB), backup, modify the original, restore → bytes equal the backup; file name unchanged; `LastWriteTimeUtc` within 5 s of now; no `.saveharbor-*` leftovers; a sibling `SaveCharacters/char.json` hash identical before/after.
- `RestoreFailureRollsBackTests`: restore with a manifest whose entry hash is wrong → exception, original file bytes unchanged, no leftovers.
- `WrongGameRejectedTests`: a Windrose-manifest zip restored with `GameId.Dragonwilds` target → `InvalidOperationException`, target untouched, **no pre-restore backup created**.
- `LegacyV1DirectoryRestoreTests`: v1-style zip (build by hand: manifest without `PayloadKind`/`Files`) restores a Windrose directory.
- `DragonwildsSaveAdapterTests`: discovers `.sav` only; ignores `*.sav.bak`, `.json`; handles `My·World.sav`; returns empty when the root is missing; `WorldId` stable; `GetExpectedWorldPath` for hostile id throws.
Use a `TestRegistry` stub registry with two adapters over temp folders.

### T03.4 Verification (CP4)
- Build + tests green.
- With the owner's Phase 0 results absent, state clearly which assumptions are unverified (list A1–A4 from T03.0).
- Manual (only if Dragonwilds is installed): switch to Dragonwilds, confirm worlds list, create a backup, restore it, check the character folder is untouched. Otherwise state "not performed — game not available" and rely on tests.

### CHECKPOINT CP4 → stop, report.
