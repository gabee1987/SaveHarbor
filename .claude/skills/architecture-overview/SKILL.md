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
| `Views/Sharing`, `ViewModels/Sharing` | The Sharing page of Settings (`SharingView`, a fresh `SharingViewModel` each time Settings opens; closing Settings reads the cloud again after a change): provider choice, SaveHarbor Cloud sign-in, the group (members, invite code, storage), the Drive folder, the player's name | `SharingViewModel` with `CloudOnboardingViewModel`, `SharingGroupViewModel`, `SharingProfileViewModel`; styles in `SharingStyles.xaml` |
| `Localization/` | `UiTextCatalog` + `{ui:UiText}` markup extension | Reads `Resources/Texts/ui-text.en.json` |
| `Resources/Styles/` | `DarkTheme.xaml` (brushes + styles) | Only theme dictionary today |
| `Configuration/` | Encrypted Google client secret (`.enc`, git-ignored) | Never commit secrets |

## Composition root

`App.xaml.cs` builds a Generic Host, loads three option objects manually from `appsettings.json`
(`AppLoggingOptions`, `CloudProviderOptions`, `GameLauncherOptions`), configures Serilog, and
registers everything as **singletons**. `ICloudProvider` resolves to `CloudProviderSelector`, which
forwards to the provider chosen on this PC (`AppSettings.CloudProvider`: Google Drive or SaveHarbor Cloud) or, with
`DeveloperProviderOverride: LocalTest`, to `FolderCloudProvider`. SaveHarbor Cloud lives in
`Infrastructure/Cloud/Supabase/` (`SupabaseCloudProvider`, `SupabaseGroupService` as `ICloudGroupService`, and their
HTTP, auth, REST and storage clients, and `SupabaseRealtimeClient` as `ICloudLiveUpdates` for live updates; options
under `SaveHarbor:CloudProvider:Supabase`).

Entry point: `Program.Main` (the csproj `StartupObject`) runs `VelopackApp.Build().Run()` first, which handles the
installer's and updater's hook arguments and exits for them, then creates `App`. `IAppUpdateService` is
`Infrastructure/Updates/VelopackUpdateService` (GitHub releases, options under `SaveHarbor:Updates`, `--update-source
<folder>` for a local feed); it is `Disabled` outside a Velopack install (`dotnet run`, an unzipped folder).

`Program.Main` also keeps one SaveHarbor per Windows user (`Infrastructure/SingleInstanceGuard`): a second start
(shortcut or `saveharbor://join/<CODE>` link, registered in HKCU by `ProtocolRegistration` from the Velopack hooks or
Settings) hands its arguments to the running app (`App.Activate` → `MainWindowViewModel.HandleActivationAsync`).

Startup: `MainWindow.Show()` → `MainWindowViewModel.InitializeAsync()` → scan worlds, show the cached cloud overview
(`Infrastructure/Cloud/CloudSnapshotCache`, display only, marked "as of HH:mm") and read the live one with
`RefreshCloudAsync` (`MainWindowViewModel.CloudRefresh.cs`: one `ICloudSyncService.RefreshAllAsync` = one connection
check and one snapshot for the Sharing line, the world list and the selected world; requests within 250 ms share
one refresh), start game monitor timer, `PromptCloudSetupIfNeededAsync` (Settings on the Sharing page when no provider is chosen and SaveHarbor
Cloud is configured, or when SaveHarbor Cloud has no group yet; the Drive folder prompt for Google Drive), and an update check 10 s later that
never blocks the UI (`MainWindowViewModel.Updates.cs`: banner, footer version, Settings "Check for updates").

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
