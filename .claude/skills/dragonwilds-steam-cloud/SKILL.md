---
name: dragonwilds-steam-cloud
description: Steam Cloud behaviour for RuneScape Dragonwilds saves and how SaveHarbor must coexist with it (conflict risks, timestamps, user guidance, verification steps). Use when implementing Dragonwilds restore/upload or diagnosing "my downloaded world was replaced" reports.
---

# Dragonwilds and Steam Cloud

## Facts

- Launched (Apr 2025) **without** Steam Cloud; Steam Cloud saves went live ~2 May 2025 (update 0.7.3 era). High confidence.
- **Unknown**: whether Steam Cloud syncs worlds (`SaveGames`), characters (`SaveCharacters`), or both,
  and its conflict behaviour. **[verify]** via `C:\Program Files (x86)\Steam\userdata\<accountId>\1374490\remote`
  and `remotecache.vdf`, or SteamDB's `/app/1374490/ufs/` page.

## Why it matters

Steam Cloud is per Steam account. When SaveHarbor replaces a world `.sav` before launch:
- Steam sees a local change since its last sync. If the account's cloud copy is unchanged
  (normal case: player only uses one PC) → Steam uploads our file. ✅
- If the account's Steam Cloud also changed (player played on another PC) → Steam shows its
  **Cloud Conflict dialog** at launch. Choosing "Cloud" would replace the SaveHarbor-downloaded
  world with a stale one. ⚠️
- Steam syncs on launch and exit; it never syncs while the game is closed, so SaveHarbor writing
  files while the game is closed is safe by itself.

## Rules for SaveHarbor

1. Set restored file `LastWriteTimeUtc` = now (never back-date to the uploader's time).
2. After a Dragonwilds download, show a one-line hint in the activity log / stage text:
   "If Steam asks about a cloud conflict, choose **Local files**."
3. Before upload (after game exit), compare the `.sav` hash with the version we launched from.
   If unchanged, skip creating a new version ("No changes to share") — also protects against
   Steam having silently reverted the file (then the hash would equal some older version: log a warning).
4. Never edit Steam's `userdata` or `remotecache.vdf`. Never disable Steam Cloud programmatically.
5. If verification shows Steam Cloud does **not** sync `SaveGames`, simplify: drop the hint.

## Open verification (Phase 0 of plan 03)

- Which folders appear under `userdata\<id>\1374490\remote`.
- Download world via SaveHarbor on PC A → launch → does Steam prompt? Does the world load as downloaded?
- Same with a deliberately diverged Steam Cloud (play on PC B first).
