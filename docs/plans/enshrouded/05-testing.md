# Plan 05 — Testing

**Branch:** `feature/enshrouded-support`
**Depends on:** 02.
**Skills:** `build-and-verify`, `coding-standards`.

## 1. Fixtures (`SaveHarbor.Tests/Games/Fixtures/Enshrouded/`)

Synthetic files only. Never copy a real save into the repo.

- `EnshroudedFixture.WriteWorld(path, guid, counter, timestampUtc, payloadBytes = 64)`: writes a 48-byte header (`SV2C`, version 2, header size 48, payload size, GUID, counter, timestamp, 8 zero bytes) followed by `S2BH` and random bytes. Does **not** need valid zstd; the adapter never decompresses.
- `WriteCharacter(path, guid)`: same header, name `character0.ksd`.
- `WriteRoot(dir, worlds: (slot, guid, counter)[])` plus a `backup\` folder with `world<N>.ksd.backup-9` … entries.
- `BackupHarness` (`Support/BackupHarness.cs`) gains an Enshrouded branch with a `TestPathProvider` that points the Local root and one fake Steam root (`userdata\12345\1203620\remote`) under the temp directory. The Steam locator must be injectable (`IEnshroudedSaveRootLocator`) so tests never read the registry.

## 2. Unit tests (`SaveHarbor.Tests/Games/Enshrouded*.cs`)

| Area | Cases |
|---|---|
| `EnshroudedSaveHeader` | Reads GUID, counter, timestamp; rejects wrong magic, wrong version, short file, zero-length; does not read past 48 bytes (use a file with exactly 48 bytes) |
| Discovery | Finds `world0.ksd`, `World12.KSD`; ignores `character0.ksd`, `global.ksd`, `world1.ksd.backup-3`, `migrated\*`, `worldX.ksd`; unions Local and Steam roots; Local root listed when missing with `CanReceiveWorlds = true`; Steam root `CanReceiveWorlds = false`; `SaveRoot` override yields exactly one Local root |
| `EnshroudedSlotMap` | Same GUID → existing slot (even if it is `world7.ksd`); no match → `world0.ksd` in an empty root; gaps filled (`0,1,3` present → 2); unreadable header files are skipped, never chosen as targets |
| Restore | Archive carrying `world5.ksd` restores into the resolved slot name; a second restore of a newer version replaces the same file; mtime updated; `.saveharbor-prev` removed; mid-way failure rolls back; GUID mismatch between payload and `expectedWorldId` → `InvalidDataException` and nothing changed |
| Import | `.ksd` world accepted; `world3.ksd.backup-4` accepted with its counter; character file refused; file > limit refused; comparison says older / newer by counter then timestamp |
| Untouched files | `character*.ksd`, `global.ksd`, `user_settings.json` hashes equal before and after backup, restore, import and remove |
| Cross-game | Importing a Windrose or Dragonwilds zip into Enshrouded is refused, and the reverse |
| Health | Same GUID in Local and Steam → one notice; otherwise null |
| Leak checks | `GameRegistry.All` has three games; `ToStorageKey` round-trips; `AppOptionsLoader.LoadGameOptions()` has an entry per enum value with a non-empty launch URI |

## 3. Supabase

`supabase/tests/games.sql` as in plan 03. Run `supabase test db`.

## 4. Web

`npm test` in `web/` after the games list change.

## 5. Manual checks (owner)

1. Fresh clone of the group on a second PC or second Windows user: Play → the world lands in `...\Enshrouded\default\world0.ksd`; the Local tab shows it; it loads.
2. Share from the Steam Cloud tab once; the notice appears; the next Play on the same PC downloads into Local; both copies are visible in the game; the health notice is shown.
3. Play while a friend holds the lock → blocked with their name (unchanged flow).
4. P5 from plan 01 on the first real two-player session; record the outcome in `enshrouded-world-sharing`.
5. `build-and-verify` smoke test for Windrose and Dragonwilds after the theme work.
