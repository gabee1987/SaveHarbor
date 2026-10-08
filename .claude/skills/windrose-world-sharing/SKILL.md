---
name: windrose-world-sharing
description: How Windrose worlds are shared between friends with SaveHarbor (what to upload, what never to share, why a downloaded world may not show up in the game), how to diagnose "the game doesn't see the world", and how to test sharing on one PC without a second player. Use when changing Windrose upload/download/restore, explaining sharing limits to users, or debugging a world that does not appear in the game.
---

# Windrose World Sharing

The file-level details are in `windrose-save-format`. This skill covers behaviour, diagnosis and testing.

## Model

Windrose co-op is host-based: the host's world is the world, and joining players bring their own character
(`Players`) and account (`Accounts`). SaveHarbor rotates the host by moving the world between PCs through the Windrose
game's own Drive folder, never the Dragonwilds one (`multi-game-separation`).

## What travels and what never does

| Item | Shared? | Why |
|---|---|---|
| `RocksDB_v2\<ver>\Worlds\<islandId>\` (except `LOCK`) | yes | The world database. |
| `RocksDB_v2_Backups\Worlds\<islandId>\<islandId>_<ver>_Latest.zip` | **yes, required** | The game loads the world from this and rebuilds the folder from it. Without it the world is not listed. |
| Dated world archives `<islandId>_<ver>_<date>.zip` | no | The game's own history on that PC. Left untouched. |
| `Players\`, `Accounts\` and their archives | **never** | Personal to each PC's player (data minimisation). They may only be counted or backed up locally. |
| `RocksDB\` (legacy) | no | Pre-0.10.0.5.120 format. |

SaveHarbor cannot create `Latest.zip` (its table names embed a RocksDB session id), so it must come from the PC that
last played the world.

## Diagnosis: "I downloaded the world but the game doesn't show it"

Check in this order:

1. **Archive missing.** Is there `RocksDB_v2_Backups\Worlds\<islandId>\<islandId>_<ver>_Latest.zip`?
   - If not, that is the cause.
   - Save health shows "Windrose cannot load this world".
   - Typical source: a backup or upload made with SaveHarbor 2.2.0 or earlier.
   - Fix: the sender uploads again with 2.2.1 or later. By hand, they copy that one `Latest.zip` to the same path under
     the receiver's profile.
2. **Folder name.** Does it differ from `islandId` in `WorldDescription.json`? Import refuses that (F3).
3. **Version folder.** Does it differ between the PCs (`0.10.0`)? Restore and import refuse that (F4).
4. **Profile.** Is it the wrong one? The world must be under the profile the game uses (newest `SaveProfiles\<id>`).
5. **Archive differs.** Is the archive older than the folder? Then the game loads the archive's state; save health
   shows "Windrose's archive of this world differs".

A world folder that the game never opened has no `LOCK`, no files newer than the download, and no new game archive.

## Testing without a second player

A deleted-and-downloaded world stands in for a friend's PC. Steam Cloud restores anything deleted outside the game
(see `windrose-save-format`), so the deletion must happen **in Windrose**. SaveHarbor's Remove alone is not enough:
the world reappears on the next launch.

1. Close Windrose. In SaveHarbor (Windrose), check that All world info shows no archive warning.
2. Upload the world.
3. Start Windrose, delete the world in its world list, then exit. The game deletes the folder and its archive, and
   Steam removes the archive from the cloud on exit. The upload and SaveHarbor's backups are the safety copies.
4. Start Windrose once more to confirm the world stays gone, then exit.
5. Refresh SaveHarbor, then download the world. Both the world folder and `RocksDB_v2_Backups\Worlds\<id>\` are back.
6. Start Windrose. The world must be listed, with its progress.
7. Play a few minutes, exit normally, and refresh SaveHarbor. If "archive differs" appears, the game does not rewrite
   `Latest.zip` on exit, and the upload timing needs rethinking.

Automated tests: `WindroseGameArchiveTests` uses a second profile (`67890`) as the friend's PC. It covers import,
restore, remove and download again, untrusted archives, a missing archive and an out-of-date archive. `SaveFixtures.CreateWindroseGameArchive`
writes a matching archive.

## Known limits

- A world created before the sharing PC ever ran 2.2.1 still shares fine, as long as that PC has the game's
  `Latest.zip`.
- Steam Cloud syncs `RocksDB_v2_Backups`, so:
  - a world removed only by SaveHarbor comes back on the next launch;
  - another PC on the same Steam account may pull a different `Latest.zip`. Save health's "replaced" warning catches
    this when the file numbers go down.

## Sources

- [Winternode: upload a world](https://winternode.com/help/games/windrose/setup/upload-a-world): "RocksDB_v2 is
  rebuilt from the archives on boot".
- [Steam discussion: Dedicated World not portable to RocksDB_v2](https://steamcommunity.com/app/3041230/discussions/0/837249543665987048/)
- Real-install comparison on 2026-10-08, recorded in `windrose-save-format/references/save-structure.md`.
