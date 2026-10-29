---
name: enshrouded-save-format
description: Enshrouded (1.0) local save locations, the world<N>.ksd file layout and SV2C header, what SaveHarbor syncs and what it never touches, world identity (header GUID) versus slot number, and the game's rolling backups. Use when writing or changing the Enshrouded save adapter, discovery, backup, restore or import.
---

# Enshrouded Save Format (1.0, Steam, Windows)

Verified on a real install on 2026-10-28 (plan `docs/plans/enshrouded/01-save-format-and-verification.md`).
Enshrouded 1.0 (15 October 2026) replaced the Early Access layout; SaveHarbor supports **1.0 only**.

## Locations (two folders; SaveHarbor manages one)

| Folder | Path | Notes |
|---|---|---|
| Local | `%USERPROFILE%\Saved Games\keen games\Enshrouded\default\` | The game's "Local" tab. Not tracked by Steam. **Not managed by SaveHarbor** (not listed, never written). |
| Steam Cloud | `<Steam>\userdata\<steamid3>\1203620\remote\` | The game's "Steam" tab. Every top-level file is tracked in `..\1203620\remotecache.vdf`. **SaveHarbor's managed folder** for the signed-in account (see `enshrouded-steam-cloud`). |

`<Steam>` = `HKCU\Software\Valve\Steam\SteamPath` (fallback `HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`). The managed account is `HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser` when its `userdata` folder has `1203620\remote`, else the account whose `remote` folder changed last.
`SaveRoot` option override → that folder instead. Ignore `%USERPROFILE%\Saved Games\Enshrouded\` (Early Access leftovers) and any `migrated\` folder.

## Files in a root

| File | SaveHarbor |
|---|---|
| `world<N>.ksd` | **The world. One file = one world. The payload.** `N` is a local slot number (0, 1, …), not identity |
| `character<N>.ksd` | Never read, written or uploaded (per-player progress) |
| `global.ksd` | Never touched (per-user data, Adventure Sharing list; contains no world GUIDs) |
| `user_settings.json` | Never touched |
| `backup\world<N>.ksd.backup-<K>` | The game's rolling copies; `backup-9` is the oldest, lower K newer; the main file equals the newest copy. Read-only (`GetGameBackupCopyPath` → lowest K). Never uploaded. Untracked by Steam |
| `backup\character*.backup-*`, `backup\global.*` | Never touched |
| `migrated\*` | Early Access files. Ignored |

The game writes the main file and a new rolling copy together on autosave and on exit.

## `.ksd` header (48 bytes, little-endian; same shape for world, character and global files)

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | Magic `SV2C` |
| 4 | 2 | Format version = 2 |
| 6 | 2 | Header size = 48 (`0x30`) |
| 8 | 4 | File length (observed; not checked) |
| 12 | 16 | **World GUID** — stable across saves and rolling copies; unique per world |
| 28 | 4 | **Save counter** — increments on every save |
| 32 | 8 | **Unix timestamp (s)** of the save |
| 40 | 8 | Unknown |
| 48 | … | `S2BH` blocks. The first one is uncompressed: GUID copy at +14, world name as `u32` length at +71 and UTF-8 bytes at +75 (observed on 1.0, `EnshroudedWorldNameReader`), then an 18-byte tail. Worlds created or edited in 1.0 follow it with an uncompressed **world settings** block (`S2BH`, version 2, type 12, layout 3, 2 zero bytes, 127 field bytes in the order of the dedicated server's `gameSettings`: floats, 1-byte switches/choices, `u64` nanosecond durations; one trailing byte; then the next `S2BH`), read read-only by `EnshroudedWorldSettingsReader`. Early Access worlds carried over have no such block. The rest is compressed; SaveHarbor never parses or edits it |

World file check = name matches `^world\d+\.ksd$` (ignore case) **and** header passes (magic, version 2, size 48, length ≥ 48). Character files have the same header, so the name check is what excludes them.

## Identity and slots

- `WorldId` = the 16 GUID bytes as 32 lowercase hex chars **in file order** (do not use `Guid.ToString()`, whose byte order differs). Cloud layout `worlds/<worldId>/…`; the slot never reaches the cloud.
- Verified: a world renamed to another slot loads; an older copy placed over a newer one loads; the same GUID in two slots shows **two** worlds (the game does not deduplicate) → SaveHarbor maps same GUID → same slot.
- Slot resolution for a root: file whose header GUID matches → reuse; else the smallest free `N` from 0.
- Archives carry the file as `world.ksd` (uploader's slot number stripped); restore writes it to the resolved slot.
- Display name: read from the first `S2BH` block (see the header table); every check must pass, else `"Enshrouded world <N>"`. Subtitle shows `Slot <N> · Local|Steam Cloud`.
- **The game re-keys a duplicate.** A copy of a world that the game saved while another copy with the same GUID existed got a new GUID (observed 2026-10-28; owner confirmation pending). Never let the same GUID sit in two places the game can see if it must keep its identity.
- `GetSaveFormatVersion` → `"2"` from the header; a different version is never restored into this game.

## Never

- Never edit `remotecache.vdf` or any non-world file in `remote`; write `world<N>.ksd` only while the game is closed.
- Never delete the game's rolling copies; move them aside under `<root>\saveharbor-aside\<timestamp>\` when a world is removed.
- Never decompress or modify the content of a `.ksd`. Byte-for-byte copies only.
- Never put a real Steam id, world GUID or world name from a developer's PC into code, tests or docs.

## Early Access layout (unsupported, for recognition only)

Ten fixed hex slot ids (`3ad85aea` = slot 1, `3bd85c7d` = slot 2 …) each with `-1`…`-9` rolling copies, a `-index` JSON (`latest`, `time`, `deleted`), a `_info` file (`KSC1` + zstd; held `saveGameId`, `name`, `playTime`), a single `characters` file and `adventure_sharing`. The first 1.0 launch converts them to `world<N>.ksd` and moves the originals to `migrated\`.
