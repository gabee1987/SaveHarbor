# 13 — Tasks: Updates, Release Pipeline, Companion Web (Plans 06, 07, 08)

Read [10-implementation-handoff.md](10-implementation-handoff.md) first. `App/` = `SaveHarbor.App/`, `Tests/` = `SaveHarbor.Tests/`.

---

## T06 — Velopack updates (CP4) · plan 06

### T06.1 Project and entry point
- `App/SaveHarbor.App.csproj`: `<PackageReference Include="Velopack" Version="1.2.161" />`, `<StartupObject>SaveHarbor.App.Program</StartupObject>`. No `<ApplicationIcon>` until the owner supplies `Resources/saveharbor.ico` (**owner step**, report it).
- `App/Program.cs` as in plan 06 §2 (`VelopackApp.Build()…Run()` first, then single-instance guard if T05a.6 exists, then `new App()`). Confirm `App.xaml` has no `StartupUri`; if it has one, keep it (the generated `Main` is no longer used, `StartupUri` still applies to the `App` instance).
- `dotnet run` must still work (`VelopackApp.Run()` is a no-op when not installed).

### T06.2 Service
- `App/Services/IAppUpdateService.cs` (plan 06 §2) + `Domain/AppUpdateInfo.cs`, `Domain/AppUpdateState.cs`.
- `App/Infrastructure/Updates/VelopackUpdateService.cs(UpdateOptions options, IAppSettingsStore settings, IProcessDetectionService processes, IActiveGameContext activeGame, IAppLogger logger, Func<IUpdateSource> sourceFactory)`: wraps `UpdateManager`; `IsInstalled = manager.IsInstalled`; `CheckAsync` → `CheckForUpdatesAsync()`; skip when `TargetFullRelease.Version == SkippedUpdateVersion`; `DownloadAsync` → `DownloadUpdatesAsync(info, p => progress.Report(p))`; `ApplyAndRestart` → `ApplyUpdatesAndRestart(info)` only when the active game is not running; `ApplyOnExit` → `WaitExitThenApplyUpdates(info)`; timer for `CheckIntervalHours`. Source factory default: `new GithubSource(options.GitHubRepository, null, false)`; `--update-source <folder>` argument → `SimpleFileSource(new DirectoryInfo(folder))`.
- `UpdateOptions` (`appsettings.json` → `SaveHarbor:Updates`): `GitHubRepository`, `CheckIntervalHours = 6`, `AutoDownload = true`; loaded by `AppOptionsLoader.LoadUpdateOptions`.
- Register in `App.xaml.cs`; `MainWindowViewModel.InitializeAsync` schedules the first check 10 s after the window is shown (`Task.Delay`, non-blocking, exceptions logged under a new `AppLogKeyword.Updates`).

### T06.3 UI
- `MainWindowViewModel.Updates.cs` (new partial): `UpdateState`, `UpdateBannerText`, `UpdateProgress`, commands `RestartForUpdateCommand`, `LaterCommand`, `ShowReleaseNotesCommand`, `SkipUpdateCommand`, `CheckForUpdatesCommand` (Settings). "Restart now" while the game runs → info dialog + `ApplyOnExit`.
- `Views/Shell/GameShellView.xaml`: banner strip above the header bound to `UpdateState` (Available/Downloading/Ready), with the three actions; collapses on "Later" leaving a dot on the Settings tab.
- `IDialogService.ShowReleaseNotes(string title, string markdown)` → `AppDialogWindow` with a `FlowDocumentScrollViewer`; `Utilities/MarkdownLite.cs` converts `#`, `##`, `-`/`*` bullets, `**bold**`, blank-line paragraphs into a `FlowDocument` (≤ 120 lines, no package).
- Footer: `AppVersionText` becomes "v2.3.1 · up to date" / "v2.3.1 · 2.4.0 ready"; Settings "Check for updates" button and "Updates are available in the installed version." when `!IsInstalled`.
- Texts: `Update.*` keys.

### T06.4 Minimum version gate
- `CloudSyncService.RefreshStatusAsync`: when `connection.Reason == "UpdateRequired"` → state `UpdateRequired` (before `NotConnected`) with title "Update required" and detail from plan 04 §4; `IsUploadAllowed`/session commands blocked in that state (extend the existing `CanExecute` guards).
- Drive and LocalTest: a manifest or lock with `SchemaVersion` greater than the app's known maximum (`CloudSchema.MaxKnownManifestVersion = 2`, `MaxKnownLockVersion = 2` in `Domain/CloudSchema.cs`) → `UpdateRequired` for that world.
- Sharing hero row "Update required" with the **Update** button → `CheckForUpdatesCommand`.

### T06.5 Tests
`Updates/VelopackUpdateServiceTests` using a fake `IUpdateSource` (Velopack's `IUpdateSource` interface: `GetReleaseFeed`, `DownloadReleaseEntry`; a `FakeUpdateSource` returning a feed with one newer asset), `Updates/MarkdownLiteTests`, `Cloud/MinimumVersionGateTests`.

**Done when:** build clean; tests pass; `dotnet run` shows "Updates are available in the installed version."; **owner step** (or agent if `vpk` is installed): `vpk pack` two local versions into a folder, install the first, start with `--update-source <folder>`, see banner → download → restart → second version. Record the outcome in FINDINGS.md.

### CHECKPOINT CP4 → stop, report.

---

## T07 — Release pipeline (CP5) · plan 07

### T07.1 CI
`.github/workflows/ci.yml` as plan 07 §1: `windows-latest`, `actions/setup-dotnet@v4` with `global-json-file`, restore, build Release with `-warnaserror:false`, count warnings from the build log and fail if above `WARNING_BASELINE` (env, set to the current count), `dotnet test`.

### T07.2 Release
`.github/workflows/release.yml` from plan 07 §3; `build/Get-ReleaseNotes.ps1` (extracts the `## [X.Y.Z]` section from `CHANGELOG.md`, fails when missing). **V3/V4** apply: verify flags with `vpk --help` output pasted into FINDINGS.md.

### T07.3 Supabase and keep-alive
`.github/workflows/supabase.yml` (`supabase/setup-cli@v1`, `supabase start`, `supabase db reset`, `supabase test db`, `supabase db lint`) and `.github/workflows/keepalive.yml` (weekly `curl -fsS -H "apikey: $KEY" "$URL/rest/v1/app_meta?select=key"` using repository variables).

### T07.4 CHANGELOG, README, runbook
- `CHANGELOG.md` with `## [Unreleased]` and a prepared `## [2.3.0]` section listing: SaveHarbor Cloud provider, sign-in with e-mail code, groups and invite codes, atomic locks and heartbeats, automatic updates, installer; and the one-time "install the new build" instruction for friends (plan 06 §7).
- README: replace "Build a zip to share with friends" with the release checklist (plan 07 §5) and a "What a friend does" section for the installer; keep the portable zip mention; add "Where data is stored" rows for `supabase-session.bin`, `cloud-cache\`, and the Velopack install folder; privacy note for the update check and for SaveHarbor Cloud (plan 00 §6).
- `.gitignore`: add `Releases/`, `artifacts/` (exists), `web/node_modules/`, `web/dist/`, `supabase/.temp/`, `supabase/.branches/`.

### T07.5 Tests
None automated beyond the workflows themselves. **Owner step:** push the branch, confirm `ci` and `supabase` workflows pass on the PR; after merging, tag `v2.3.0` and confirm the release assets.

**Done when:** workflow files lint (`actionlint` if available; otherwise careful review), CHANGELOG and README updated, FINDINGS.md records the `vpk` flag verification.

### CHECKPOINT CP5 → stop, report. (First installer release follows, by the owner.)

---

## T08 — Companion web page (CP8) · plan 08

**Owner step before starting:** Node.js LTS installed; confirm in the report.

### T08.1 Scaffold
`web/` with Vite + React 19 + TypeScript (`npm create vite@latest web -- --template react-ts`), `@supabase/supabase-js@^2`, `vitest`. `vite.config.ts` `base: '/SaveHarbor/'`. `.env.example` with `VITE_SUPABASE_URL=https://TEST_REF.supabase.co` and `VITE_SUPABASE_PUBLISHABLE_KEY=sb_publishable_TEST`. CSP meta tag from plan 08 §5. No UI framework; `src/styles.css` with the colour tokens copied from `Resources/Themes/Windrose.Colors.xaml` and `Dragonwilds.Colors.xaml` (documented at the top).

### T08.2 Features
- `src/lib/supabase.ts` (client), `src/lib/snapshot.ts` (types + `mapSnapshot`), `src/lib/lockText.ts` (pure: lock state → text, uses `server_time`).
- Routes (react-router or a tiny hash router; choose hash router to avoid Pages 404 handling): sign-in, groups, group (tabs per game, world cards, live dot), world (version history).
- Realtime channel `saveharbor:<groupId>` with the same `postgres_changes` filters as the desktop app; presence join with `{ display_name, device_label: 'web', game }`.
- Owner-only invite code card (`rpc('get_invite_code')`), copy buttons for code and `saveharbor://join/<code>`.

### T08.3 Tests and workflow
Vitest for `mapSnapshot` and `lockText`; `.github/workflows/pages.yml` (`actions/setup-node@v4`, `npm ci`, `npm test`, `npm run build`, `actions/upload-pages-artifact`, `actions/deploy-pages`) with the two repository variables as build env. **Owner step:** enable Pages (source: GitHub Actions) in the repository settings.

**Done when:** `npm test` and `npm run build` pass locally; the page runs against the local Supabase stack (`supabase start`) with the seed group and shows worlds and locks; the owner confirms the Pages URL after the first deploy.

### CHECKPOINT CP8 → stop, report.
