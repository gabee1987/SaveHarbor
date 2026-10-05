# Plan 05 — One-Click Play Flow and Lock Hardening

**Branch:** `feature/dragonwilds-support` (the lock fixes may be split out to `fix/session-lock-race` and `fix/session-lock-heartbeat` if the owner wants them in Windrose sooner).
**Depends on:** 02, 04. Works for both games.
**Skills:** `play-session-flow`, `cloud-sync-and-locking`, `wpf-mvvm-conventions`.

## 1. Objective

Turn "Start game" into a dependable one-click **Play** that always runs the newest shared world, prevents two hosts at once, and shares progress automatically after the game closes.

## 2. Gaps in the current implementation

| # | Gap | Location |
|---|---|---|
| G1 | Play is blocked when the cloud is newer; the user must click Download manually | `CloudSyncService.Sessions.cs` `StartSessionAsync` returns `CloudNewer` |
| G2 | Closing the game only clears the lock; it **does not upload** | `MainWindowViewModel.GameMonitor.cs` `AutoEndCloudSessionAfterGameClosedAsync` |
| G3 | Lock acquisition is read-then-write (race) | `StartSessionAsync` |
| G4 | No heartbeat; fixed 6 h expiry | `CloudSessionLock`, `StartSessionAsync` |
| G5 | No handling for "launch requested but the game never started" | `StartGameAsync` |
| G6 | A first upload is required before Play works on a new world | `StartSessionAsync` (`LatestVersion is null`) |
| G7 | Expired foreign lock is silently ignored; no take-over UX | `IsActiveOtherPlayerLock` |
| G8 | App closed while the game is running → lock is left with no follow-up | `App.OnExit` |
| G9 | Process detection by substring (`R5`) can false-positive | `WindowsProcessDetectionService` |

## 3. Design

### 3.1 New orchestration service

`Services/IPlaySessionService.cs` and `Infrastructure/PlaySessionService.cs` (target under 300 lines; split into partials `.Prepare.cs` and `.Finish.cs` if needed). This moves the flow out of the VM. The VM only renders progress.

```csharp
public interface IPlaySessionService
{
    Task<PlayPreparationResult> PrepareAndLaunchAsync(GameWorld world, IProgress<PlayStage> progress, CancellationToken ct);
    Task<PlayFinishResult> FinishAfterExitAsync(GameWorld world, IProgress<PlayStage> progress, CancellationToken ct);
    Task HeartbeatAsync(GameWorld world, CancellationToken ct);
    Task ReleaseIfNeverStartedAsync(GameWorld world, CancellationToken ct);
}

public enum PlayStage { CheckingCloud, BackingUp, Downloading, Verifying, Restoring, Locking, Launching, WaitingForGame, Playing, WaitingForSaves, Uploading, Releasing, Done }
```

Results are records with `Outcome` (enum: `Launched`, `BlockedSomeonePlaying`, `BlockedConflict`, `NeedsFirstPublishConfirmation`, `NeedsTakeOverConfirmation`, `LaunchFailed`, `NotConnected`, `Failed`) and the data the UI needs (player name, start time, version). They contain **no UI strings**. The VM maps outcomes to text, so all wording lives in one place.

### 3.2 Prepare-and-launch algorithm

1. Refresh status (existing `RefreshStatusAsync`).
2. `NotConnected` → return. The VM triggers `ConnectCloudCommand` and retries once on success.
3. `SomeonePlaying` → return `BlockedSomeonePlaying(lock)`.
4. Foreign lock exists but expired → return `NeedsTakeOverConfirmation(lock)`. After the user confirms, the VM calls again with `allowTakeOver: true`.
5. `Conflict` → return `BlockedConflict`.
6. `ConnectedNoCloudSave` → return `NeedsFirstPublishConfirmation`. After confirmation, `UploadCurrentAsync` runs and the flow continues.
7. `CloudNewer` → `DownloadLatestAsync` (already does backup → download → hash verify → restore). Progress stages are reported. Requires `!IsGameRunning`.
8. **Acquire the lock** (§3.3). If it is lost → `BlockedSomeonePlaying(winner)`.
9. Launch via `IGameLauncherService`. On failure → release the lock and return `LaunchFailed`.
10. Return `Launched`. The VM enters "waiting for game" mode.

### 3.3 Lock acquisition with verification (G3)

```
write lock { LockId = new Guid, StartedAtUtc = now, ExpiresAtUtc = now + LockTtl }
wait LockVerifyDelay (2 s; LocalTest: 200 ms)
re-read lock
if re-read.LockId == mine            → acquired
else if re-read is foreign and active → lost (report re-read.PlayerName)
```
- Drive "upload by name" today **updates the existing file or creates a new one**. When two clients create at the same moment, Drive can end up with **two files named `active-session.json`**. `FindFileIdByNameAsync` then returns an arbitrary one. Mitigation: `GetSessionLockAsync` reads **all** files with that name, picks the winner deterministically (earliest `StartedAtUtc`, then lowest `LockId`), and the loser deletes its own file. Document this in code with a short "why" comment.
- Constants live in `SessionLockPolicy` (static class): `LockTtl = 20 min`, `HeartbeatInterval = 5 min`, `LockVerifyDelay = 2 s`, `GameStartTimeout = 3 min`. Before heartbeats ship, keep the 6 h TTL so that friends on older versions are not affected.

### 3.4 Heartbeat (G4)

- Reuse the existing 5 s `DispatcherTimer`. In `OnGameMonitorTick`, when the game is running, we own the lock, and `now - lastHeartbeat >= HeartbeatInterval` → `HeartbeatAsync` (fire-and-forget with error logging; do not toggle `IsBusy`).
- Heartbeat rewrites the lock with `LastHeartbeatAtUtc = now` and `ExpiresAtUtc = now + LockTtl`. If the re-read shows a foreign `LockId` (someone took over) → persistent warning: "<Player> took over the session. Do not upload; your progress will conflict."
- **Compatibility:** older SaveHarbor builds treat `ExpiresAtUtc` as authoritative, which is fine because the heartbeat keeps it in the future.
- PC sleep is longer than the TTL → the lock expires → a friend may take over only after explicit confirmation (§3.2 step 4). When our app wakes and detects the take-over, it raises the warning above.

### 3.5 Waiting for the game (G5)

After `Launched`, the VM records `launchRequestedAt`. On each tick, if the game is not running and `now - launchRequestedAt > GameStartTimeout` and the game was never observed → `ReleaseIfNeverStartedAsync` and a toast "<Game> did not start. Session released." This replaces the implicit `hasObservedGameRunningDuringSession` handling, which is kept as the "observed" flag.

### 3.6 Finish after exit (G2)

Triggered by the monitor when the game was observed running and is no longer running, and we own the lock:
1. Stage `WaitingForSaves`: wait until the save payload files (`IGameSaveAdapter.GetPayloadFiles`) have had no `LastWriteTimeUtc` change for `game.SaveSettleDelay` (Windrose 10 s, Dragonwilds see plan 03), with a 2 min cap. Poll every 1 s; this is a short-lived loop, not a new timer.
2. Upload via `UploadCurrentAsync`, which already backs up, zips, hashes, publishes `v+1`, and clears our lock.
3. On failure: retry 3 times with backoff (5 s, 15 s, 45 s). After that, **keep the lock**, set `PendingUpload = true` in local sync state, and show a persistent banner "Your progress is not shared yet — Retry". The banner is backed by a VM property; it is not a toast.
4. Upload is blocked by `Conflict` (someone else published meanwhile, which is only possible after a forced take-over) → keep the local files, release nothing, show a conflict dialog explaining that both versions exist and the backup path.

### 3.7 App shutdown during a session (G8)

- In `MainWindow.Closing`, if we own the lock and the game is running → confirm: "The game is still running. If you close SaveHarbor, your progress will not be shared automatically. Close anyway?"
- On next start (`InitializeAsync`): if the selected world has our own lock, the game is not running, and the local files are newer than `LastDownloaded/UploadedAtUtc` → banner "Share your last session now?" (one click → finish flow). If the local files are not newer → silently release the lock.

### 3.8 Exact process detection (G9)

`IGameDefinition.ProcessNames` holds exact names (case-insensitive equality on `Process.ProcessName`). Use `Process.GetProcessesByName(name)` per name instead of enumerating all processes. This is faster and has no substring false positives. Dispose the `Process` objects. Windrose's exact shipping exe name must be verified on a real install before switching. Until then, keep substring matching for Windrose only, via a definition flag `ProcessMatch = Exact | Contains`.

### 3.9 UI changes

- `ActionsSectionView`: the Play button becomes the dominant primary action (full width, accent). Its label and enabled state come from `PlayButtonState` (VM computed property: `Ready`, `Busy(stage)`, `Playing`, `BlockedBy(name)`, `Sharing`, `PendingUpload`).
- Manual Upload, Download, Start session and End session move into an "Advanced" expander, collapsed by default. They remain available for recovery.
- A stage text under the button (`PlayStageText`), for example "Downloading v14 from Player2…".
- Header session chip: "Free", "You are hosting", "<Player> is hosting · since 20:14", or "Expired · <Player>".
- All new strings go into `ui-text.en.json`.

### 3.10 VM restructuring

- `MainWindowViewModel.CloudCommands.cs` (310 lines) loses `StartGameAsync` and `TryStartCloudSessionAsync`. A new `MainWindowViewModel.Play.cs` holds `PlayCommand`, the outcome-to-text mapping, and confirmation dialogs.
- `MainWindowViewModel.GameMonitor.cs` gets the heartbeat, the start timeout and the finish trigger, and stays under 200 lines.

## 4. Step order

1. `SessionLockPolicy`, plus lock verify and duplicate-file resolution in both providers. Tests with a fake provider simulating the race.
2. Heartbeat on tick, plus the take-over detection warning.
3. `PlaySessionService.PrepareAndLaunchAsync`, plus the VM `PlayCommand` with outcome mapping (G1, G6, G7).
4. Start timeout (G5).
5. `FinishAfterExitAsync` with settle wait, retries, pending-upload banner (G2).
6. Shutdown guard and next-start recovery (G8).
7. Exact process detection (G9), after the exe names are verified.
8. UI restructuring (primary Play, Advanced expander, header chip).

## 5. Acceptance criteria (manual, LocalTest provider, two app instances simulated by editing `MachineName` in lock JSON)

- [ ] Cloud newer → Play backs up, downloads, verifies, restores, locks and launches with no further clicks.
- [ ] A foreign active lock → Play is blocked and the dialog shows the player name and start time.
- [ ] Expired foreign lock → take-over confirmation; a lock is written only after the user confirms.
- [ ] Two lock files created simultaneously → both clients agree on the same winner (unit test).
- [ ] Game closes → the upload creates v+1 and the lock is cleared; a toast names the version.
- [ ] Upload failure (provider throws) → the lock is kept, the banner is shown, Retry succeeds later.
- [ ] Launch where the game never starts → the lock is released after the timeout.
- [ ] Heartbeat updates `LastHeartbeatAtUtc` about every 5 min while playing (log check).
- [ ] No UI freezes during any stage; window remains movable.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Auto-upload of a corrupted save after a crash | Every version is retained (20 versions), and a local backup exists before each download. Document how to roll back (future "Restore previous version" in the Advanced section — out of scope unless requested) |
| Drive eventual consistency makes the verify read stale | 2 s delay plus deterministic duplicate resolution; the residual risk is acceptable for a friend group |
| Steam launches a launcher process first (Dragonwilds) | `ProcessNames` lists every relevant exe; the start timeout is generous |
