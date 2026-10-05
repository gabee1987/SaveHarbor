# Plan 02 — Game Abstraction and Active-Game Context

**Branch:** `feature/dragonwilds-support`
**Depends on:** 01. **Blocks:** 03, 04, 05, 06.
**Skills:** `multi-game-separation`, `architecture-overview`, `coding-standards`, `wpf-mvvm-conventions`.

## 1. Objective

Remove every hardcoded Windrose assumption behind a small game abstraction, with **zero behaviour change for Windrose**. After this plan the app still supports only Windrose, but adding Dragonwilds (plan 03) becomes a matter of registering new implementations.

## 2. Design

### 2.1 New domain types (`Domain/`)

| File | Content |
|---|---|
| `GameId.cs` | `public enum GameId { Windrose, Dragonwilds }` |
| `GameWorld.cs` | `sealed record GameWorld(GameId Game, string WorldId, string WorldName, string Subtitle, string SavePath, DateTimeOffset CreatedAt, DateTimeOffset LastModifiedAt, long SizeBytes, int FileCount)`. `Subtitle` replaces Windrose's `WorldPresetType` as a game-neutral secondary line. For Windrose it is the preset; for Dragonwilds it can be the world/difficulty type, or empty. |
| `GameSaveRoot.cs` | `sealed record GameSaveRoot(GameId Game, string RootId, string RootPath, string WorldsPath, string Detail, DateTimeOffset LastModifiedAt)`. Replaces `WindroseProfile`. `Detail` carries "RocksDB 0.10.0" for Windrose. |
| `WorldPayloadKind.cs` | `enum WorldPayloadKind { Directory, FileSet }`. Windrose = Directory (whole RocksDB folder); Dragonwilds = FileSet (exactly the world `.sav`; the game's own backup files and character JSON are excluded, see the `dragonwilds-save-format` skill). |

`WindroseWorld` and `WindroseProfile` are **deleted**, not kept as aliases. All call sites move to the new records. This is a mechanical rename across `Services`, `Infrastructure`, `ViewModels` and `Views` (bindings to `WorldPresetType` change to `Subtitle`).

### 2.2 Game definition (`Services/IGameDefinition.cs`, `Infrastructure/Games/*`)

```csharp
public interface IGameDefinition
{
    GameId Id { get; }
    string StorageKey { get; }               // "windrose" | "dragonwilds"
    string DisplayName { get; }
    string LaunchUri { get; }                // from options, with a default
    string? ExecutablePath { get; }          // optional override from options
    IReadOnlyList<string> ProcessNames { get; }
    TimeSpan SaveSettleDelay { get; }
    Uri ThemeDictionary { get; }
    IGameSaveAdapter SaveAdapter { get; }
}
```

Files:
- `Infrastructure/Games/WindroseGameDefinition.cs` holds the values from the `windrose-save-format` skill. `ProcessNames` initially stays `["Windrose", "R5"]` with **substring** semantics, to preserve behaviour. Plan 05 tightens it after the exact exe names are verified.
- `Infrastructure/Games/GameRegistry.cs` (`IGameRegistry`): `IReadOnlyList<IGameDefinition> All`, `Get(GameId)`. This is the **only** place that enumerates games.

### 2.3 Save adapter (`Services/IGameSaveAdapter.cs`)

Replaces `IWindroseSaveDiscoveryService`.

```csharp
public interface IGameSaveAdapter
{
    WorldPayloadKind PayloadKind { get; }
    Task<IReadOnlyList<GameSaveRoot>> DiscoverSaveRootsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<GameWorld>> DiscoverWorldsAsync(CancellationToken ct = default);
    Task<GameWorld?> ReadWorldAsync(string savePath, CancellationToken ct = default);
    IReadOnlyList<string> GetPayloadFiles(GameWorld world);       // absolute paths that form the world unit
    string GetImportTargetPath(GameSaveRoot root, string worldId, string sourceFileName); // where a cloud/zip world lands on a PC without it
}
```

- `Infrastructure/Games/Windrose/WindroseSaveAdapter.cs` is a move of `WindroseSaveDiscoveryService` with the rename only. `GetPayloadFiles` returns all files under the directory except `LOCK`, matching current zip behaviour. Confirm this against `ZipBackupService.CreateArchive` before moving.

### 2.4 Active game context (`Services/IActiveGameContext.cs`, `Infrastructure/ActiveGameContext.cs`)

```csharp
public interface IActiveGameContext
{
    IGameDefinition Current { get; }
    event EventHandler<IGameDefinition>? ActiveGameChanged;
    void SetActive(GameId id);   // caller (VM) is responsible for checking whether switching is allowed
}
```
- Loads the initial game from, in order: command-line `--game <storageKey>`, then `app-settings.json` (`LastGame`), then the default `Windrose`.
- Persists `LastGame` on change. Add `IAppDataPathProvider.AppSettingsPath` → `%LOCALAPPDATA%\SaveHarbor\app-settings.json`.
- `app-settings.json` schema: `{ "schemaVersion": 1, "lastGame": "dragonwilds" }`.

### 2.5 Services that become game-aware

| Service | Change |
|---|---|
| `IProcessDetectionService` | `bool IsGameRunning(IGameDefinition game)` replaces `IsWindroseRunning()` |
| `IGameLauncherService` | `LaunchAsync(IGameDefinition game, CancellationToken)`. `WindroseGameLauncherService` is renamed to `SteamGameLauncherService` (logic unchanged; messages use `game.DisplayName`) |
| `IBackupService` | Methods take `GameWorld`. `BackupRoot` becomes `GetBackupRoot(GameId)` (scoping applied in plan 04; here it returns the legacy root to keep behaviour). Manifest `Game` = `world.Game` storage key; read-validation compares against the active game instead of `"Windrose"` |
| `ICloudSyncService`, `ILocalSyncStateService` | Signature rename to `GameWorld` / `GameSaveRoot` only |
| `GoogleDriveCloudProvider`, `FolderCloudProvider` | Manifest `Game` from `request.World.Game` instead of the literal |
| `GameLauncherOptions` | Becomes per-game `GameOptions` read from `SaveHarbor:Games:<StorageKey>` (`LaunchUri`, `ExecutablePath`). The legacy `SaveHarbor:GameLauncher` section is still read as a Windrose fallback |

`appsettings.json` target shape:
```json
"Games": {
  "Windrose":    { "LaunchUri": "steam://rungameid/3041230", "ExecutablePath": "" },
  "Dragonwilds": { "LaunchUri": "steam://rungameid/<appId>", "ExecutablePath": "" }
}
```
The Dragonwilds app id comes from the `dragonwilds-save-format` skill.

### 2.6 View model

- New partial `MainWindowViewModel.GameSelection.cs`:
  - `ObservableCollection<GameOption> Games` (Id, DisplayName, icon key), `[ObservableProperty] GameId activeGame`.
  - `CanSwitchGame` = `!IsBusy && !IsGameRunning && !HasOwnCloudSession()`.
  - `SwitchGameCommand(GameId)` → `_activeGame.SetActive(id)` → clear `Worlds`, `SelectedWorld` and `CloudStatus` → `RefreshAsync()`.
  - `SwitchGameDisabledReason` (string) for the tooltip.
- Replace every `"Windrose"` in status, toast and dialog strings with `ActiveGameName` (`_activeGame.Current.DisplayName`). Affected files: `.CloudCommands`, `.LocalCommands`, `.GameMonitor`, `.Operations`, `MainWindowViewModel.cs` (initial `profileStatus`).
- `UpdateGameStatus()` → `_processDetectionService.IsGameRunning(_activeGame.Current)`.

### 2.7 UI text

Game-specific strings in `ui-text.en.json` (for example `App.Subtitle`, `World.PresetTooltip`, `Header.GameTooltip`) become neutral, or are split into `Windrose.*` and `Dragonwilds.*` keys. Add a `{ui:GameText Key}` markup extension **only if** more than about 5 keys are game-specific. Otherwise expose the few values as VM properties. Prefer the simpler option.

### 2.8 DI registration (`App.xaml.cs`)

```csharp
services.AddSingleton<WindroseGameDefinition>();
services.AddSingleton<IGameRegistry, GameRegistry>();
services.AddSingleton<IActiveGameContext, ActiveGameContext>();
```
Remove `IWindroseSaveDiscoveryService`. The `LoadGameLauncherOptions` method becomes `LoadGameOptions(storageKey)`.
`App.xaml.cs` is already 283 lines. Extract the option loaders into `Infrastructure/AppOptionsLoader.cs` (static, pure config reading) as part of this plan, because new loaders are being added. This is the justified split.

## 3. Step order (each step builds and runs)

1. Add `GameId`, `GameWorld`, `GameSaveRoot`, and rename the records across the codebase (compile-driven).
2. Add `IGameSaveAdapter` and move the Windrose discovery into `WindroseSaveAdapter`.
3. Add `IGameDefinition`, `WindroseGameDefinition`, `GameRegistry`, and `AppOptionsLoader` with the `Games` section and legacy fallback.
4. Make the process detection, launcher, backup manifest and provider manifest game-aware.
5. Add `ActiveGameContext` and `app-settings.json`.
6. Add the VM game-selection partial (no visible switch yet, or a switch with Windrose only) and neutralise strings.
7. Smoke test Windrose end-to-end.

## 4. Acceptance criteria

- [ ] `rg -n "Windrose" SaveHarbor.App --glob "*.cs"` matches only inside `Infrastructure/Games/Windrose*` and `WindroseGameDefinition`.
- [ ] Existing users: backups, sync-state, Drive folder and the `GameLauncher` config work unchanged.
- [ ] Unit tests (plan 07): `WindroseSaveAdapter` discovers fixture worlds; `AppOptionsLoader` legacy fallback works; `ActiveGameContext` restores the last game and honours `--game`.
- [ ] No file exceeds 400 lines because of this plan.

## 5. Risks and decisions for the owner

- **Rename churn**: about 30 files touched mechanically. It is kept in its own reviewable change set, before any Dragonwilds logic.
- **Decision**: single exe with a game switch (recommended), versus two separately published executables from one codebase (`SaveHarbor.Windrose.exe`, `SaveHarbor.Dragonwilds.exe` with a fixed game). The abstraction supports both. Two executables add release overhead for no real isolation gain beyond what plan 04 already provides. The `--game` argument plus a desktop shortcut per game gives the "separate apps" feel at no extra cost.
