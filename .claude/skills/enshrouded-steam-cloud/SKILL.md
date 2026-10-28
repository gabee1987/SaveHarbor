---
name: enshrouded-steam-cloud
description: How Enshrouded's two save tabs (Local and Steam Cloud) map to folders, how Steam tracks the remote folder, and SaveHarbor's policy of writing only to the Local folder. Use when implementing Enshrouded discovery, download/restore targets, user hints, or diagnosing "the game shows two copies / my world went back in time".
---

# Enshrouded and Steam Cloud

## The two tabs

Since 1.0 the character and world selection screens have a **Local** tab and a **Steam Cloud** tab. They are two folders:

| Tab | Folder | Steam tracking |
|---|---|---|
| Steam Cloud | `<Steam>\userdata\<steamid3>\1203620\remote\` | Every top-level file is hashed in `..\1203620\remotecache.vdf` and synced with Valve's servers on launch/exit. `backup\` and `migrated\` subfolders are **not** tracked |
| Local | `%USERPROFILE%\Saved Games\keen games\Enshrouded\default\` | None |

Existing players' worlds live in the Steam Cloud folder (the 1.0 migration put them there).

## SaveHarbor policy (decided, plan 02 §3)

1. **Writes go to Local only.** Downloads, restores and imports always target the Local root. SaveHarbor never creates, replaces or deletes anything under `remote\`. This avoids Steam conflict dialogs, cross-PC overwrites and any need to touch `remotecache.vdf`.
2. **Steam Cloud worlds are listed read-only** (`GameSaveRoot.CanReceiveWorlds = false`) so a player can share an existing world once.
3. After such a share, show once per world: "Shared from your Steam Cloud tab. From now on, play this world from the Local tab so SaveHarbor can keep it up to date." (`IGameDefinition.PostShareHint`).
4. After every download: `PostRestoreHint` = "Open this world from the Local tab in Enshrouded."
5. If the same GUID exists in both roots, `CheckHealth` reports one notice ("also exists in your Steam Cloud tab; play the Local one"). No automatic deletion.
6. Removing a world removes only the Local copy; `RemoveWorldNote` says Steam Cloud keeps its own.

## Verified behaviour

- A world file placed in the Local folder (even into a folder the game had not created) appears in the Local tab and loads.
- Replacing a world file with an older rolling copy produced no Steam conflict dialog (file in `remote`, same account). Not relied on, since the policy avoids `remote` writes.
- **Untested:** Steam's reaction to files written into `remote` by an external tool while the same account is logged in elsewhere. Irrelevant under the policy.

## Diagnosing user reports

| Report | Likely cause | Answer |
|---|---|---|
| "I see the world twice" | A copy in each tab (one shared from Steam Cloud, one downloaded to Local) | Play the Local one; the health notice says so. Optionally delete the Steam Cloud copy in-game |
| "My progress went back" | Hosted the Steam Cloud copy after a friend's newer version was downloaded to Local | Same as above; SaveHarbor only ever updates Local |
| "SaveHarbor doesn't see my world" | The world is in `remote` of a different Steam user folder, or a `SaveRoot` override points elsewhere | Check the roots listed in Settings |

## Manual verification steps (release checklist)

1. Fresh PC or Windows user: Play → `...\Enshrouded\default\world0.ksd` exists, Local tab shows it, it loads.
2. Share a Steam Cloud world → notice appears; next Play downloads into Local; health notice appears while both copies exist.
3. Steam Cloud tab worlds unchanged (compare `remotecache.vdf` hashes before/after; they must be identical).
