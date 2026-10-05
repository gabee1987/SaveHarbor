---
name: play-session-flow
description: The target one-click "Play" UX for SaveHarbor (check cloud, pull newer save, lock, launch, monitor, upload on close, release) with every branch and edge case. Use when changing StartGame, game monitoring, session start/end, or any launch-related UX.
---

# Play Session Flow (target behaviour)

The product promise: **open SaveHarbor, press Play, and you are always on the newest shared world;
when you quit, your friends get your progress.** Everything else is secondary.

## Current behaviour (as of develop @ 0dbf842)

`StartGameAsync`: start session (blocked if cloud newer → user must Download manually) → launch via Steam.
Game monitor (5 s): when the game closes and we own the lock → `EndSessionAsync` (lock cleared, **no upload**).

## Target flow (single primary button)

```
Press Play
 ├─ Game already running? ─ yes → if we own lock: show "Session active"; else offer to start session (existing behaviour)
 ├─ Cloud connected?       ─ no  → inline prompt "Connect Google Drive" (one click), then continue
 ├─ Refresh status
 │   ├─ SomeonePlaying     → BLOCK. Dialog: "<Player> is hosting since <time> (v<N>)". Buttons: OK / Refresh.
 │   │                        If lock expired: "Session from <Player> expired <ago>. Take over?" (explicit confirm)
 │   ├─ Conflict           → BLOCK. Explain; offer "Open backups" – never auto-resolve.
 │   ├─ CloudNewer         → auto: safety backup → download → verify hash → restore (progress text per step)
 │   ├─ NoCloudSave        → confirm "Publish this world as v1 and play?" → upload → continue
 │   └─ UpToDate           → continue
 ├─ Acquire lock (write → wait → verify LockId)  ── lost race → BLOCK with winner's name
 ├─ Launch game (Steam URI / exe)                 ── failure → release lock, show fix hint
 └─ Monitor (existing 5 s timer)
     ├─ wait up to ~3 min for process to appear; if it never appears → release lock, notify
     ├─ while running: heartbeat lock every ~5 min
     └─ on exit: wait for save files to settle (no writes for ~10 s) → upload (backup+zip+hash)
                  → lock cleared by upload → toast "Progress shared as v<N+1>"
                  upload failure → keep lock, persistent warning banner + "Retry upload" action
```

## UX rules

- One primary button, label reflects state: `Play`, `Updating world…`, `Launching…`,
  `Playing – lock held`, `Sharing progress…`, `<Player> is playing`.
- Every blocking state names the person and the time; never generic "operation failed".
- Auto actions (download, upload) are announced via status + activity log, not modal dialogs.
  Modal dialogs only for: taking over a lock, publishing first version, conflicts.
- If the app is closed while the game runs and we hold the lock, ask on close:
  "Keep session locked / Upload later" – on next start, detect "own lock + game not running +
  local newer" and offer "Share your last session now".
- Background work must never block the window; cancelation only where safe (not mid-restore).

## Edge cases checklist

- Steam launches the game but the process name differs (launcher → shipping exe) — detect by
  the game definition's process names list, not substring of the title.
- Game crash → treated as exit; upload still happens (files may be inconsistent: upload is
  still a new version, previous versions retained, so it is recoverable).
- PC sleeps while playing → heartbeat stops; lock expiry must be long enough to tolerate short sleeps
  but the owner reclaims automatically because `MachineName` matches.
- Two worlds for one game: Play acts on the selected world only; lock is per world.
- Network drops on exit → retry with backoff (3 attempts), then the persistent "Retry upload" banner.
- Game-specific settle time and file locks: see each game's save-format skill.

Implementation details: `docs/plans/dragonwilds/05-play-flow-ux.md`.
