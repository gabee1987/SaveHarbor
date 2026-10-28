---
name: enshrouded-steam-cloud
description: How Enshrouded's two save tabs (Local and Steam Cloud) map to folders, how Steam tracks the remote folder, and SaveHarbor's policy of managing only the signed-in account's Steam Cloud folder (verified that Steam accepts files SaveHarbor writes there). Use when implementing Enshrouded discovery, download/restore targets, user hints, or diagnosing "the game shows two copies / my world went back in time / Steam conflict".
---

# Enshrouded and Steam Cloud

## The two tabs

Since 1.0 the session screen has a **SAVEGAMES:LOCAL** tab and a **SAVEGAMES:STEAM** tab. They are two folders:

| Tab | Folder | Steam tracking |
|---|---|---|
| Steam | `<Steam>\userdata\<steamid3>\1203620\remote\` | Every top-level file is listed in `..\1203620\remotecache.vdf` (size, times, SHA-1, `syncstate`) and synced with Valve's servers on launch/exit. `backup\` and `migrated\` subfolders are **not** tracked |
| Local | `%USERPROFILE%\Saved Games\keen games\Enshrouded\default\` | None |

The game numbers slots from 1 in its menu: `world0.ksd` is slot 1. Existing players' worlds live in the Steam folder (the 1.0 migration put them there).

## SaveHarbor policy (decided 2026-10-28, replaces the earlier "Local only" policy)

1. **One managed folder per PC: the Steam Cloud folder of the signed-in account.** `EnshroudedSaveRootLocator` reads the account from `HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser` (the userdata folder name); with Steam closed it takes the account whose folder changed last. A save folder chosen in Settings replaces it.
2. Discovery, downloads, restores, imports and removals all use that folder. The **Local tab is not managed** and not listed; one folder keeps every world to one card and one copy.
3. Steam backs the world up after every session, as for any Steam Cloud world. SaveHarbor's cloud versions and its own backups come on top.
4. SaveHarbor writes only `world<N>.ksd` files (and moves the game's rolling copies of a removed world into `saveharbor-aside\`). It never edits `remotecache.vdf`, character, global or settings files. Writes happen only while the game is closed (the shared download flow already refuses while it runs).
5. Never place a second copy of a world's GUID in the folder; `EnshroudedSlotMap.ResolveTargetPath` reuses the slot that holds the GUID.

## Verified behaviour (owner's PC, 2026-10-28)

- **P6 passed:** with the game closed and Steam running, an older version (save counter 18) was written over `remote\world1.ksd` after Steam had synced counter 20. Starting the game showed no Steam conflict dialog, the world loaded in the older state, kept its GUID, and after playing Steam synced the new file (`remotecache.vdf` SHA-1 equal to the file, `syncstate` 1).
- Two copies of one GUID (P3/P4 test copies) made the game give one copy a new GUID on save. Hence rule 5.
- **Untested:** removing a world file from `remote` (does Steam delete its cloud copy or restore the file on next launch?); a conflict when the same account played the world on a second PC that Steam had not synced yet. Steam then shows its own conflict dialog; the user should keep the newer one.

## Diagnosing user reports

| Report | Likely cause | Answer |
|---|---|---|
| "SaveHarbor doesn't see my world" | The world is on the Local tab, or in another Steam account's folder, or a Settings save folder points elsewhere | Bring a Local-tab world in with SaveHarbor's Import (pick its `world<N>.ksd` from the Local folder; it is copied into the managed folder), then delete the Local copy in the game; check the folder shown in Settings |
| "Steam asks which version to keep" | The same Steam account played the world on another PC before Steam synced | Keep the newer one (normally "local", the one SaveHarbor just placed) |
| "I see the world twice" | A copy with the same name in another slot (an old copy, or one on the Local tab) | Delete the old copy in the game |

## Manual verification steps (release checklist)

1. Fresh Windows user with Steam signed in and the game started once: download a shared world → it appears on the Steam tab in the first free slot and loads.
2. Play, quit → SaveHarbor shares; Steam's library page shows "Cloud status: Up to date"; `remotecache.vdf` SHA-1 of the file equals the file.
3. Download a newer version over an existing world → same slot, no Steam conflict dialog, GUID unchanged.
