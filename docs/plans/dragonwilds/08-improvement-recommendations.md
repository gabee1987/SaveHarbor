# Improvement Recommendations

**Date:** 2026-10-05. **Basis:** code review of `develop` @ `0dbf842`.
Items already covered by plans 01–07 are cross-referenced instead of repeated in full. Priority: **P1** = protects save data or prevents lost progress; **P2** = noticeably improves reliability or UX; **P3** = polish and maintainability. Effort: S < ½ day, M ≈ 1–2 days, L > 2 days.

## 1. Reliability and data safety

| # | Recommendation | Evidence | Pri | Effort | Branch |
|---|---|---|---|---|---|
| R1 | **Auto-upload after the game closes**, with retry and a persistent "not shared yet" banner | Game close only clears the lock (`MainWindowViewModel.GameMonitor.cs`) → progress silently stays local | P1 | M | plan 05 §3.6 |
| R2 | **Verified lock acquisition** (write → wait → re-read) and deterministic resolution of duplicate lock files | Read-then-write in `CloudSyncService.Sessions.cs` | P1 | M | plan 05 §3.3 |
| R3 | **Lock heartbeat** and a shorter TTL | `LastHeartbeatAtUtc` never updated; 6 h fixed expiry | P1 | S | plan 05 §3.4 |
| R4 | **Path-traversal hardening** for `WorldId` and `ArchiveFileName` from shared JSON | `CloudSyncService.Transfers.cs`, `ZipBackupService.ImportBackupAsNewWorldAsync` | P1 | S | plan 04 §7 → `fix/untrusted-path-segments` |
| R5 | **Single-instance guard** (named `Mutex` `Local\SaveHarbor`); a second launch brings the existing window to the front | No guard: two instances can both monitor, auto-end sessions, or upload | P1 | S | `fix/single-instance` |
| R6 | **Enforce cloud retention** (`KeepLatestVersions = 20`): after a successful upload, delete versions older than the newest 20 | `CloudRetentionSettings` is declared but never used → Drive usage grows without bound | P2 | S | `fix/cloud-retention` |
| R7 | **Prune local backups** per game and world (for example keep the last 10 automatic backups, keep manual ones forever) | Every upload and download creates a zip; nothing is ever deleted | P2 | S | `feature/backup-retention` |
| R8 | **Crash-safe directory restore**: rename target → `.saveharbor-old`, move staging into place, then delete old. On startup, recover any leftover `.saveharbor-old` or `-staging` folders | `ZipBackupService.ReplaceDirectory` deletes the target before moving staging in; a crash in between leaves no world folder (the pre-restore zip still exists, but recovery is manual) | P2 | S | plan 03 §5 (generalised restore) |
| R9 | **Free-space check** before backup, download and restore (needs about 3× the world size: zip, temp extract, staging) | Not checked; a full disk fails mid-operation | P2 | S | `fix/disk-space-check` |
| R10 | **Reliable shutdown**: `OnExit` is `async void` and awaits `_host.StopAsync()`. WPF can end the process before `Log.CloseAndFlush()` runs, so the last log lines are lost. Use synchronous `StopAsync().GetAwaiter().GetResult()` with a short timeout in `OnExit`, or `Closing`-time cleanup | `App.xaml.cs:84` | P2 | S | `fix/shutdown-flush` |
| R11 | **Retry with backoff** for transient Drive errors (HTTP 429, 5xx, network) on manifest, lock and version calls | No retry policy; one transient failure aborts Play | P2 | S | plan 05 or `fix/drive-retry` |
| R12 | **Skip no-op uploads** (payload hash unchanged since the launched version) | Every upload creates a new version even when nothing changed | P3 | S | plan 03 §6 |

## 2. Ease of use (UX)

| # | Recommendation | Pri | Effort | Where |
|---|---|---|---|---|
| U1 | **One primary Play button** that does everything (pull → lock → launch → share). Move manual Upload, Download and session buttons into a collapsed "Advanced" section | P1 | M | plan 05 §3.9 |
| U2 | **Configurable display name** (first-run prompt, stored in `app-settings.json`). Today friends see `Environment.UserName`, which is often "User" or "Admin" and is also personal data | P2 | S | plan 02 (app-settings) |
| U3 | **First-run wizard**: (1) pick a game, (2) connect Google Drive, (3) paste the group folder link and test it, (4) pick your world or download the group's world. It replaces the current sequence of dialogs at startup | P2 | M | `feature/first-run-wizard` |
| U4 | **Per-game desktop shortcuts** ("SaveHarbor – Dragonwilds" → `--game dragonwilds`), offered once from settings. Optionally `--play`, which presses Play automatically: "double-click and play" | P2 | S | plan 02 §2.4 |
| U5 | **Clear "who's hosting" presence** in the header: "Player2 is hosting · since 20:14 · v14". Optionally a Windows toast when the lock is released ("World is free — Player2 shared v15") using the existing 5 s timer to poll every ~60 s while the app is idle | P2 | S | plan 05 §3.9 |
| U6 | **Version history and rollback** in Advanced: list cloud versions (who, when, size), with "Restore this version" (creates a backup first, then publishes as a new version so history stays linear) | P2 | M | `feature/version-history` |
| U7 | **Tray mode**: minimise to tray while the game runs; the tray icon shows Playing, Sharing or Shared | P3 | M | `feature/tray-mode` |
| U8 | **Friendly error texts with next steps**. The dialog today shows `Code`, `ErrorId` and the technical message to the user; move technical details behind a "Details" expander and add "Copy diagnostics" | P3 | S | `chore/error-dialog` |
| U9 | **Diagnostics export**: one button that zips the logs and the sanitised settings (no tokens or secrets) for troubleshooting a friend's issue | P3 | S | `feature/diagnostics-export` |

## 3. Consistency and maintainability

| # | Recommendation | Evidence | Pri | Effort |
|---|---|---|---|---|
| C2 | **Move VM strings into `ui-text.en.json`**. Toasts, status and dialog texts are inline in the view model while XAML uses the catalog. A single place makes tone consistent and enables the game-name substitution in plan 02 | `MainWindowViewModel.*.cs` | P2 | M |
| C3 | **Remove the static `ToastService.Current`** used by `CopyOnClick` (a service locator). Pass the toast service through an attached property or a command | `Views/Behaviors/CopyOnClick.cs:64` | P3 | S |
| C4 | **Unify field naming** (`_camelCase` vs `camelCase`) only when files are touched anyway; do not run a mass reformat (AGENT.md §5) | mixed across folders | P3 | — |
| C5 | **Split `GoogleDriveCloudProvider.cs`** (686 lines, at the AGENT.md limit) into partials: `.Auth`, `.Folders`, `.Json`, `.Transfers` before it grows further | file size | P2 | S |
| C6 | **Stop flagging the activity log by string level** (`"Info"`, `"Success"`…): use the existing `ToastKind` or a small `ActivityLevel` enum | `AddActivity(string level, …)` | P3 | S |
| C7 | **Mark historical docs**: `IMPLEMENTATION_PLAN.md` and `CLOUD_SYNC_IMPLEMENTATION_PLAN.md` describe earlier phases. Add a status header or move them to `docs/history/` | repo root | P3 | S |
| C8 | **Dispose `Process` objects** in process detection (`Process.GetProcesses()` returns handles that are never disposed, every 5 s) | `WindowsProcessDetectionService` | P2 | S (plan 05 §3.8) |

## 4. Distribution and operations

| # | Recommendation | Pri | Effort |
|---|---|---|---|
| D1 | **Minimum-version gate in the shared folder**: `saveharbor-app.json` `{ "minVersion": "1.1.0" }`. Older clients refuse to write (read-only mode, with "update SaveHarbor" text). This prevents a friend on an old build from bypassing new lock rules | P2 | S |
| D2 | **Versioned releases**: bump `<Version>` per release, show the version in the header tooltip, and keep a `CHANGELOG.md`. The owner tags releases manually | P3 | S |
| D3 | **Code-signing** is optional (cost). Without it, SmartScreen warns friends; document "More info → Run anyway" in a short `README.md` for friends | P3 | S |
| D4 | **The `Testing/` folder contains real save archives** (one file name looks like a real Steam ID). It is git-ignored, which is good; keep it that way and use placeholder names for any fixture that is committed (plan 07) | P2 | — |

## 5. Suggested order

1. **Quick wins before the big feature** (each a small `fix/` branch, all valuable for Windrose today): R5 single instance, R4 path safety, R3 heartbeat, R10 shutdown flush, C8 process disposal, R6 cloud retention.
2. The Dragonwilds roadmap (plans 01 → 07 → 02 → 04 → 03/06 → 05), which already contains R1, R2, U1, U2, U4 and R12.
3. Afterwards: U6 version history, U3 first-run wizard, R7 local retention, D1 minimum-version gate, and the remaining polish items.

## 6. Compliance notes

- U2 reduces personal data in shared files (display name instead of the OS account name). Recommended under data minimisation.
- Google Drive and any future update or distribution channel (for example GitHub Releases for D1/D2) are external services. They are acceptable for a private hobby group, but they would need review in an organisational setting.
