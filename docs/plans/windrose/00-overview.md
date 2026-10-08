# Windrose Overhaul — Findings, Plan and Phases

**Branch:** `feature/windrose-overhaul` (from `develop`). The owner performs all commits, pushes and pull requests.
**Date:** 2026-10-07.
**Goal:** bring Windrose to the same level as the Dragonwilds mode — same features, same robustness — with its own
pirate-themed interface.

## 1. Investigation: why Windrose world sharing was unreliable

Sources: the official [Windrose FAQ](https://playwindrose.com/faq/) and
[Dedicated Server Guide](https://playwindrose.com/dedicated-server-guide), a
[community report](https://steamcommunity.com/app/3041230/discussions/0/807975542034972527/) and a real install
inspected on this PC (names and sizes only).

### 1.1 What a Windrose profile contains (game 0.10.0.x, after update 0.10.0.5.120)

```
%LOCALAPPDATA%\R5\Saved\SaveProfiles\<profileId>\
  RocksDB\                        legacy pre-update saves (now often only steam_autocloud.vdf)
  RocksDB_v2\<version>\           RUNTIME databases the game works on ("do not touch" while it runs)
      Worlds\<islandId>\          one RocksDB per world: WorldDescription.json, *.sst, MANIFEST-*, CURRENT,
                                  OPTIONS-*, IDENTITY, *.log (WAL), LOCK while open
      Players\<playerId>\         the player's characters (inventory, progression); may contain *.blob files
      Accounts\<accountId>\       account-level data
  RocksDB_v2_Backups\             the game's OWN backups, RocksDB checkpoint archives:
      <Kind>\<id>\<id>_<version>_<yyyyMMdd-HHmmss>.zip and <id>_<version>_Latest.zip
      steam_autocloud.vdf         → this folder is synchronised by Steam Cloud
<profileId>_Backups\<date>\       legacy dated backups (version folder + AccountDescription.json)
```

### 1.2 Findings

| # | Finding | Effect on SaveHarbor |
|---|---|---|
| F1 | Characters live in `Players` on **each player's own PC**; dedicated servers store only `Worlds`. A joining player brings their own character. | Only the world folder is shared. `Players`/`Accounts` must **never** be shared or overwritten — they are personal. They can be backed up locally. |
| F2 | The game keeps worlds in up to three places and runs an automatic recovery from `RocksDB_v2_Backups` (Steam-Cloud synchronised) when it considers a runtime database broken or missing. Copying only one place can be reverted by the game. | A downloaded/restored world can be silently replaced on the next launch. SaveHarbor must detect this and say so, and keep its own verified copy to put back. |
| F3 | The world folder name must equal `islandId` in `WorldDescription.json`. | Must be validated on import/download; a mismatch is refused. |
| F4 | The database version folder (`0.10.0`) must match between PCs. | Must be recorded in each backup and checked before restore/import. |
| F5 | SaveHarbor staged restores **inside** `Worlds\` (`<id>.saveharbor-staging-*`, `<id>.saveharbor-prev-*`). An interrupted restore left a second folder with the same `islandId` that the game would see. | Staging moves outside `Worlds\`. Leftovers are detected and cleaned safely. |
| F6 | Process detection matched any process whose name *contains* `R5`. Real executables: `Windrose.exe`, `Windrose-Win64-Shipping.exe` (`WindroseServer.exe` is the dedicated server). | Exact names only. |
| F8 | **The game loads a world from its own archive**, `RocksDB_v2_Backups\Worlds\<id>\<id>_<version>_Latest.zip` (a RocksDB backup-engine checkpoint: `Checkpoint/meta`, `Checkpoint/private`, `Checkpoint/shared_checksum`, plus `AdditionalRecordFiles/WorldDescription.json`), and rebuilds the `RocksDB_v2` folder from it on start. A world folder copied without that archive is not listed. Confirmed on 2026-10-08 with a world downloaded through SaveHarbor 2.2.0 ([hosting guide](https://winternode.com/help/games/windrose/setup/upload-a-world)). | Every backup carries the latest archive in `.saveharbor-game-files\` inside the payload; restore and import put it back. Its table file names hold a RocksDB session id, so SaveHarbor cannot build one itself. Save health reports a world without an archive, or with one that differs from the folder. |
| F9 | **Steam Cloud syncs every `.zip` in `RocksDB_v2_Backups`** (world, player and account archives; nothing in `RocksDB_v2`). On 2026-10-08 a world removed by SaveHarbor (archive folder moved away while the game was closed) came back on the next launch: Steam downloaded the identical `Latest.zip` and the game rebuilt the world from it. Only a deletion the game makes itself reaches Steam Cloud. | Remove warns that Steam Cloud brings the world back and that it must also be deleted in Windrose's world list. A placed archive is dated now so Steam treats it as this PC's newest change. |
| F7 | `WorldDescription.json` carries the world settings (difficulty, multipliers, co-op options). | Shown as world details and rules, like Dragonwilds. |

## 2. Design principles

- One shared, game-neutral main screen. Each game supplies only its theme dictionary, texts and a few small parts.
  The Dragonwilds look must not change.
- Data minimisation: only the world folder is shared. Character and account databases never leave the PC.
- Security: every value read from a save or the cloud is untrusted (bounded, allow-listed, path-safe).
- No new NuGet packages, no downloaded fonts or assets (Palatino Linotype / Georgia ship with Windows).

## 3. Phases

| Phase | Scope | Risk |
|---|---|---|
| 1 | **Save robustness** — exact process names, staging outside `Worlds`, leftover cleanup, `islandId`/folder check, database-version check, world settings parser, full inspector. | Medium (touches restore) |
| 2 | **Shared shell** — move the Dragonwilds screen to game-neutral views and style keys; Dragonwilds looks identical. | Medium (large mechanical refactor) |
| 3 | **Pirate theme** — Windrose theme dictionary (ink, crimson, bone, brass), Windrose shell on the shared views, old Windrose controls removed. | Low/medium |
| 4 | **Windrose extras** — "Save health" checks (game reverted the world, Steam Cloud, game backups present, leftovers), character backups (local only), texts and README. | Medium |

Each phase ends with a build, the full test suite, screenshots of both games, a manual test list and a one-line
commit message suggestion (§4).

## 4. Save health (phase 4)

- **Replaced world (F2).** After every restore, import or download SaveHarbor records the world's RocksDB file names
  (and table sizes) under `sync-state\windrose\world-files\<hash of path>.json`. RocksDB never reuses a file number:
  playing only adds higher numbers and deletes old files. A file at or below the highest recorded number that was not
  recorded therefore means the folder was swapped for another copy. The selected world then shows a warning, All world
  info explains it and offers "Keep as it is" (records the current files), and Upload asks for confirmation first.
  A swapped-in copy whose numbers are all higher cannot be told apart from play, so no warning is not proof.
- **Game archive (F8).** A world without the game's `Latest.zip` is reported ("Windrose cannot load this world"); an
  archive whose manifest or table numbers differ from the world folder is reported as differing. Both ask for
  confirmation before upload.
- **Leftovers (F5).** `<islandId>.saveharbor-*` folders next to a world are reported and can be moved (never deleted)
  to `%LOCALAPPDATA%\SaveHarbor\leftovers\windrose` while the game is closed.

## 5. Manual test lists and commit messages

Recorded per phase in the chat hand-over and summarised here once each phase is complete.
