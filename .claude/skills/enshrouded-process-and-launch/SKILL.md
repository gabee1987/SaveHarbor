---
name: enshrouded-process-and-launch
description: Enshrouded Steam app id, launch URI, executable/process names for running-detection, save cadence and settle timing, and storefront limits. Use when implementing the Enshrouded game definition, launcher or process detection.
---

# Enshrouded: Process and Launch

| Item | Value | Status |
|---|---|---|
| Steam app id | `1203620` | verified (save folder name) |
| Launch URI | `steam://rungameid/1203620` | standard Steam scheme |
| Executable | `enshrouded.exe` → `ProcessNames = ["enshrouded"]` (exact, no `.exe`) | verified 2026-10-28 (file properties, `<SteamLibrary>\steamapps\common\Enshrouded\`; the game can live in any Steam library, so never derive paths from the install folder) |
| Dedicated server | `enshrouded_server.exe` (Steam tool app id `2278520`) | not a "game running" signal by default |
| Save cadence | Autosave every few minutes (Keen docs say 5–10 min) plus on exit; the world file and a rolling copy are written together | verified timestamps |
| `SaveSettleDelay` | 15 s after the process exits | same as Dragonwilds; the exit write is small (a few MB) |
| Storefront | Steam on Windows only in v1 (`SaveRoot` override for anything else). No Epic / Game Pass PC build known | — |
| Game settings file | `%USERPROFILE%\Saved Games\keen games\Enshrouded\local_settings.json` | never touched |

## Play flow notes

- **Play** = host. Joining a friend's world needs no SaveHarbor action: start the game normally and join. `Enshrouded.JoinHint` says so under the Play button.
- The game must be closed before any file operation; running-detection blocks Play/restore while `enshrouded` runs (shared flow).
- Launch via Steam URI; `ExecutablePath` option exists for direct launch, empty by default.
- After exit, the shared finish flow waits `SaveSettleDelay`, hashes the payload, and uploads when changed.

## Not a signal

`enshrouded_server` running on the same PC does not mean the player's client is running; keep it out of `ProcessNames` unless the owner asks for server-aware behaviour (future dedicated-server plan).
