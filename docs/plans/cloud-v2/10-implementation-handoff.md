# 10 — Implementation Handoff (read this first)

**Audience:** the implementing agent. **Owner:** the repository owner. **Branch:** `feature/supabase-cloud` (from `develop`).
This file defines *how* to implement plans 01–09. The task files hold the *what*:

| File | Covers |
|---|---|
| [11-tasks-backend-and-provider.md](11-tasks-backend-and-provider.md) | Plan 01 (SQL), 02 (provider model), 03 (Supabase provider) |
| [12-tasks-signin-ux-realtime.md](12-tasks-signin-ux-realtime.md) | Plan 04 (sign-in, groups, Settings), 05 (cache, heartbeat, upload on close, tray, realtime) |
| [13-tasks-updates-release-web.md](13-tasks-updates-release-web.md) | Plan 06 (Velopack), 07 (workflows), 08 (web page) |

Where a task file and an earlier plan differ, **the task file wins**. Plan 09 (testing) is folded into each task's "Done when".

## 1. Rules of engagement

1. Read first: `AGENT.md`, then the skills `git-workflow`, `coding-standards`, `architecture-overview`, `wpf-mvvm-conventions`, `build-and-verify`, `cloud-sync-and-locking`, `multi-game-separation`, `play-session-flow`.
2. **Never run `git commit`, `git add`, `git push`, `git stash`, or create PRs.** Do not switch branches. The owner commits at every **CHECKPOINT** (§4). Leave work as uncommitted changes on `feature/supabase-cloud`.
3. Work **one task at a time, in the order given**. Each task lists files, the change, and a **Done when** check. Run that check before starting the next task.
4. Every task must leave the solution **building** and `dotnet test` green. Compile-driven refactors are fine: change the type, fix errors until it builds.
5. Surgical changes (AGENT.md §5): touch only what the task names. Unrelated defects go as one line into `docs/plans/cloud-v2/FINDINGS.md` (create it if absent).
6. **Packages:** only those named in the tasks: `Velopack` 1.2.161 and, if the build needs it, `System.Security.Cryptography.ProtectedData` 10.0.x. No Supabase SDK, no Newtonsoft, no mocking library, no tray-icon package, no Markdown package.
7. All new user-visible XAML text goes through `Resources/Texts/ui-text.en.json`; view-model strings stay inline as today. Provider name in UI text is "SaveHarbor Cloud"; "Supabase" appears only in the Settings provider description and logs.
8. Placeholder data only (`TEST_GROUP`, `TESTCODE22`, `test@example.invalid`, `12345`, `TEST_WORLD_ID`, `TEST_USER`, `TEST_DEVICE`). Never copy a real e-mail, project ref, key, player name or world name into code, tests, fixtures or docs. The real Supabase URL and publishable key are entered by the owner into `appsettings.json` at CP3; until then the tasks use `https://TEST_REF.supabase.co` and `sb_publishable_TEST`.
9. Never log tokens, codes, invite codes, e-mails of other users, or archive contents. Log keyword `CloudAuth` logs events, not payloads.
10. **Stop and ask the owner** (do not guess) when: a **[verify]** item decides behaviour (listed in §5); a task needs a tool or SDK installed (`supabase`, `vpk`, Docker, Node); a Velopack/Supabase API differs from the plan in a way that changes the design; two tasks conflict; a test cannot be made deterministic.
11. File size: every new file under ~300 lines; split into `Type.Concern.cs` partials rather than exceeding 400.
12. No `TODO` comments, commented-out code, or dead code from your own changes. Remove usings your change made unused. `WriteSessionLockAsync` is deleted in T02.6 once nothing calls it.
13. The owner runs anything that touches the hosted Supabase project (`supabase link`, `db push`, dashboard settings) and anything that installs software. Tasks say "**Owner step**" where that applies; write the exact command for them.

## 2. Fixed decisions (assume these; the owner can override)

| Topic | Decision |
|---|---|
| Provider kinds | `"GoogleDrive"`, `"Supabase"`, `"LocalTest"` (dev only); stored in `AppSettings.CloudProvider`; empty = not chosen |
| Supabase client | hand-written over `HttpClient`; folder `Infrastructure/Cloud/Supabase/`; namespace `SaveHarbor.App.Infrastructure.Cloud.Supabase` |
| Sign-in | e-mail + 6-digit code (`/auth/v1/otp` then `/auth/v1/verify` type `email`); session DPAPI-protected at `%LOCALAPPDATA%\SaveHarbor\supabase-session.bin` |
| Group per game | `LocalCloudProviderSettings.ActiveGroups[gameKey] = groupId`; "use for both games" default on when the other game has none |
| Lock identity | `AppSettings.DeviceId` GUID; `CloudSessionLock.DeviceId`; `IsOwnLock` prefers `DeviceId`, falls back to `MachineName` |
| Lock TTL / heartbeat | 20 min / every 5 min, all providers |
| Retention default | Supabase 10, Drive "keep all" (0), via `CloudProviderCapabilities.DefaultRetentionCount` |
| Max archive | 45 MB for Supabase (`MaxArchiveBytes = 47185920`) |
| Velopack | packId `SaveHarborApp`; `GithubSource` on the public repo; check 10 s after start and every 6 h; auto-download; apply on restart or on exit; never while the game runs |
| App icon | none exists today (`Resources/` has Fonts, Styles, Texts, Themes). T13.1 asks the owner for `Resources/saveharbor.ico`; until provided, `vpk pack` runs without `--icon` and `<ApplicationIcon>` is omitted |
| Realtime | after the MVP (CP6), not before |
| Web page | last (CP8); React + Vite + TypeScript under `web/`, GitHub Pages |
| Version | csproj `<Version>` stays 2.2.2 during the feature branch; the owner bumps to 2.3.0 when tagging |

## 3. Conventions for the new code (summary of the skills, with the exact shapes)

- Namespaces follow folders (file-scoped). New: `SaveHarbor.App.Infrastructure.Cloud`, `.Cloud.Supabase`, `.Updates`, `.Migrations` (exists).
- Concrete classes `sealed`; records for immutable values; async methods end with `Async` and take `CancellationToken cancellationToken = default` last.
- DTOs for Supabase JSON: `sealed class` with `[JsonPropertyName("snake_case")]`, `{ get; set; }`, nullable where the column is nullable; deserialise with a shared `JsonSerializerOptions` (`PropertyNameCaseInsensitive = false`, `DefaultIgnoreCondition = WhenWritingNull`, `MaxDepth = 16`).
- Local JSON documents (settings, cache) stay PascalCase with `SchemaVersion`, like today.
- Logging: `IAppLogger` + `AppLogKeyword`; new keyword `CloudAuth` added to the enum **and** `appsettings.json` together.
- UI feedback triple in view models: `StatusText`, `AddActivity`, toast. Register every new command in the existing `NotifyCommandStates()`.
- Dialogs go through `IDialogService` (add `ConfigureCloudGroup`, `ChooseCloudProvider`, `ShowReleaseNotes`), skinned with the active game's dictionary like `CloudFolderSetupWindow`.
- Tests: xUnit v3 in `SaveHarbor.Tests`, fakes in `Support/`, temp dirs via `TempDirectory`, fixtures as JSON files under `Cloud/Supabase/Fixtures/` with placeholder data.

## 4. Order and checkpoints

```
CP0  T01  Supabase SQL (migrations, tests, seed) + repo layout       → owner: supabase init/link, db push to the new project
CP1  T02  Provider model: selector, capabilities, device id,
          TryAcquire/snapshot in Drive + LocalTest, sync-state scoping → owner commits (Drive smoke test by hand)
CP2  T03  Supabase provider: http, auth, session store, rest,
          storage, provider, group service, error mapper, tests      → owner commits
CP3  T04  Sign-in + group dialog, provider choice, Sharing/Settings → owner enters real URL/key, both-PC smoke test; commits
          === MVP: friends can share through SaveHarbor Cloud ===
CP4  T06  Velopack + update service + banner + version gate          → owner commits
CP5  T07  Workflows, CHANGELOG, README release checklist             → owner tags v2.3.0 → first installer release
CP6  T05a Snapshot cache, coalesced refresh, no-op skip,
          heartbeat, upload on close, tray, single instance          → owner commits
CP7  T05b Realtime client + presence + toasts                        → owner commits
CP8  T08  Companion web page + pages workflow                        → owner commits
```

At every checkpoint: `dotnet build SaveHarbor.sln -c Debug` with no new warnings, `dotnet test SaveHarbor.sln`, and the manual checks named in the task. Then **stop and report**: files changed, tests added, open [verify] items, anything skipped and why.

## 5. [verify] items (ask before relying on them)

| Id | Question | Where it matters |
|---|---|---|
| V1 | Does `ProtectedData` compile on `net10.0-windows` without the `System.Security.Cryptography.ProtectedData` package? | T03.2 |
| V2 | Exact Phoenix/Realtime v1 message framing and the `access_token` refresh event name | T05b.1 |
| V3 | `vpk pack` / `vpk upload github` flag names in the installed `vpk` version | T13.4 |
| V4 | Does Velopack discourage `PublishSingleFile`? (plan assumes yes: publish as folder) | T13.3 |
| V5 | Does a weekly REST ping keep a free Supabase project active? | after the first idle week; T13.5 |
| V6 | Built-in Supabase e-mail sender rate limit on the free plan | T04 smoke test; plan 07 §6 |
| V7 | Supabase storage object `DELETE` body shape (`prefixes`) and the 100-key limit | T03.5 |

## 6. Reporting format at a checkpoint

```
CPn — <task ids> done
Build: 0 errors, <n> warnings (baseline <m>)
Tests: <passed>/<total>
Files: <list>
Owner steps required now: <commands>
[verify] still open: <ids>
Skipped / deviations: <none or list with reason>
```
