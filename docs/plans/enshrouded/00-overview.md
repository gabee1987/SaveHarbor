# Enshrouded Support — Overview and Roadmap

**Branch:** `feature/enshrouded-support` (from `develop`). The owner performs all commits and pull requests manually (see the `git-workflow` skill).
**Status:** planning. Phase 0 (verification on a real install) is **done**, see plan 01. No production code has been changed yet.
**Date:** 2026-10-28.

## 1. Goal

Add Enshrouded (Keen Games, Steam app id 1203620) as SaveHarbor's third game, with the same rotating-host model as Windrose and Dragonwilds:
- one friend presses **Play**, gets the group's newest world, hosts it, and the world is shared again after exit;
- strict separation from the other two games (worlds, backups, cloud, locks, theme);
- an Enshrouded look of its own (cool slate panels, gold filigree frames) that stays as minimal as the other two skins;
- SaveHarbor Cloud (Supabase) support through one new `games` row; Google Drive keeps working through the existing per-game folder model;
- the companion web page knows the game.

Characters are never shared: in Enshrouded every player's character lives in that player's own files, separate from the world.

## 2. What is different from Dragonwilds

| Topic | Dragonwilds | Enshrouded |
|---|---|---|
| World unit | `<Name>.sav`, name = identity | `world<N>.ksd`, **N is a slot number**, identity = a GUID in the file header |
| Save folder | `%LOCALAPPDATA%\RSDragonwilds\...` (game-owned) | Steam Cloud folder `Steam\userdata\<steamid3>\1203620\remote\` **and** a game-owned Local folder `%USERPROFILE%\Saved Games\keen games\Enshrouded\default\` (1.0 "Local" tab) |
| Steam Cloud | Separate `remote` copy, hint after restore | The Steam folder **is** a save root. Writing there is seen by Steam immediately |
| World name | Readable from the save | Not yet readable (deep inside the compressed file). Fallback: named at first share |
| Game's own backups | `<world>.sav.backup` | `backup\world<N>.ksd.backup-1..9` (9 = oldest), not tracked by Steam |

The consequences: the adapter must map a world GUID to a local slot (reuse the slot that already holds the GUID, else the first free one), discover two save roots, and SaveHarbor's home for shared worlds should be the **Local** folder so Steam Cloud never sees SaveHarbor's writes (owner decision, plan 02 §3).

## 3. Plans and order

| # | Plan | Size | Depends on |
|---|---|---|---|
| 01 | [Save format and verification](01-save-format-and-verification.md) — verified layout, header, Phase 0 results, open items | — | done |
| 02 | [Adapter, slots and Steam Cloud](02-adapter-slots-and-steam-cloud.md) — game definition, adapter, slot mapping, roots, hints | M | — |
| 03 | [Backend and web](03-backend-and-web.md) — Supabase migration and tests, companion page | S | — |
| 04 | [Theme and UI](04-theme-and-ui.md) — the Enshrouded skin, shell view, ambient effect | M | — |
| 05 | [Testing](05-testing.md) — unit tests, fixtures, manual checks | S | 02 |
| 06 | [Tasks and handoff](06-tasks.md) — ordered tasks with checkpoints for the implementing agent | — | — |

Recommended execution: **03 (migration first, so uploads are accepted) → 02 → 05 → 04**. 04 can run in parallel with 02 after the game definition exists.

## 4. Key decisions (recommendations; owner to confirm)

| Decision | Recommendation | Alternative |
|---|---|---|
| Home folder for shared worlds | **Replaced 2026-10-28 (FINDINGS.md): the signed-in account's Steam Cloud folder.** Originally: The **Local** folder (`...\keen games\Enshrouded\default\`). SaveHarbor never writes into Steam's `remote` folder. Worlds found in `remote` can be shared *from* there once; after that the group plays the Local copy | Write into `remote` and rely on the "choose Local files" hint, as Dragonwilds does |
| World identity | The 16-byte GUID at header offset 12 (`WorldId` = its lowercase hex). File slot is a local detail | Slot number as id (breaks on a friend's PC) |
| Slot allocation on download | Reuse the slot whose header has the same GUID; else first free `world<N>.ksd` with N from 0 upward | Ask the user |
| World display name | Spike to read it from the file (time-boxed); fallback "Enshrouded world <N>" locally and the name given at first share in the cloud | Always ask |
| Platforms | Steam on Windows only in v1; `SaveRoot` override for anything else | — |
| Enshrouded Steam Cloud user guidance | One-time notice when a Steam Cloud world is shared: "Play this world from the Local tab from now on" | — |
| Dedicated-server export | Out of scope (future plan; `world0.ksd` in the server's `savegame` folder is the same format) | — |

## 5. Skills created (`.claude/skills/`)

| Skill | Purpose |
|---|---|
| `enshrouded-save-format` | Verified 1.0 layout, `.ksd` header, what to sync and what never to touch |
| `enshrouded-steam-cloud` | Two save roots, how Steam tracks `remote`, the Local-folder policy |
| `enshrouded-process-and-launch` | App id, process name, settle timing, save cadence |
| `enshrouded-world-sharing` | Rotating host with slots, characters, host permissions, alternatives |
| `enshrouded-theme` | Palette, typography, filigree frames, motion, accessibility |

`multi-game-separation` and `git-workflow` were updated for the third game and the new branch.

## 6. Compliance and external services

- No new external service. SaveHarbor Cloud (Supabase, existing) and Google Drive (existing) are used as before; both remain subject to the compliance note in the Dragonwilds overview for any use beyond a private hobby group.
- SaveHarbor copies whole `.ksd` files and never edits their content. The owner should read Keen Games' EULA for any clause on third-party tools, as was done for the other games.
- No Keen Games artwork, logos, fonts or textures are used. The theme uses original vector shapes and an OFL font.

## 7. Sources

- Verified on the owner's install on 2026-10-28, after the first 1.0 launch migrated the Early Access saves (plan 01).
- enshrouded.com — 1.0 release changelog (new save format; Local / Steam Cloud selection; Local folder path); hotfix 1.0.0.2 (character migration crash).
- Keen Games Zendesk — Dedicated Server FAQ (characters are never stored on the server), Save Game Management (rolling copies).
- Community and hosting-provider articles (Early Access slot ids, transfer procedure). Only used for background; everything the code relies on was verified locally.
