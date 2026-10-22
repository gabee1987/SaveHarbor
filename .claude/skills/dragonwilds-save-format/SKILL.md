---
name: dragonwilds-save-format
description: RuneScape Dragonwilds local save locations, file layout (world .sav vs character .json), what to sync and what never to touch, world identity, and the open questions to verify on a real install. Use when writing or changing the Dragonwilds save adapter, discovery, backup, or restore.
---

# Dragonwilds Save Format

Research date: 2026-10-05 (game v1.0, released 15 Sep 2026). Sources: Steam community threads,
Dragonwilds wiki, hosting-provider knowledge bases (no primary Jagex technical doc found).
Items marked **[verify]** must be confirmed on a real install before code depends on them
(see `docs/plans/dragonwilds/03-dragonwilds-save-integration.md` Phase 0).

## Locations (Steam, Windows)

| Data | Path | Confidence |
|---|---|---|
| **Worlds** | `%LOCALAPPDATA%\RSDragonwilds\Saved\SaveGames\*.sav` | High |
| Characters | `%LOCALAPPDATA%\RSDragonwilds\Saved\SaveCharacters\*.json` | High |
| Settings | `%LOCALAPPDATA%\RSDragonwilds\Saved\Config\Windows\GameUserSettings.ini` | High |

- One `.sav` file per world; file name ≈ world name (e.g. `My World.sav`; may contain unusual
  characters such as `·`). Medium confidence.
- Per-Steam-ID subfolders under `SaveGames`: none reported. **[verify]**
- Game-made backups (`.bak`, `.json.backup`) mentioned in community threads. Exact names **[verify]**.
- Epic / Jagex Launcher / PC Game Pass (since 15 Sep 2026) save paths: **unknown**. Game Pass
  titles often store saves elsewhere (`Packages\...`, Xbox cloud). **Support Steam only** initially;
  make the save root overridable via `SaveHarbor:Games:Dragonwilds:SaveRoot`.

## What SaveHarbor syncs

- **Only the world `.sav` file** (one world = one file). Payload kind `FileSet` with exactly that file.
- **Never** sync or modify `SaveCharacters\*.json` — character progress (skills, inventory, quests)
  belongs to each player and travels with them into any world.
- **Never** touch `Config\`.
- Ignore game backup files in discovery and payload; do not delete them.

## World identity

- No known internal world id is readable without parsing the binary. Use the **file name stem**
  as `WorldId` (sanitised for cloud paths) and as display name.
- **Never rename** a `.sav` file: the world name is also stored inside the save and renaming has
  reportedly caused lost progress. Restore must write to the exact same file name.
- Same-name collision risk: two friends could each have an unrelated local world called the same.
  Restoring over a local file that has **no sync-state base version** must require explicit
  confirmation ("Replace your unsynced local world 'X'?"), after the automatic backup.

## File format

- Internal format unpublished. Likely Unreal `GVAS` (check for ASCII `GVAS` at offset 0) **[verify]**.
- Use the header check only as a sanity validation ("is this a Dragonwilds world file?") when
  importing/downloading — never parse or edit contents.
- Readable metadata: file name, size, `LastWriteTimeUtc`. Creation time = file creation time
  (unreliable after copies; display as "first seen").
- Character JSON is parseable but out of scope (privacy + not needed).

## Write rules

- Never read/copy while the game (or a local dedicated server) is running — files are written
  during play; copying mid-write corrupts saves (every guide agrees).
- Restore = write to `<name>.sav.saveharbor-tmp` in the same folder → flush → `File.Replace`
  (atomic on NTFS, keeps a `.saveharbor-prev` backup) → delete prev only after success.
- After restore set `LastWriteTimeUtc` to now, so "newest file" heuristics (dedicated server
  loads the newest `.sav`; Steam Cloud compares timestamps) treat it as the current world.
- Several corruption bugs were fixed in 0.11.0.8 / 1.0.0.4 / 1.0.0.5 — versioned backups are essential.

Related: `dragonwilds-world-sharing`, `dragonwilds-steam-cloud`, `dragonwilds-process-and-launch`.
