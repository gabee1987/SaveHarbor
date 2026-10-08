# Windrose save structure (reference sample)

Recorded on 2026-10-08 from a real profile holding one world created locally and one world downloaded with
SaveHarbor 2.2.0, which the game did not list. Ids are replaced with placeholders, sizes are typical, and the file
contents are not reproduced.

- `12345`: profile
- `WORLD_A` and `WORLD_B`: 32-character hex island ids
- `PLAYER_1` and `ACCOUNT_1`: player and account ids

```
SaveProfiles\12345\
  RocksDB\
    steam_autocloud.vdf
  RocksDB_v2\0.10.0\
    Worlds\
      WORLD_A\                            created on this PC - listed by the game
        WorldDescription.json   ~1 KB
        CURRENT                 16 B      "MANIFEST-000061"
        IDENTITY                36 B
        LOCK                    0 B       only while the game runs
        MANIFEST-000061         ~4 KB
        OPTIONS-000059, OPTIONS-000063    ~107 KB each
        000060.log              0 B       write-ahead log
        000014.sst ... 000075.sst         tables
        000015.blob                       blob file
      WORLD_B\                            downloaded via SaveHarbor 2.2.0 - NOT listed (no archive below)
        same kinds of files, ~90 tables, MANIFEST-051206, ~20 MB in total
    Players\PLAYER_1\                     characters - personal
    Accounts\ACCOUNT_1\                   account data - personal
  RocksDB_v2_Backups\
    steam_autocloud.vdf                   Steam Cloud syncs this folder
    Worlds\
      WORLD_A\
        WORLD_A_0.10.0_Latest.zip         <- what the game loads WORLD_A from
      (no WORLD_B folder)                 <- why WORLD_B was invisible
    Players\PLAYER_1\
      PLAYER_1_0.10.0_20261004-162406.zip ... (about every 10 minutes while playing)
      PLAYER_1_0.10.0_Latest.zip
    Accounts\ACCOUNT_1\
      ACCOUNT_1_0.10.0_20260712-143947.zip
      ACCOUNT_1_0.10.0_Latest.zip
```

## WORLD_A_0.10.0_Latest.zip

```
Checkpoint/meta/1
Checkpoint/private/1/000060.log
Checkpoint/private/1/CURRENT
Checkpoint/private/1/MANIFEST-000061              same manifest as the folder
Checkpoint/private/1/OPTIONS-000063
Checkpoint/shared_checksum/000014_s<session>_1210.sst
Checkpoint/shared_checksum/000015_<crc32c>_175.blob
Checkpoint/shared_checksum/000016_s<session>_1944.sst ... 000075_s<session>_2883.sst
AdditionalRecordFiles/WorldDescription.json       byte-identical to the folder's copy
```

`Checkpoint/meta/1` (text):

```
<unix timestamp>
<sequence number>
<file count>
shared_checksum/000069_s<session>_3228.sst crc32 <n>
...
private/1/MANIFEST-000061 crc32 <n>
private/1/CURRENT crc32 <n>
```

The table numbers in `shared_checksum` (14-17 and 64-75) and the manifest (`000061`) match the world folder one for
one. This is what `WindroseGameArchive.MatchesWorldFolder` checks.

## Timing observed

- The world archive had the same timestamp as the newest table in the folder while the game was running, so the game
  updates it while playing.
- Whether it is also rewritten on exit is not confirmed yet. If save health reports "archive differs" after a normal
  exit, it is not.
