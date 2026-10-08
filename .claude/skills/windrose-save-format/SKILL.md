---
name: windrose-save-format
description: Windrose save profile layout (RocksDB_v2 runtime databases, RocksDB_v2_Backups game archives, Players/Accounts), the Latest.zip checkpoint the game actually loads, world identity, process names and Steam launch details. Use when changing Windrose discovery, backup/restore, import, save health, process detection, or launch logic.
---

# Windrose Save Format

Verified on a real install (game 0.10.0.x, after update 0.10.0.5.120) on 2026-10-08. The full annotated tree is in
[references/save-structure.md](references/save-structure.md). Use placeholder ids (`12345`, `TEST_WORLD_ID`) in code,
tests and docs, never real ones.

## Profile layout

```
%LOCALAPPDATA%\R5\Saved\SaveProfiles\<profileId>\
  RocksDB\                         legacy pre-update format; usually only steam_autocloud.vdf now
  RocksDB_v2\<version>\            RUNTIME databases (a cache the game rebuilds on start)
      Worlds\<islandId>\           one RocksDB database per world
      Players\<playerId>\          this PC's characters        - personal, never shared
      Accounts\<accountId>\        account-level data          - personal, never shared
  RocksDB_v2_Backups\              the game's OWN archives - what the game loads; Steam Cloud syncs it
      Worlds\<islandId>\<islandId>_<version>_Latest.zip   (+ dated <islandId>_<version>_<yyyyMMdd-HHmmss>.zip)
      Players\<playerId>\...       same naming
      Accounts\<accountId>\...     same naming
      steam_autocloud.vdf
```

- `<version>`: database version folder, e.g. `0.10.0`. It must match between PCs (`SaveFormatCompatibility`).
- `RocksDB_v2` is preferred over `RocksDB` when both exist (`RocksDbRootNames` order).

## The rule that matters: the game loads the archive, not the folder

On start the game rebuilds `RocksDB_v2\<version>\Worlds\<islandId>` from
`RocksDB_v2_Backups\Worlds\<islandId>\<islandId>_<version>_Latest.zip`.

- A world folder **without** that archive is **not listed** in the game. This is why SaveHarbor before 2.2.1 could not
  share Windrose worlds.
- A world folder whose archive holds an older state is silently reverted to the archive's state.

So a shareable Windrose world is **the world folder plus its `Latest.zip`**. SaveHarbor stages the archive in
`.saveharbor-game-files\` inside each backup payload. `WindroseGameArchive` handles staging, validation and placing it
back; `DirectoryPayloadStrategy` never copies that folder into the world folder.

## Latest.zip contents (RocksDB BackupEngine checkpoint)

```
Checkpoint/meta/1                                       text: timestamp, sequence, file count, "<path> crc32 <n>" lines
Checkpoint/private/1/CURRENT, MANIFEST-<n>, OPTIONS-<n>, <n>.log
Checkpoint/shared_checksum/<n>_s<dbSessionId>_<size>.sst
Checkpoint/shared_checksum/<n>_<crc32c>_<size>.blob
AdditionalRecordFiles/WorldDescription.json             identical to the folder's copy
```

- SaveHarbor **cannot build this archive**. Table names embed the RocksDB session id stored inside each `.sst`. Always
  carry the game's own file.
- Archive and folder match when the archive's `private/*/MANIFEST-<n>` exists in the folder and the archive's
  `shared_checksum` numbers equal the folder's `<n>.sst` / `<n>.blob` numbers (`WindroseGameArchive.MatchesWorldFolder`).
  Restore keeps the original file numbers.
- Treat a received archive as untrusted. It must be one file named `<worldId>_<version>_Latest.zip` with entries only
  under `Checkpoint/` or `AdditionalRecordFiles/`, no `..` segments, a bounded size and entry count, and an `islandId`
  equal to the world id.

## World folder (runtime database)

`WorldDescription.json`, `*.sst`, `*.blob`, `MANIFEST-*`, `CURRENT`, `OPTIONS-*`, `IDENTITY`, `<n>.log` (WAL). `LOCK`
exists only while the game has the world open, and SaveHarbor never copies it. RocksDB never reuses a file number:
play adds higher numbers and compaction deletes old ones (the basis of the save-health ledger).

`WorldDescription.json` contains `{ "Version": 1, "WorldDescription": { "islandId", "WorldName", "CreationTime"
(Unreal ticks), "WorldPresetType", "WorldSettings": { BoolParameters, FloatParameters, TagParameters } } }`. Keys are
tag names such as `WDS.Parameter.CombatDifficulty`. `islandId` is the world id and must equal the folder name.

## Rules

- Never read or write while the game is running (RocksDB is mid-write; `LOCK` present).
- The world folder and its `Latest.zip` are one unit. Restore, import and download write both, and the safety backup
  taken before them contains both.
- Never touch `Players` or `Accounts`, or their archives; they belong to this PC's player.
- Leave the game's dated archives alone; only `_Latest.zip` is replaced.
- Importing on a fresh PC needs a profile: the user must start Windrose once first.

## Process / launch

- Steam app id **3041230**, URI `steam://rungameid/3041230`.
- Process names (exact match): `Windrose`, `Windrose-Win64-Shipping`. `WindroseServer` is the dedicated server, which
  uses its own save folder.

## Cloud layout

`<game folder>/worlds/<islandId>/...`, in the Windrose game's own Drive folder (see `multi-game-separation`). The
uploaded archive is a normal SaveHarbor backup zip, whose payload now contains `.saveharbor-game-files\`.
