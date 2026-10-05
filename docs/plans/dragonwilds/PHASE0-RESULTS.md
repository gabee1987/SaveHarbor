# Phase 0 results (Dragonwilds, owner-filled)

To be completed by the owner on a real Dragonwilds installation. Use placeholder text for any account or machine identifiers; do not paste real user ids, Steam ids or paths that contain a personal name. V5 is the go/no-go check for the rotating-host model.

| ID | Question | Result | Date |
|----|----------|--------|------|
| V1 | Contents of %LOCALAPPDATA%\RSDragonwilds\Saved\SaveGames (subfolders? exact names? backup files?) | | |
| V2 | First 4 bytes of a world .sav (GVAS?) | | |
| V3 | Process names in Task Manager > Details while in-game | | |
| V4 | Steam Cloud: contents of Steam\userdata\<id>\1374490\remote | | |
| V5 | Can a second Steam account host a copied world? | | |
| V6 | Does Steam show a cloud-conflict dialog after SaveHarbor restores a world? | | |
| V7 | Does the game rely on file modified time for the world list? | | |
| V8 | Where is the game build id (appmanifest_1374490.acf)? | | |

## Assumptions in force until the results exist

Each assumption lives in exactly one place in the code so that it can change easily.

| ID | Assumption | Code location |
|----|------------|---------------|
| A1 | Worlds are top-level files with the extension exactly `.sav` in `SaveGames`, with no subfolders. | `DragonwildsSaveAdapter` (`WorldExtension`, `DiscoverWorldsAsync`) |
| A2 | Game backup files contain `.bak` or `.backup` in the name and are ignored. | `DragonwildsSaveAdapter` (`BackupMarkers`) |
| A3 | No `GVAS` header validation is performed. It is added only after V2 confirms the header. | not implemented |
| A4 | Process names are `RSDragonwilds-Win64-Shipping`, `RSDragonwilds` and `RSDragonwildsServer`. | `DragonwildsGameDefinition.ProcessNames` |

## Stop condition

If V5 fails (a copied world cannot be hosted by another account), the rotating-host model does not work. Stop and report; Play-flow work for Dragonwilds (T05) must not continue.

## Release gate

Per-game Dragonwilds behaviour must not be released to friends until V1, V3 and V5 are filled in.
