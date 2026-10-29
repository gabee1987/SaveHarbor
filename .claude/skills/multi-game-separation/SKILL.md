---
name: multi-game-separation
description: How SaveHarbor supports multiple games (Windrose, RuneScape Dragonwilds) with strict separation - game definitions, active-game context, per-game data/cloud scoping, the game switch, and what must never be shared. Use for any change that touches game-specific code or adds a game.
---

# Multi-Game Separation

Goal: one app, two **fully isolated** games. A user can never upload a Dragonwilds save into a
Windrose cloud folder, restore a Windrose backup into Dragonwilds, or see the other game's
worlds/locks while a game is selected.

## Core types (planned, see `docs/plans/dragonwilds/02-game-abstraction.md`)

```csharp
public enum GameId { Windrose, Dragonwilds, Enshrouded }   // persisted as lowercase string: "windrose", "dragonwilds", "enshrouded"

public interface IGameDefinition
{
    GameId Id { get; }
    string DisplayName { get; }                  // "Windrose", "RuneScape: Dragonwilds"
    string StorageKey { get; }                   // "windrose" / "dragonwilds" – used in paths & JSON
    string LaunchUri { get; }                    // steam://rungameid/<appId>
    IReadOnlyList<string> ProcessNames { get; }  // exact names, no ".exe"
    TimeSpan SaveSettleDelay { get; }            // quiet period after exit before reading saves
    Uri ThemeDictionary { get; }                 // pack URI of the game's brush dictionary
    string TextKeyPrefix { get; }                // "Windrose." / "Dragonwilds." for game-specific UiText
}
```

- `IGameSaveAdapter` (one per game): `DiscoverWorldsAsync`, `DiscoverSaveRootsAsync`,
  `ReadWorldAsync`, `GetWorldPayload(world)` (which files/dirs form the unit),
  `GetRestoreTarget(...)`. Windrose = RocksDB directory adapter; Dragonwilds = `.sav` file adapter.
- `IActiveGameContext` singleton: `Current` (`IGameDefinition`), `event ActiveGameChanged`,
  `TrySwitch(GameId)` (refuses while busy / game running / own lock held). Persists the last
  choice to `%LOCALAPPDATA%\SaveHarbor\app-settings.json`.
- Generic domain records replace Windrose ones: `GameWorld` (adds `GameId`), `GameSaveRoot`
  (replaces `WindroseProfile`).

## Separation rules (invariants)

| Concern | Scoping |
|---|---|
| Local backups | `%LOCALAPPDATA%\SaveHarbor\backups\<storageKey>\` |
| Sync state | `%LOCALAPPDATA%\SaveHarbor\sync-state\<storageKey>\<worldId>.json` |
| Cloud folder | **Separate shared folder per game** in `cloud-provider-settings.json` (`SharedFolders: { windrose: id, dragonwilds: id }`) – friend groups can differ |
| Cloud folder identity | Marker file `saveharbor-game.json` (`{ "game": "dragonwilds", "schemaVersion": 1 }`) at shared root; setup and every connect refuse a folder marked for another game. Legacy Windrose folders without marker are accepted for Windrose only and get the marker written |
| Manifests / backup zips | `Game` field = `StorageKey`; reads reject mismatches (`ZipBackupService.ReadManifestAsync` already checks `"Windrose"`; generalise) |
| Logs | Shared files, but every log line includes `{Game}` via `LogContext` |
| Google auth token | Shared (same Google account) – not game data |
| Process detection & launch | From `IGameDefinition` only |
| UI text | Shared keys stay neutral ("world", "session"); game-specific keys prefixed |

Never write a `switch (GameId)` outside the game definition/adapter registration. Code that
needs game-specific behaviour asks the active definition or adapter.

## Backward compatibility (Windrose users already have data)

One-time, idempotent migration at startup (logged, never deletes without a successful move):
- `backups\*.zip` → `backups\windrose\`
- `sync-state\*.json` → `sync-state\windrose\`
- `cloud-provider-settings.json` `GoogleSharedFolderId` → `SharedFolders.windrose`
- `appsettings.json` `GameLauncher` section → per-game `Games:Windrose:*` (old keys still read as fallback)

Cloud layout inside each game's folder stays `worlds/<worldId>/...` — no cloud migration.

## Game switch UX

- Segmented switch in the header (game name + small icon), always visible.
- Disabled with explanatory tooltip while: busy, the active game is running, or we hold a lock.
- Switching: swap theme dictionary, reload worlds/status for the new game, keep window position.
- Optional launch args: `SaveHarbor.exe --game dragonwilds` (per-game desktop shortcuts).

## Adding a third game (Enshrouded, `docs/plans/enshrouded/`)

New `IGameDefinition` + `IGameSaveAdapter` + theme dictionary + text keys + one registration line.
If that requires editing anything else, the abstraction leaked – fix that instead. Known leaks found when
planning Enshrouded, to be fixed in its T1: `AppOptionsLoader.DefaultLaunchUri` (per-game switch → `GameDefaults`
next to the game code) and the per-game `DataTrigger` in `MainWindow.xaml` (→ a `DataTemplateSelector`).

Enshrouded adds two generic, additive members rather than game checks: `GameSaveRoot.CanReceiveWorlds`
(a root that can be shared from but never written to – the Steam Cloud folder) and
`IGameSaveAdapter.GetArchiveFileName` (a world whose local file name is a slot number is archived under a
fixed name and restored into the resolved slot).

SaveHarbor Cloud: one new migration with `insert into public.games (key) values ('<key>');`, where the key equals
`GameIdExtensions.ToStorageKey`. Functions, the `worlds.game` foreign key and the storage upload policy all read that
table (migration `20261021000000_games_table.sql`, tests in `supabase/tests/games.sql`). Push the migration before
releasing the app version that knows the game, otherwise its uploads are refused with `invalid_input`.
