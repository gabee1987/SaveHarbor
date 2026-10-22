# 05 — Speed, Realtime and Convenience

The items that make the app feel instant and let friends forget it is running. Ordered by value per effort; §1–§4 are part of the MVP release, §5–§8 follow.

## 1. Instant startup: snapshot cache

Problem: today the window waits for cloud calls before it shows meaningful sharing state.

- `Infrastructure/Cloud/CloudSnapshotCache.cs`: persists the last `CloudGroupSnapshot` per provider and game to `%LOCALAPPDATA%\SaveHarbor\cloud-cache\<provider>\<game>.json` with `CachedAtUtc`.
- `MainWindowViewModel.InitializeAsync`: scan local worlds → render → load the cached snapshot and show it immediately with an "as of 21:14" badge and muted styling → refresh in the background → replace. Cached data is **never** used to decide uploads or locks; only `RefreshStatusAsync` with a live snapshot does.
- Status refresh for the world list uses the snapshot once (plan 02 §3); the per-world "Check cloud" button still forces a live call.
- Budget: first paint under 300 ms on a cold start with 10 worlds; live refresh over Supabase under 600 ms (one RPC). Measure with `Stopwatch` logs at `Debug` under `AppLogKeyword.Ui`.

## 2. Fewer, smarter requests

- Coalesce refreshes: `RefreshCloudStatusAsync` and `RefreshCloudOverviewAsync` are both called after most commands; introduce `RefreshCloudAsync(reason)` that fetches one snapshot and updates both. Debounce 250 ms.
- Skip no-op uploads: before zipping, compare the world's content hash (`LocalChangeDetector` already captures `LocalBaseContentHash`) with the base; if equal and the base equals the cloud latest, show "Nothing changed since v12; nothing to upload." and return. Applies to all providers.
- Parallel zip + hash: compute SHA-256 while writing the zip stream instead of a second pass (`ZipBackupService` + `FileHashCalculator`), saving one read of the archive.
- HTTP: keep-alive connection reuse (`SocketsHttpHandler`), gzip for JSON responses, `Prefer: return=minimal` on writes that do not need the row back.

## 3. Heartbeat and lock expiry (all providers)

- `MainWindowViewModel.GameMonitor` ticks every 5 s. While `HasOwnCloudSession()` and the game runs, every 5 minutes call `ICloudSyncService.HeartbeatSessionAsync(world)`; the service calls `HeartbeatSessionLockAsync` and extends the lock by `LockTtlSeconds` (Supabase: RPC; Drive/LocalTest: rewrite the lock file with new `LastHeartbeatAtUtc` and `ExpiresAtUtc`).
- Lock TTL becomes 20 minutes for all providers (`CloudSessionLock.ExpiresAtUtc = now + 20 min` at acquire). Drive's old 6 h locks written by older app versions are honoured as-is until they expire.
- Lost heartbeat (`lock_lost` / lock file replaced by someone else): set `CloudStatus` to `Conflict` with the text from plan 04 §7, keep the session marked locally so the upload-on-close path (§4) runs the normal conflict check instead of overwriting.
- Expired foreign lock UX: "<player>'s session expired at <time>. You can take over." and the Play button says "Take over and play".

## 4. Upload on game close (completes weakness 4 from `cloud-sync-and-locking`)

Today `AutoEndCloudSessionAfterGameClosedAsync` only clears the lock. Target flow after the game process disappears and the session is ours:

1. Wait for save settle: poll the world's modified time until unchanged for 3 s (max 30 s), as planned in `dragonwilds/05`.
2. `UploadCurrentAsync` (with the no-op skip from §2). On success → `EndSessionAsync` → toast "Shared v13 of <world>".
3. On failure (offline, conflict, too large) → **keep the lock**? No: release the lock (others could otherwise never play) but set a persistent banner "Your progress from <time> is not shared yet" with **Retry upload** and **Discard** (discard = mark local state as needing download confirmation). The banner survives restarts (`sync-state` flag `PendingUploadSince`).
4. If the app is closed while the game runs: on next start, if the lock is ours and the game is not running, offer the same banner.

## 5. Minimise to tray while playing

- `AppSettings.MinimizeToTrayWhilePlaying` (default true). When Play launches the game, the window minimises to the tray (WPF `NotifyIcon` via `System.Windows.Forms` is not available without a reference; use a small P/Invoke `Shell_NotifyIcon` wrapper in `Views/Behaviors/TrayIconBehavior.cs`, ≤ 200 lines, or the `H.NotifyIcon.Wpf` package — **decision: P/Invoke wrapper**, no new package).
- Tray icon tooltip: "SaveHarbor · playing <world> · lock until 21:40"; double-click restores; context menu: Open, Upload now, Exit.
- Toasts while in tray become Windows toast notifications? No: keep in-app toasts and show a tray balloon (`NIF_INFO`) only for "Shared v13" and failures. Balloons need no extra permissions.
- Single instance: `Mutex` named `Local\SaveHarbor.App` in `Program.cs` (plan 06 §2 introduces `Program.cs`); a second start brings the first window to front via a named pipe message or `EventWaitHandle`.

## 6. Realtime (Supabase only)

Replaces the 5 s poll of cloud state (the game-process poll stays).

- `Infrastructure/Cloud/Supabase/SupabaseRealtimeClient.cs` (≤ 300 lines): `ClientWebSocket` to `wss://<ref>.supabase.co/realtime/v1/websocket?apikey=<key>&vsn=1.0.0`. Phoenix protocol messages `[join_ref, ref, topic, event, payload]` as JSON objects (`{"topic","event","payload","ref"}` in v1.0.0 **[verify]** exact framing against the Realtime docs when implementing). Join topic `realtime:saveharbor:<groupId>` with payload `{ "config": { "postgres_changes": [ {event:"*", schema:"public", table:"session_locks", filter:"group_id=eq.<groupId>"}, {event:"*", schema:"public", table:"worlds", filter:"group_id=eq.<groupId>"}, {event:"INSERT", schema:"public", table:"world_versions", filter:"group_id=eq.<groupId>"} ], "presence": { "key": "<userId>" } }, "access_token": "<jwt>" }`. Send `phoenix` heartbeat every 30 s; send `access_token` refresh messages when the JWT rotates.
- Presence: track `{ display_name, device_label, game, playing_world_id? }` so the Sharing view shows "online in SaveHarbor" dots and the companion page (plan 08) shows the same.
- On any change event: debounce 500 ms → `RefreshCloudAsync("realtime")` (one snapshot RPC; the event payload is not trusted as state). Toasts: "<player> shared v13 of <world>", "<player> started playing <world>", "<player> finished".
- Reconnect with exponential backoff (1 s → 60 s); while disconnected, fall back to a 60 s snapshot poll. Connection state shown as a small dot in the Sharing hero ("live" / "polling").
- Free-plan limits (200 concurrent connections, 2 M messages/month) are far above a friend group's use; one connection per running app.

## 7. Convenience details

- **Keyboard**: Enter on the world list = Play; Ctrl+U upload; Ctrl+R refresh; F1 opens the README section for the active provider.
- **Invite link**: `saveharbor://join/<CODE>` registered per user (`HKCU\Software\Classes\saveharbor`) by a Velopack install hook (`VelopackApp.Build().OnAfterInstallFastCallback`) and handled in `Program.cs` → opens the group dialog at step 3 with the code filled in. Copy button in Settings offers both the code and the link.
- **Pre-flight on Play**: the Play button's tooltip states what will happen ("Cloud has v13 by Anna, you have v12: download, lock, launch") before the click, from the cached snapshot.
- **Activity log**: realtime events appear in the Chronicle view with the player name, so the history reads like a timeline.
- **Startup argument** `--game` stays; add `--provider supabase|googledrive` for shortcuts and support.

## 8. Not doing (recorded so nobody re-opens it)

- Delta uploads (rsync-style): archives are 3–18 MB; not worth the complexity on either provider.
- Background service / auto-start with Windows: the app must only run when the player wants it to.
- Push notifications to phones: the companion page (plan 08) with realtime covers "who is playing" without a notification service.
