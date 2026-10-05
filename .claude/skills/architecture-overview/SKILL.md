---
name: architecture-overview
description: Map of the SaveHarbor WPF app - folders, DI composition root, key services, data locations, and where each responsibility lives. Use when orienting in the codebase or deciding where new code belongs.
---

# SaveHarbor Architecture Overview

Single WPF project `SaveHarbor.App` (solution `SaveHarbor.sln`). ~6.6k lines. No test project yet.

## Folder responsibilities

| Folder | Holds | Rules |
|---|---|---|
| `Domain/` | Records, enums, JSON document classes (`WindroseWorld`, `CloudWorldManifest`, `CloudSessionLock`, `LocalSyncState`, `BackupManifest`, `AppError*`) | No IO, no WPF references |
| `Services/` | Interfaces (`I*Service`, `ICloudProvider`) + `ToastService` | Contracts only (ToastService is a legacy exception) |
| `Infrastructure/` | Implementations: discovery, backup (zip), cloud providers, sync, launcher, process detection, logging, options | All file/network/process IO lives here |
| `Utilities/` | Pure static helpers (hashing, formatting, filename sanitising) | Stateless |
| `ViewModels/` | `MainWindowViewModel` split into partials by concern | CommunityToolkit.Mvvm source generators |
| `Views/Controls`, `Views/Dialogs`, `Views/Behaviors` | Section user controls, dialogs, attached behaviours | Bind to the single main VM |
| `Localization/` | `UiTextCatalog` + `{ui:UiText}` markup extension | Reads `Resources/Texts/ui-text.en.json` |
| `Resources/Styles/` | `DarkTheme.xaml` (brushes + styles) | Only theme dictionary today |
| `Configuration/` | Encrypted Google client secret (`.enc`, git-ignored) | Never commit secrets |

## Composition root

`App.xaml.cs` builds a Generic Host, loads three option objects manually from `appsettings.json`
(`AppLoggingOptions`, `CloudProviderOptions`, `GameLauncherOptions`), configures Serilog, and
registers everything as **singletons**. `ICloudProvider` resolves to `FolderCloudProvider`
(`Provider: LocalTest`) or `GoogleDriveCloudProvider`.

Startup: `MainWindow.Show()` → `MainWindowViewModel.InitializeAsync()` → scan worlds,
start game monitor timer, prompt for Drive folder setup.

## Core flows

- **Discovery**: `WindroseSaveDiscoveryService` enumerates
  `%LOCALAPPDATA%\R5\Saved\SaveProfiles\<profile>\RocksDB[_v2]\<ver>\Worlds\<worldId>\`.
- **Backup/restore**: `ZipBackupService` zips a world dir + `saveharbor-manifest.json`
  into `%LOCALAPPDATA%\SaveHarbor\backups`. Restore = extract to temp, then replace target.
- **Cloud**: `CloudSyncService` (partials: core status, `.Transfers`, `.Sessions`, `.SessionLocks`)
  orchestrates `ICloudProvider` + `ILocalSyncStateService` + `IBackupService`.
  See `cloud-sync-and-locking`.
- **Play**: `StartGameCommand` → start cloud session (lock) → `IGameLauncherService.LaunchAsync`
  (Steam URI). The game monitor ticks every 5 s and auto-ends the session when the game closes.
  See `play-session-flow`.

## App data locations (`%LOCALAPPDATA%\SaveHarbor\`)

`logs\`, `backups\`, `sync-state\<worldId>.json`, `google-drive-token\`,
`cloud-provider-settings.json`, `cloud-test\` (LocalTest provider root).
**None are game-scoped today** — see `multi-game-separation` for the planned layout.

## Cloud layout (Google Drive shared folder / LocalTest folder)

```
<shared root>/worlds/<worldId>/manifest.json
<shared root>/worlds/<worldId>/versions/<versionId>.zip + .json
<shared root>/worlds/<worldId>/locks/active-session.json
```

## Hardcoded Windrose couplings (must be abstracted for multi-game)

`IWindroseSaveDiscoveryService`, `WindroseWorld`, `WindroseProfile`, `WindroseGameLauncherService`,
`IProcessDetectionService.IsWindroseRunning`, `"Windrose"` literals in `ZipBackupService`,
`GoogleDriveCloudProvider` (manifest `Game`), `CloudWorldManifest.Game` default, VM status/toast
strings, `ui-text.en.json` texts, and `GameLauncherOptions` default Steam URI (app id 3041230).

## Related docs

`IMPLEMENTATION_PLAN.md`, `CLOUD_SYNC_IMPLEMENTATION_PLAN.md` (historical),
`docs/plans/dragonwilds/` (current feature plans).
