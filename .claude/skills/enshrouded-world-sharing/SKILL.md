---
name: enshrouded-world-sharing
description: How Enshrouded worlds are shared between friends with SaveHarbor (rotating host via world<N>.ksd in the Local folder), what stays per player (characters), host/permission semantics, slot mapping on a friend's PC, and the alternatives (Adventure Sharing, dedicated server). Use when designing Enshrouded sync behaviour, explaining limits to users, or evaluating alternatives.
---

# Enshrouded World Sharing

## Model

Rotating host, like Windrose and Dragonwilds: the group's world is one `world<N>.ksd`. Whoever presses **Play** gets the newest version into their Local save folder, hosts it, and SaveHarbor shares the result after they quit. Friends join through the game's normal multiplayer.

## What travels and what stays

| Data | Where | Shared? |
|---|---|---|
| World (terrain, buildings, chests, NPCs, flame level, quests) | `world<N>.ksd` | **Yes** |
| Characters (level, skills, inventory, equipment) | each player's own `character<N>.ksd` | **No** — never leaves the PC. A player keeps their character whoever hosts |
| Adventure Sharing list, user settings | `global.ksd`, `user_settings.json` | No |

Keen's own dedicated server works the same way: "Character progression is stored only on clients; world progression on the server."

## Slots on a friend's PC

The file name is a local slot (`world0`, `world1` …) and the GUID inside the header is the identity (`enshrouded-save-format`). SaveHarbor puts a downloaded world into the slot that already holds that GUID, else the first free slot. The game shows it in the **Local** tab. Two files with the same GUID show as two worlds, so never let a world land in two slots.

## Host and permissions (**[open]**, needs the first two-player session)

Nothing in the files ties a world to a Steam account that we could find (worlds load after renaming, copying and moving between folders on one account). 1.0 remembers per-player roles inside the world, so when a friend hosts your world, your role in that world is expected to persist. Verify: original host joins a friend-hosted copy → can build, open chests, use altars. Record the result here.

## User-facing rules (texts)

- Play hosts for the group; "Just joining? Start Enshrouded and join your host." (`Enshrouded.JoinHint`).
- After a download: open the world from the **Local** tab.
- A world shared from the Steam Cloud tab: from then on play the Local copy (one-time notice).
- Play while a friend holds the lock → blocked with their name (shared flow).

## Alternatives and why SaveHarbor still helps

| Alternative | What it is | Gap |
|---|---|---|
| Adventure Sharing (official, since Update 8 / 1.0) | Publish a world publicly; others download and *visit* it, alone or as a party; votes, tags, screenshots | A public showcase. No private groups, no two-way progress, no continuing a shared game |
| Official dedicated server (`enshrouded_server`, free, Windows) | The world lives on one always-on machine; up to 16 players | Needs a 24/7 PC or a hosting provider (third-party service: compliance review before any non-hobby use) and port forwarding |
| Manual copying | Copy `world<N>.ksd` between friends | Error-prone (which copy is newest, slot clashes, Steam tab confusion); exactly what SaveHarbor automates |

Future: an "export to dedicated server" action is the same file (`savegame\world0.ksd` on the server). Out of scope for v1.

## Testing sharing on one PC

Two Windows users, or one user with the `SaveRoot` option pointing to a temp folder for the second "player", plus the LocalTest cloud provider. Verified single-PC checks on 2026-10-28: slot rename, older-copy replacement, duplicate GUID, Local-folder load (plan 01 §4).
