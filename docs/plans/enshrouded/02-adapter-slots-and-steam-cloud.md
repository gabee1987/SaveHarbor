# Plan 02 — Game Definition, Save Adapter, Slot Mapping and Steam Cloud Policy

**Branch:** `feature/enshrouded-support`
**Depends on:** nothing in code (the game abstraction from the Dragonwilds work exists). Plan 03's migration must be pushed before a release.
**Skills:** `enshrouded-save-format`, `enshrouded-steam-cloud`, `enshrouded-process-and-launch`, `enshrouded-world-sharing`, `multi-game-separation`, `coding-standards`.

## 1. Objective

Add Enshrouded as a third `IGameDefinition` + `IGameSaveAdapter` pair so that the shared Play flow works unchanged: pull the newest `world<N>.ksd`, lock, launch, share after exit. The only new mechanics are (a) GUID-based identity with local slot mapping and (b) two save roots with a Local-folder write policy.

## 2. Game definition

`Infrastructure/Games/Enshrouded/EnshroudedGameDefinition.cs` (mirror `DragonwildsGameDefinition`):

| Member | Value |
|---|---|
| `Id` | `GameId.Enshrouded` (new enum member; `ToStorageKey` → `"enshrouded"`) |
| `DisplayName` | `"Enshrouded"` |
| `LaunchUri` | options; default `steam://rungameid/1203620` |
| `ProcessNames` | `["enshrouded"]` (exact, no `.exe`). Add `"enshrouded_server"` only if the owner wants a running dedicated server to count as "game running" (**not** by default) |
| `SaveSettleDelay` | 15 s (the game writes the world and a rolling copy on exit) |
| `ThemeDictionary` | `/Resources/Themes/Enshrouded.Colors.xaml` |
| `SkinDictionary` | `/SaveHarbor.App;component/Resources/Themes/Enshrouded/Enshrouded.Theme.xaml` |
| `PostRestoreHint` | `"Open this world from the Local tab in Enshrouded."` |
| `RemoveWorldNote` | `"If the world also exists in your Steam Cloud tab, Enshrouded keeps that copy."` |
| `PlayHint` | same wording pattern as Dragonwilds |

Options (`appsettings.json` → `SaveHarbor:Games:Enshrouded`): `LaunchUri`, `ExecutablePath`, `SaveRoot` (override; when set it is the **only** root and is treated as Local).

**Leaks to fix while here** (the abstraction's own rule: a third game must need only a definition, an adapter and a registration line):
- `AppOptionsLoader.DefaultLaunchUri` switch → move the default launch URI into each `IGameDefinition` (or a static `GameDefaults` table keyed by `GameId` inside the game's folder) so the loader does not list games.
- `MainWindow.xaml` shell `DataTrigger` per game → replace with a `DataTemplateSelector` (or a per-game `ShellViewType` on the definition) so adding a game does not edit `MainWindow.xaml`. Keep the "only the active game's template is instantiated" behaviour.
- `GameIdExtensions.ToStorageKey` switch: acceptable to extend (it is the one allowed place), but keep it exhaustive.

## 3. Save roots and the Local-folder policy (owner decision, recommended)

`DiscoverSaveRootsAsync` returns up to N+1 roots:

| Provider key | `WorldsPath` | Detail | Writable |
|---|---|---|---|
| `"local"` | `%USERPROFILE%\Saved Games\keen games\Enshrouded\default\` | "Local · N worlds" | **Yes** (the only write target) |
| `"steam"` (one per `<steamid3>`) | `<Steam>\userdata\<steamid3>\1203620\remote\` | "Steam Cloud · N worlds" | **No** (share from here, never write) |

Rules:
- The Local root is listed even when the folder does not exist yet (the adapter creates `default\` on first write; the game accepts a folder it did not create, P4). Detail then reads "Local · not created yet".
- Downloads, restores and imports always target the Local root. `GetExpectedWorldPath(root, worldId)` throws `InvalidOperationException("Enshrouded worlds are placed in the Local folder only.")` for a Steam root. Callers that pick a root for a download must pick the Local one; check how the Windrose flow chooses between multiple profiles and reuse that selection, defaulting to Local.
- Sharing (upload) works from any root. When the uploaded world came from a Steam root, add one activity line and a toast: "Shared from your Steam Cloud tab. From now on, play this world from the Local tab so SaveHarbor can keep it up to date." The downloaded copy on the same PC lands in Local; the Steam copy stays and is not deleted (Steam would sync the deletion; too surprising).
- If the same GUID exists in both a Steam root and the Local root, `CheckHealth` returns one notice: "This world also exists in your Steam Cloud tab. Enshrouded shows both; play the Local one." No automatic action.

`GameSaveRoot` has no "writable" flag today. Prefer adding a `bool CanReceiveWorlds` with a default of `true` (additive; Windrose and Dragonwilds unaffected) over a per-game `switch`.

## 4. Identity and slot mapping

- `WorldId` = lowercase hex of the 16 header bytes at offset 12 (32 chars, `SafePath.IsSafeSegment` true). Cloud layout stays `worlds/<worldId>/...`; the slot never reaches the cloud.
- `WorldName` = result of the name spike (plan 06 T2.6) or `"Enshrouded world <N>"`. `Subtitle` = `"Slot <N> · Local"` or `"Slot <N> · Steam Cloud"`.
- `CreatedAt` = file creation time; `LastModifiedAt` = file mtime (equal to the header timestamp); `FileCount` = 1.
- **Slot resolution for a target root** (`EnshroudedSlotMap`, pure, unit-tested):
  1. Enumerate `world<N>.ksd` in the root, read each header GUID.
  2. If any file's GUID equals the wanted `WorldId` → that file (reuse; this is what makes repeated downloads land in the same place).
  3. Else the smallest N ≥ 0 with no `world<N>.ksd` → new file.
  4. Files that fail the header check are skipped (and never overwritten).
- `GetExpectedWorldPath(root, worldId)` returns the resolved slot path. Because the path depends on folder contents, it is computed at restore time, not cached in sync state.
- `FileSetPayloadStrategy.Restore` refuses a backup whose entry name differs from the target file name. Enshrouded archives carry `world<N>.ksd` with the **uploader's** N, which can differ from the downloader's slot. Options: (a) the adapter stages the payload under a fixed name `world.ksd` and the restore renames it to the resolved slot; (b) relax the check per adapter. Choose **(a)**: `GetPayloadFiles` stays the real path, but `Stage` uses `adapter.GetArchiveFileName(world)` (new optional member, default = real name) and `Restore`/`Import` use `adapter.GetExpectedWorldPath` for the target. This is a small, generic extension of the strategy; Windrose and Dragonwilds return the real name and behave as today.
- `ValidatePayload(payloadRoot, expectedWorldId)`: the staged file must pass the header check and its GUID must equal `expectedWorldId`. Throws `InvalidDataException` otherwise (the payload is untrusted).

## 5. Other adapter members

| Member | Behaviour |
|---|---|
| `PayloadKind` | `FileSet` |
| `DiscoverWorldsAsync` | Union over all roots; `^world\d+\.ksd$` files whose header passes; ordered by `LastModifiedAt` desc |
| `ReadWorldFactsAsync` | "Slot", "Save counter", "Saved at (from file)", "Location (Local / Steam Cloud)", "Game copies kept" (count of `backup\world<N>.ksd.backup-*`) |
| `InspectWorldAsync` | The facts plus the header fields and the list of rolling copies with their counters and timestamps. Nothing from inside the compressed blocks |
| `ImportFileFilter` | `"Enshrouded world (world*.ksd)|*.ksd"` |
| `ReadImportCandidateAsync` | Accept `.ksd` ≤ 256 MB whose header passes and whose name is `world<N>.ksd` or `world<N>.ksd.backup-<K>`; `ImportCandidate` carries the GUID, save counter and timestamp so the comparison window can say "older / newer than yours" |
| `GetGameBackupCopyPath` | Newest `backup\world<N>.ksd.backup-*` (lowest K) or null |
| `GetSaveFormatVersion` | `"2"` from the header (so a future format 3 cannot be restored into a 2 game; cheap and useful) |
| `StageGameFiles` / `PlaceGameFiles` | No-ops |
| `MoveGameFilesAside` | Moves `backup\world<N>.ksd.backup-*` of a removed world into `<root>\saveharbor-aside\<timestamp>\` (never deletes) |
| `RememberWorldState` / `CheckHealth` | Only the duplicate-GUID notice from §3; otherwise null |
| `MoveLeftoversAside` | `.saveharbor-tmp` / `.saveharbor-prev` next to the world |

Header parsing lives in `EnshroudedSaveHeader` (`TryRead(path)` → record with `Guid`, `SaveCounter`, `SavedAtUtc`, `FormatVersion`; reads the first 48 bytes only). Keep the adapter under 250 lines by putting roots, slot map and header in their own files.

## 6. Steam Cloud behaviour to tell users (text keys)

- After a download: `PostRestoreHint` (above).
- First share from Steam root: the toast/activity line in §3.
- Settings page, Enshrouded only: a short paragraph "SaveHarbor keeps shared Enshrouded worlds in the Local save folder. Your Steam Cloud worlds are listed so you can share them, but SaveHarbor never changes them." (`Enshrouded.SaveRootsHint`).

## 7. Acceptance criteria

- [ ] Switching to Enshrouded lists Local and Steam Cloud worlds with slot, location and save counter; the other games are unaffected.
- [ ] Download of a world that is not on this PC creates `world<N>.ksd` in Local in the first free slot; a second download of a newer version replaces the **same** file.
- [ ] A world shared from the Steam Cloud tab is uploaded correctly and the one-time notice appears.
- [ ] After Play, the new version is uploaded; `character*.ksd`, `global.ksd` and `user_settings.json` are byte-identical before and after every operation.
- [ ] A `.ksd` that is not a world (character file, truncated file, wrong magic) is refused on import and on restore with `InvalidData`.
- [ ] No `switch (GameId)` was added outside `GameIdExtensions`; `MainWindow.xaml` and `AppOptionsLoader` no longer list games.

## 8. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Keen changes the header or the Local folder in a patch | Low–medium | Header check fails closed; `SaveRoot` override; adapter isolated |
| Players keep hosting the Steam Cloud copy, so SaveHarbor's Local copy drifts | Medium | Duplicate-GUID health notice; the one-time share notice; subtitle shows the location |
| Host permissions differ when a friend hosts (P5 open) | Low | First two-player session; document in the skill either way |
| The world name cannot be read | Medium | Naming at first share; `"Enshrouded world <N>"` locally |
