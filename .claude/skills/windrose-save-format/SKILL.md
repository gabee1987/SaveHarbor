---
name: windrose-save-format
description: Windrose save location, RocksDB world folder structure, world identity, process names and Steam launch details. Use when changing Windrose discovery, backup/restore, process detection, or launch logic.
---

# Windrose Save Format

## Location

```
%LOCALAPPDATA%\R5\Saved\SaveProfiles\<profileId>\RocksDB[_v2]\<rocksDbVersion>\Worlds\<worldId>\
```

- `<profileId>`: numeric per-account folder (use placeholder `12345` in examples/tests).
- `RocksDB_v2` is preferred over `RocksDB` when both exist (`RocksDbRootNames` order).
- `<rocksDbVersion>`: e.g. `0.10.0`; the latest-written version directory is chosen.

## World folder

A **RocksDB database** – copy the whole directory as one unit:
`WorldDescription.json`, `*.sst`, `MANIFEST-*`, `CURRENT`, `OPTIONS-*`, `IDENTITY`, `LOG*`, `*.log` (WAL).
`LOCK` exists while the DB is open.

`WorldDescription.json` → `{ "Version": n, "WorldDescription": { "islandId", "WorldName",
"CreationTime" (Unreal ticks), "WorldPresetType" } }`. `islandId` is the world id (fallback: folder name).

## Rules

- Never copy while the game is running (RocksDB is mid-write).
- Treat all files as required; no cherry-picking.
- Restore = replace whole directory (after automatic backup).
- Importing to a fresh PC requires a profile: the user must start Windrose once first.

## Process / launch

- Steam app id **3041230**, URI `steam://rungameid/3041230`.
- Process detection today: substring match on `Windrose` or `R5`. `R5` is too broad
  (can false-positive on unrelated processes); the multi-game plan replaces it with an exact
  process-name list per game definition. Verify the exact shipping exe name on a real install
  before tightening.

## Cloud layout

Legacy, unscoped: `<shared root>/worlds/<islandId>/...`. Keep it for Windrose to avoid migration
(see `multi-game-separation`).
