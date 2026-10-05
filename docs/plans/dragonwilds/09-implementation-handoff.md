# 09 — Implementation Handoff (read this first)

**Audience:** the implementing agent (Sonnet). **Owner:** Gabor. **Branch:** `feature/dragonwilds-support`.
This file defines *how* to implement plans 01–07. The task files hold the *what*:

| File | Covers |
|---|---|
| [10-tasks-foundation.md](10-tasks-foundation.md) | Plan 01 (upgrade), 07 (tests), 02 (game abstraction) |
| [11-tasks-scoping-and-dragonwilds.md](11-tasks-scoping-and-dragonwilds.md) | Plan 04 (scoping, migration, path safety), 03 (Dragonwilds) |
| [12-tasks-play-flow-and-theme.md](12-tasks-play-flow-and-theme.md) | Plan 05 (Play flow, locks), 06 (theme) |

Where a task file and an earlier plan differ, **the task file wins** (it was written after a second pass through the code).
Plan 08 (improvements) is optional and is not part of this handoff unless the owner asks.

## 1. Rules of engagement

1. Read first: `AGENT.md`, then the skills `git-workflow`, `coding-standards`, `architecture-overview`, `wpf-mvvm-conventions`, `build-and-verify`, plus any game/topic skill named by the task.
2. **Never run `git commit`, `git add`, `git push`, `git stash`, or create PRs.** Do not switch branches. The owner commits at every **CHECKPOINT** (see §4). Leave work as uncommitted changes on `feature/dragonwilds-support`.
3. Work **one task at a time, in the order given**. Each task lists files, the change, and a **Done when** check. Run that check before starting the next task.
4. Every task must leave the solution **building**. If a rename is mechanical and large, do it compile-driven (change the type, fix errors until it builds).
5. Surgical changes (AGENT.md §5): touch only what the task names. If you find an unrelated defect, add one line to `docs/plans/dragonwilds/FINDINGS.md` (create it if absent) and move on.
6. No new NuGet packages other than those the tasks name. No mocking library, no analyzers, no formatting tools.
7. All new/changed user-visible text goes through `Resources/Texts/ui-text.en.json` for XAML; view-model strings stay inline unless the task says otherwise (plan 08 C2 is out of scope).
8. Placeholder data only (`12345`, `TEST_WORLD_ID`, `TEST_USER`, `TEST_MACHINE`). Never copy a real Steam ID, player name, folder ID or file name from `Testing/` or the owner's disk into code, tests, or docs.
9. Never log secrets, tokens, Drive folder links, or full world file contents.
10. **Stop and ask the owner** (do not guess) when: a **[verify]** item decides behaviour and Phase 0 (T03.0) has not been recorded; a task would need a package/SDK installed; two tasks conflict; a test cannot be made deterministic.
11. File size: keep every new file under ~300 lines; split into `Type.Concern.cs` partials rather than exceeding 400. `GoogleDriveCloudProvider.cs` is already 686 lines: **split it (task T04.0) before adding anything to it.**
12. Do not leave `TODO` comments, commented-out code, or dead code from your own changes. Remove usings your change made unused.

## 2. Fixed decisions (owner can override; assume these)

| Topic | Decision |
|---|---|
| App shape | One exe; game switch in header; `--game windrose|dragonwilds` CLI argument; last game remembered in `app-settings.json` |
| Cloud | Separate shared folder per game; folder marker file `saveharbor-game.json`; Windrose keeps its existing `worlds/…` layout |
| Dragonwilds platform | Steam only in v1; `SaveRoot` override in options for anything else |
| Font | Cinzel via embedded `Resources/Fonts/`. **Do not download fonts.** If the files are absent, the FontFamily fallback (`Georgia`) applies automatically. The owner adds the files (see T06.3) |
| Upload after game exit | Automatic, with retry and a persistent banner on failure |
| Process matching | Windrose keeps substring matching (`Windrose`, `R5`) until the owner verifies exe names; Dragonwilds uses exact names |
| Display name | Still `Environment.UserName` (plan 08 U2 not in scope) |
| Version | Leave `<Version>` unchanged; the owner bumps at release |

## 3. Conventions for the new code (summary of the skills, with the exact shapes)

- Namespaces: `SaveHarbor.App.Domain`, `.Services`, `.Infrastructure`, `.Infrastructure.Games`, `.Infrastructure.Games.Windrose`, `.Infrastructure.Games.Dragonwilds`, `.Infrastructure.Migrations`, `.Utilities`.
- Folder = namespace (file-scoped namespaces).
- Concrete classes `sealed`. Records for immutable values. Async methods end with `Async` and take `CancellationToken cancellationToken = default` last.
- JSON documents: `sealed class` with `{ get; set; }`, `SchemaVersion`, default values; serialised with `JsonSerializerOptions { WriteIndented = true }`; **property names stay PascalCase** (the existing files are PascalCase; reading uses `PropertyNameCaseInsensitive = true` where the current code does).
- Storage keys are lowercase: `"windrose"`, `"dragonwilds"`. Compare keys with `StringComparison.OrdinalIgnoreCase` (legacy manifests contain `"Windrose"`).
- Logging: `IAppLogger` + `AppLogKeyword`. Add new keywords to the enum **and** to `appsettings.json` `KeywordMinimumLevels` together.
- UI feedback triple in view models: `StatusText`, `AddActivity`, toast (see `wpf-mvvm-conventions`).
- Register every new command in `NotifyCommandStates()`.

## 4. Order and checkpoints

```
CP0  T01  Dependency upgrade                    → owner commits  (chore: upgrade to .NET 10)
CP1  T07a Test project skeleton + fixtures      → owner commits
CP2  T02  Game abstraction (Windrose only)      → owner commits  (smoke test Windrose by hand)
CP3  T04  Scoping, migration, path safety       → owner commits
CP4  T03  Dragonwilds integration               → owner commits  (needs T03.0 Phase 0 results)
CP5  T06  Theme + game switch UI                → owner commits
CP6  T05  Play flow + locks                     → owner commits
```

At each checkpoint, **stop** and give the owner this report: what changed (files), what verification ran (exact commands + results), what could not be verified, and any risk. Do not start the next phase until the owner says to continue.

T06 (theme) can be done before T03 if the owner prefers; the tasks do not depend on each other beyond T02.

## 5. Definition of done (every task)

- [ ] `dotnet build SaveHarbor.sln -c Debug` succeeds; warning count not higher than the baseline recorded in T01.
- [ ] `dotnet test SaveHarbor.sln` passes (from CP1 onward).
- [ ] Task's **Done when** check performed and result stated.
- [ ] No file touched outside the task's list without a one-line justification in the checkpoint report.
- [ ] Windrose behaviour unchanged unless the task says otherwise (verify by the smoke test in `build-and-verify` at CP2, CP3, CP5, CP6).

## 6. Code facts the tasks rely on (verified against `develop` @ `0dbf842`)

- `CloudProviderOptions` is a **mutable singleton** (`GoogleSharedFolderId` is set at runtime by `CloudProviderSettingsService.SaveSharedFolderAsync`). `GoogleDriveCloudProvider.FindFolderPathAsync` reads `options.ResolveGoogleSharedFolderId()` on every call; there is **no** cached root-folder id in the provider.
- `ICloudSetupService` + `CloudProviderSettingsService` persist `LocalCloudProviderSettings { GoogleSharedFolderId }` to `IAppDataPathProvider.CloudProviderSettingsPath`; `App.LoadCloudProviderOptions()` reads it at startup.
- `FolderCloudProvider` (LocalTest) roots everything at `pathProvider.LocalTestCloudRoot`; layout `worlds/<id>/{manifest.json,versions/,locks/active-session.json}`; writes use `WriteJsonAtomicAsync`.
- `ZipBackupService`: `BackupRoot` = `%LOCALAPPDATA%\SaveHarbor\backups`; archive = `saveharbor-manifest.json` + `world/` directory; manifest written as an anonymous object (SchemaVersion 1, Game "Windrose", `PayloadSha256` from `DirectoryHashCalculator`); restore = `CreateBackupAsync(..., "pre-restore")` → extract to temp → `ReplaceDirectory` (**deletes target then moves staging**).
- `LocalJsonSyncStateService` builds its own root path (does not use `IAppDataPathProvider`), file = `<worldId>.json` via `GetStatePath(worldId)` (sanitised).
- `FileNameSanitizer.MakeSafeFileName` only removes invalid filename chars: it returns `".."` unchanged and does **not** reject `.`/`..`. Do not rely on it alone for path safety (T04.1 adds `SafePath`).
- `MainWindowViewModel` is registered as a singleton; the toast event handler is wired in its constructor; game monitor is a `DispatcherTimer` (5 s).
- `HeaderStatusView.xaml` uses a fixed 780 px right-hand grid with 5 chips; adding the game switch requires re-flowing it (T06.5).
- `App.xaml.cs` (283 lines) contains three option loaders that each rebuild a `ConfigurationBuilder`.
- `FolderCloudProvider` implements only `ICloudProvider` (not `ISharedFolderCloudProvider`). `NotConfiguredCloudProvider` exists but is **not registered** in DI.
- `SerilogAppLogger` filters per keyword via `AppLoggingOptions.IsEnabled`, then logs with `Log.ForContext("Keyword", …)`.
- Code-behind resolves brushes only through `TryFindResource(...) as Brush` (`WpfDialogService`, `CloudFolderSetupWindow.xaml.cs`), so non-solid brushes such as the Dragonwilds radial background are safe.
- `ZipBackupService` and `LocalJsonSyncStateService` have parameterless constructors today; adding `IAppDataPathProvider` works because it is registered as an instance.
- `StartGameCommand` is referenced in `MainWindowViewModel.cs:35` (attribute), `MainWindowViewModel.Operations.cs:58`, and `ActionsSectionView.xaml:45`.

## 7. Review log

**2026-10-05: review against `develop` @ `0dbf842`.** Corrected in the task files:
- **Test project:** it needs `OutputType Exe` (xunit v3) and `UseWPF` (so the Windows Desktop runtime loads).
- **Restored world record:** `GameWorld` construction in `DownloadLatestAvailableAsync` now keeps today's values (`"Unknown"`, `MinValue`).
- **Dictionary initialiser:** `Dictionary` uses `new()`, not `[]`.
- **Fake provider hook:** renamed to `AfterLockWrite`, since only an after-write hook can simulate the race.
- **Dragonwilds discovery:** it filters on the exact `.sav` extension. The Windows `*.sav` pattern also matches longer extensions.
- **GVAS check:** the unused header-check constant was removed.
- **Single-file restore:** it refuses a backup whose file name differs from the target, so it cannot create a duplicate world.
- **Drive locks:** reading every duplicate lock file needs a `DownloadJsonByIdAsync` helper.
- **Finishing after exit:** it checks for a foreign lock (`LockLost`) before uploading, and runs inside `RunBusyAsync`.
- **After launch:** the cloud status must be refreshed, or the heartbeat and finish steps never trigger.
- **"Get shared world":** this case no longer depends on `CloudStatus`, which is null when no world is selected.
- **Font:** the font URI uses `/SaveHarbor.App;component/…`, because the commas in `pack://application:,,,` break the fallback list. The XAML forms for the non-brush resources are given explicitly.
- **Publish check:** the output folder is no longer deleted automatically (AGENT.md §10).
- **Placeholder world ID:** the SafePath test sample uses a placeholder.
