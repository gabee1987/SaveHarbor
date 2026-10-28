# Plan 01 — Enshrouded Save Format and Phase 0 Results

**Branch:** `feature/enshrouded-support`
**Status:** done. Everything below was verified on the owner's PC on 2026-10-28 (Enshrouded 1.0, Steam, Windows 11) unless marked **[open]**.
**Skills:** `enshrouded-save-format`, `enshrouded-steam-cloud`.

## 1. Locations

| Root | Path | Role |
|---|---|---|
| Steam Cloud | `<Steam>\userdata\<steamid3>\1203620\remote\` | The game's "Steam Cloud" save tab. Steam tracks every top-level file here in `..\1203620\remotecache.vdf` (sha, size, times) and uploads changes |
| Local | `%USERPROFILE%\Saved Games\keen games\Enshrouded\default\` | The game's "Local" save tab (new in 1.0). Not tracked by Steam. Created by the game on first use (it did not exist after a Steam Cloud session; it exists after test 4 below) |
| Settings | `%USERPROFILE%\Saved Games\keen games\Enshrouded\local_settings.json` | Graphics settings. Never touched |
| Legacy | `%USERPROFILE%\Saved Games\Enshrouded\` | Early Access location; holds only `enshrouded_user.json` and a `migrated\` folder now. Never touched |

`<Steam>` comes from `HKCU\Software\Valve\Steam\SteamPath` (fallback `HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`). `<steamid3>` is every numeric folder under `userdata\` that contains `1203620\remote\`; SaveHarbor lists each as a separate root if there is more than one.

## 2. Files inside a save root (1.0 layout)

| File | Meaning | SaveHarbor |
|---|---|---|
| `world<N>.ksd` | One world per file. `N` is a **slot number** (0, 1, …). The game lists every such file | **The payload.** One file |
| `character<N>.ksd` | One character per file | Never read, never written, never uploaded |
| `global.ksd` | Per-user data (the Adventure Sharing list). Contains no world GUIDs, so a world file is self-contained | Never touched |
| `user_settings.json` | Audio and input settings | Never touched |
| `backup\<file>.ksd.backup-<K>` | The game's rolling copies, written every few minutes and on exit. `backup-9` is the **oldest**, lower numbers are newer. The main file equals the newest copy | Read-only: `GetGameBackupCopyPath` returns the newest `world<N>.ksd.backup-*`. Never uploaded |
| `migrated\` | Untouched Early Access files (`3ad85aea`, `3bd85c7d`, `-index`, `_info`, `characters`, `adventure_sharing`) | Ignored. Not a source for sharing |

The game writes the main file and a new rolling copy at the same moment (same timestamp on both), so a world whose main file is newer than every backup is not a sign of trouble.

## 3. `.ksd` header (verified on worlds, characters and `global.ksd`)

Little-endian. Offsets in bytes.

| Offset | Size | Field | Observed |
|---|---|---|---|
| 0 | 4 | Magic | `SV2C` |
| 4 | 2 | Format version | `2` |
| 6 | 2 | Header size | `0x30` (48) |
| 8 | 4 | Payload size | file length minus 48 |
| 12 | 16 | **GUID** | Identical in every save and every rolling copy of the same world; different for a different world; present in character and global files too |
| 28 | 4 | **Save counter** | 1 on the first save after migration, incremented on every save (5 after one short session) |
| 32 | 8 | **Unix timestamp (seconds)** | Equals the file's last write time |
| 40 | 8 | Unknown | — |
| 48 | … | `S2BH` block header, then zstd frames (`28 B5 2F FD`) | Content not parsed |

Sanity check for "is this an Enshrouded world file": magic `SV2C`, version 2, header size 48, file length ≥ 48, and the file name matches `^world\d+\.ksd$` (case-insensitive). This rejects character files by name, since their header is the same shape.

**Not readable yet [open]:** the world name. It was not in the first zstd frame. The block table after `S2BH` was not parsed. Plan 06 has a time-boxed spike. If it fails, the world is named at first share (the cloud manifest already carries a name) and shown locally as "Enshrouded world <N>".

## 4. Phase 0 results (owner, 2026-10-28, game closed between steps)

| # | Test | Result |
|---|---|---|
| P1 | Rename `world1.ksd` → `world5.ksd` | **Pass.** The world appears and loads from the new slot. Slot number is not identity |
| P2 | Replace `world5.ksd` with an older rolling copy (`backup\world1.ksd.backup-9`) | **Pass.** The older state loads; no Steam conflict dialog on the next start. The game accepts a file with a lower save counter and a different checksum |
| P3 | Copy `world5.ksd` → `world6.ksd` (same GUID in two slots) | **Pass.** Both load. The game does **not** reject duplicates, so SaveHarbor must map "same GUID → same slot" itself |
| P4 | Move the world file into `Saved Games\keen games\Enshrouded\default\` | **Pass.** The world appears in the **Local** tab. SaveHarbor can stay out of the Steam folder entirely |
| P5 | Friend hosts the world on another Steam account, original host joins | **[open]** Not testable alone. Characters are per player and the world holds no host identity that we could find, so the risk is limited to in-world roles/permissions. Check on the first real two-player session |
| P6 | How Steam reacts to a file SaveHarbor writes into `remote` | Not needed if P4's policy is adopted (SaveHarbor never writes there) |

## 5. Early Access layout (for completeness, not supported)

Before 1.0, worlds were ten fixed hex slots (`3ad85aea` = slot 1, `3bd85c7d` = slot 2, …) with `-1`…`-9` rolling copies, a `-index` JSON (`latest`, `time`, `deleted`) and a `_info` file (`KSC1` + zstd; contained `saveGameId`, `name`, `playTime`, `progressLevel`). The 1.0 launch migrates these into `world<N>.ksd` and moves the originals into `migrated\`. SaveHarbor targets 1.0 only; a `migrated\` folder is ignored.

## 6. Size

An Early Access world of a few dozen hours became a 2.4 MB `world1.ksd` (one rolling copy was 3.2 MB). The SaveHarbor Cloud `saves` bucket limit is 45 MiB per archive; no change needed.
