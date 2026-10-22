# Plan 03 — RuneScape: Dragonwilds Save Integration

**Branch:** `feature/dragonwilds-support`
**Depends on:** 02 (abstraction) and 04 (scoping) before release. Development can start against the LocalTest provider once 02 is done.
**Skills:** `dragonwilds-save-format`, `dragonwilds-world-sharing`, `dragonwilds-steam-cloud`, `dragonwilds-process-and-launch`, `multi-game-separation`.

## 1. Objective

Add Dragonwilds as a second, fully isolated game. The same Play flow pulls the newest shared world `.sav`, locks it, launches the game, and shares the world after exit. Character data stays untouched.

## 2. Phase 0 — Verification on a real install (owner, about 30 minutes, **before coding**)

The research (2026-10-05) is partly community-sourced. Record the answers in the skills, replacing every **[verify]** marker.

| # | Check | How | Affects |
|---|---|---|---|
| V1 | `SaveGames` layout: subfolders? exact file names? game backup files? | Explorer: `%LOCALAPPDATA%\RSDragonwilds\Saved\SaveGames` | Adapter discovery and payload |
| V2 | `.sav` header | `Format-Hex <file> -Count 16` → `GVAS`? | Sanity validation |
| V3 | Process names | Task Manager → Details, in game | Game definition |
| V4 | Steam Cloud scope | `...\Steam\userdata\<id>\1374490\remote` contents | Steam Cloud hint |
| V5 | Portability | Friend copies your `.sav` into their `SaveGames`, hosts, you join | Whole feature viability |
| V6 | Overwrite while Steam Cloud is diverged | Restore a file, then launch → does Steam prompt? | Hint wording |
| V7 | Does the world's in-game "last played" or ordering depend on file mtime? | Restore and launch | `LastWriteTimeUtc` rule |
| V8 | Build id location | `steamapps\appmanifest_1374490.acf` → `buildid` | Version-mismatch warning |

**Stop condition:** if V5 fails (a world cannot be hosted by another account), the rotating-host model does not work. Then rescope to "backup and restore only", plus a recommendation to use the official dedicated server, and come back to the owner.

## 3. Game definition

`Infrastructure/Games/Dragonwilds/DragonwildsGameDefinition.cs`. Values are in the `dragonwilds-process-and-launch` skill (app id 1374490, exact process names, 15 s settle, theme `Themes/Dragonwilds.Colors.xaml`).
Options (`appsettings.json` → `SaveHarbor:Games:Dragonwilds`): `LaunchUri`, `ExecutablePath`, `SaveRoot` (default `%LOCALAPPDATA%\RSDragonwilds\Saved\SaveGames`). The `SaveRoot` override covers non-Steam storefronts for advanced users and also serves as the test seam.

## 4. Save adapter

`Infrastructure/Games/Dragonwilds/DragonwildsSaveAdapter.cs` (target under 200 lines).

| Member | Behaviour |
|---|---|
| `PayloadKind` | `FileSet` |
| `DiscoverSaveRootsAsync` | One root: `SaveRoot` if it exists. `Detail` = "Steam · N worlds". Returns nothing if the folder is missing (the UI says "Start Dragonwilds once to create its save folder") |
| `DiscoverWorldsAsync` | `Directory.EnumerateFiles(root, "*.sav", TopDirectoryOnly)` (subject to V1), excluding names that end with known game-backup suffixes. One `GameWorld` per file |
| `ReadWorldAsync(path)` | Builds `GameWorld`: `WorldId` = `FileNameSanitizer.MakeSafeFileName(stem)`, `WorldName` = stem (display as-is, including `·`), `Subtitle` = "" (or "Last played <relative>"), `SavePath` = **file path**, `CreatedAt` = file creation time, `LastModifiedAt` = mtime, `SizeBytes`, `FileCount = 1` |
| `GetPayloadFiles(world)` | `[world.SavePath]` |
| `GetImportTargetPath(root, worldId, sourceFileName)` | `Path.Combine(root.WorldsPath, sourceFileName)`. The original file name is carried in the archive manifest; never derive it from the sanitised id. Must go through `SafePath.CombineUnderRoot` (plan 04 §7) |

Optional sanity check `IsLikelyWorldFile(path)`: the first 4 bytes equal `GVAS` (only if V2 confirms). It is used on download and import to reject wrong files with `AppErrorCode.InvalidData`.

## 5. Backup and restore for single-file worlds

`ZipBackupService` currently assumes a directory (`SavePath` is a folder; `CreateArchive` copies a directory tree; restore replaces a directory). Generalise it **by payload, not by game**:

- `CreateBackupAsync(world)`: stage `adapter.GetPayloadFiles(world)` into `temp/world/`, preserving each file's path relative to a payload root. For `Directory` the root is the world folder (unchanged behaviour); for `FileSet` it is the folder that contains the file.
- `BackupManifest`, schema version 2, adds `PayloadKind` and `Files: [{ "relativePath": "My World.sav", "sha256": "…", "sizeBytes": 123 }]`. A v1 manifest (Windrose legacy) implies `Directory`.
- Restore:
  - `Directory`: unchanged (replace the directory).
  - `FileSet`: for each file → write `<target>.saveharbor-tmp` → `File.Replace(tmp, target, target + ".saveharbor-prev")` (or `File.Move` if the target does not exist) → set `LastWriteTimeUtc = now` (subject to V7) → delete `.saveharbor-prev` after every file succeeds. On failure, roll back from `.saveharbor-prev`.
- If the class approaches 300 lines, extract `DirectoryPayloadStrategy` and `FileSetPayloadStrategy` (`IPayloadStrategy` with `Stage` and `Restore`). Two real implementations justify the abstraction.

`DirectoryHashCalculator` covers `Directory`; add a per-file hash list for `FileSet` (reuse `FileHashCalculator`).

## 6. Sync and Play specifics

- **Unsynced same-name world:** in the prepare flow (plan 05 §3.2 step 7), if `LocalBaseVersionNumber is null` **and** a local file with that name exists → return a new outcome `NeedsReplaceUnsyncedConfirmation(worldName, localModifiedAt)`. The dialog reads: "You have a local world named 'X' that was never shared. Replace it with the group's version? A backup is created first." This applies to both games, but matters mainly for Dragonwilds' name-based ids.
- **No change after play:** in the finish flow, if the payload hash equals the launched version's hash → release the lock without uploading ("No changes to share"). Generic, and useful for both games.
- **Steam Cloud hint:** after a Dragonwilds download, show the stage/activity text from the `dragonwilds-steam-cloud` skill. It comes from an optional `IGameDefinition.PostRestoreHint`, so no game `switch` appears in shared code.
- **Build mismatch warning (optional, V8):** `CloudVersionMetadata.GameBuildId` (nullable, additive, so older manifests still parse). If the cloud build is greater than the local build → warn "Update Dragonwilds in Steam before playing this world." Implement only if V8 confirms a cheap read. Otherwise skip it.
- **Joining players:** Play is a *host* action. Add one line of helper text in the Dragonwilds theme under the Play button: "Just joining? Start Dragonwilds normally and join your host." (UiText key `Dragonwilds.JoinHint`).
- **Cloud layout:** in the Dragonwilds shared folder, `worlds/<sanitisedStem>/versions/<versionId>.zip`. The zip contains `world/<Original Name>.sav` plus the manifest. Never upload the bare `.sav`; the zip carries the original file name and hash.

## 7. Tests (added to plan 07 inventory)

- Discovery: finds `.sav` files, ignores backups, ignores `SaveCharacters`, handles a missing root, and handles names with `·` and spaces.
- `WorldId` sanitisation is stable (the same stem always gives the same id).
- `FileSet` backup → restore round-trip: bytes equal, file name preserved, mtime updated, `.saveharbor-prev` removed.
- Restore failure mid-way rolls back to the original bytes.
- Import of a Windrose zip into Dragonwilds is rejected, and vice versa.
- `GVAS` check (if enabled) rejects a non-world file.
- `SaveCharacters` content is unchanged after every operation (hash before and after).

## 8. Step order

1. Phase 0 verification, then update the skills.
2. Game definition, options and registration (the switch shows Dragonwilds; discovery works read-only).
3. Payload-based backup and restore generalisation (Windrose behaviour unchanged; v1 manifests still restore).
4. Dragonwilds adapter, plus tests.
5. Unsynced-name confirmation, no-change skip, post-restore hint, join hint.
6. Optional build-id warning.
7. End-to-end on real installs with two friends (LocalTest first, then Google Drive with a dedicated Dragonwilds folder).

## 9. Acceptance criteria

- [ ] Switching to Dragonwilds lists the local worlds with correct names; Windrose data is unaffected.
- [ ] A friend on a second PC presses Play → gets the newest world, hosts it, and others can join.
- [ ] After exit, the world is uploaded as v+1. Character files are byte-identical before and after all operations.
- [ ] Play while another friend hosts → blocked with their name.
- [ ] The Dragonwilds cloud folder cannot be configured as the Windrose folder, and vice versa.
- [ ] Every **[verify]** marker in the Dragonwilds skills is resolved or explicitly accepted as unknown.

## 10. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Worlds are not hostable by another account | Low–medium | Phase 0 V5 stop condition |
| Steam Cloud reverts the restored world | Medium | Hint, mtime = now, hash check before upload |
| Game patch changes the save location or format | Low | `SaveRoot` override; adapter is isolated |
| Same-name worlds overwritten | Low | Explicit confirmation plus automatic backup |
| Jagex terms on save manipulation | Low | SaveHarbor only copies whole files and never edits contents. Still, have the owner read the game's ToS / EULA for any clause on third-party tools |
