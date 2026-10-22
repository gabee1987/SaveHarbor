---
name: dragonwilds-world-sharing
description: How RuneScape Dragonwilds worlds can be shared between friends (rotating host via SaveHarbor vs the official dedicated server), host/ownership semantics, character separation, and the community-proven transfer procedure. Use when designing Dragonwilds sync behaviour, explaining limits to users, or evaluating alternatives.
---

# Dragonwilds World Sharing

## Rotating-host model (what SaveHarbor does)

Dragonwilds co-op is host-based: the host's world `.sav` is the world. To let any friend host,
SaveHarbor moves the latest world file between PCs:

1. Pull latest `.sav` from the group's Dragonwilds cloud folder (backup local first).
2. Lock the world in the cloud so only one friend hosts.
3. Launch the game; the host loads that world and friends join through the normal in-game flow.
4. After the host exits: upload the `.sav` as version N+1, release the lock.

Only the **host** needs SaveHarbor's Play flow. Joining players do not need to download anything —
their character JSON stays local. The UI should make this clear ("Hosting tonight? Press Play.
Just joining? Join from the game as usual.").

## Facts this relies on

| Claim | Confidence | Notes |
|---|---|---|
| Worlds are portable files (hosting guides copy local `.sav` to servers) | Medium-high | |
| A copied world can be hosted by a **different Steam account** | Medium | **[verify]** with two accounts before release |
| Character progress is separate (per-player JSON) and not in the world | High | joining/hosting keeps each player's own character |
| World name stored inside the file; do not rename | Medium | |
| No ownership check on world files for listen-server hosting | Medium | dedicated servers have an optional Owner ID (since Apr 2026), not relevant here |

Community transfer procedure (matches our flow): close game → copy `.sav` → on target, remove any
stale newer copy of the same world → paste → start the game.

## Official alternative: dedicated server

- Free dedicated server since update 0.11 "Dowdun Reach" (Mar 2026), Windows + Linux, up to 6 players,
  roles Owner/Admin/User, direct IP + auto-discovery (Apr 2026). Server app id 4019830,
  executable `RSDragonwildsServer.exe` (medium confidence). Server loads the **newest** `.sav` in
  its `Saved/Savegames` folder.
- Trade-off for the group: always-on world without rotation, but someone must run/host a machine
  or pay a hosting provider. SaveHarbor's rotating-host model costs nothing and needs no uptime.
- Out of scope now. A future "upload world to my dedicated server folder" export is a natural
  extension; do not build it unless requested.
- Third-party hosting providers or sync tools (e.g. Syncthing) would require a compliance/privacy
  review before recommending them in any organisational context.

## Edge cases specific to Dragonwilds

- Host also has an unrelated world with the same name → see same-name rule in `dragonwilds-save-format`.
- Version mismatch between friends after a game patch: a world saved by a newer game build may
  not load on an older one. Record the game build in version metadata if it can be read cheaply
  (Steam `appmanifest_1374490.acf` `buildid`) — **[verify]** path; show a warning when the
  uploader's build is newer than the local one.
- Crossplay (PS5/Xbox/Switch 2) players cannot use SaveHarbor; they can only join a PC host.
