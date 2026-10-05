# Dragonwilds Support — Overview and Roadmap

**Branch:** `feature/dragonwilds-support` (from `develop`). The owner performs all commits and pull requests manually (see the `git-workflow` skill).
**Status:** planning only. No production code has been changed yet.
**Date:** 2026-10-05.

## 1. Goal

Extend SaveHarbor from a Windrose-only world-sharing helper into a two-game helper (Windrose and RuneScape: Dragonwilds), with:
- strict separation of the two games (worlds, backups, cloud folders, locks, theme);
- a game switch in the UI, remembered between runs;
- a fantasy-themed but clean and minimal look for Dragonwilds;
- a dependable one-click **Play** flow (pull newest, lock, launch, share on exit) for both games;
- all dependencies on their latest stable versions (.NET 10 LTS).

## 2. Current state (analysis summary)

- WPF, .NET 8, CommunityToolkit.Mvvm, Generic Host DI, Serilog, Google Drive v3. About 6.6k lines in one project, no tests.
- The architecture is clean overall: interfaces in `Services/`, IO in `Infrastructure/`, a partial-class view model, a localised text catalog.
- Windrose is hardcoded across discovery, launch, process detection, backup and cloud manifests, the view model, and UI texts.
- Play-flow gaps: no auto-download before play, **no auto-upload after play**, a racy lock, no heartbeat, no start timeout (details in plan 05).
- Security: a path-traversal risk from untrusted `WorldId`/`ArchiveFileName` values in shared cloud JSON (plan 04 §7).

## 3. Plans and order

| # | Plan | Size | Depends on |
|---|---|---|---|
| 01 | [Dependency upgrade](01-dependency-upgrade.md) — .NET 10, packages | S | — |
| 07 | [Test project](07-testing.md) — xUnit, fakes, fixtures | M | 01 |
| 02 | [Game abstraction](02-game-abstraction.md) — `IGameDefinition`, adapters, active game | L | 01 (07 recommended) |
| 04 | [Data and cloud scoping](04-data-and-cloud-scoping.md) — per-game folders, marker, migration, path safety | M | 02 |
| 03 | [Dragonwilds integration](03-dragonwilds-save-integration.md) — Phase 0 verification, adapter, single-file payloads | M | 02, 04 |
| 05 | [Play flow and locks](05-play-flow-ux.md) — one-click Play, heartbeat, auto-upload | L | 02, 04 |
| 06 | [Theming](06-fantasy-theme.md) — theme split, DynamicResource, Dragonwilds theme, switch UI | M | 02 |
| 08 | [Improvement recommendations](08-improvement-recommendations.md) — reliability, UX, consistency, distribution | — | independent |
| 09–12 | **Implementation handoff for Sonnet**: [09 rules and order](09-implementation-handoff.md), [10 foundation tasks](10-tasks-foundation.md), [11 scoping + Dragonwilds tasks](11-tasks-scoping-and-dragonwilds.md), [12 theme + play-flow tasks](12-tasks-play-flow-and-theme.md). Task-level signatures and code; where they differ from plans 01–07, they win | — | — |

Recommended execution: **01 → 07 → 02 → 04 → (03 ∥ 06) → 05**. Plan 03 Phase 0 (manual checks on a real Dragonwilds install) can be done at any time and should be done **first**, because its stop condition decides whether the rotating-host model is viable for Dragonwilds.

Optional early fixes on separate `fix/` branches that benefit Windrose immediately: path-traversal hardening (04 §7), lock verification and heartbeat (05 §3.3–3.4), and auto-upload on exit (05 §3.6).

## 4. Key decisions (recommendations; owner to confirm)

| Decision | Recommendation | Alternative |
|---|---|---|
| One app or two | One exe with a game switch, plus `--game` argument for per-game desktop shortcuts | Two published exes from one codebase |
| Cloud separation | A separate Google Drive shared folder per game, guarded by a marker file | One folder with `games/<key>/` subfolders |
| Windrose cloud layout | Unchanged (no cloud migration) | — |
| Dragonwilds platforms | Steam only in v1 (`SaveRoot` override for others) | Research Game Pass / Epic paths |
| Display font | Cinzel (OFL), embedded | System Georgia only |
| Auto-upload on exit | Yes, with retry and a persistent "not shared yet" banner | Ask each time |
| Dedicated-server export | Out of scope | Future plan |

## 5. Skills created (`.claude/skills/`)

| Skill | Purpose |
|---|---|
| `git-workflow` | Branch prefixes; never commit, push or create PRs |
| `coding-standards` | AGENT.md applied to C#/WPF, plus known mismatches |
| `architecture-overview` | Map of the codebase and data locations |
| `wpf-mvvm-conventions` | Commands, busy handling, feedback, resources |
| `build-and-verify` | Build, test, smoke-test and publish gate |
| `dependency-versions` | Version targets and upgrade rules |
| `cloud-sync-and-locking` | Version, manifest and lock contracts; weaknesses |
| `play-session-flow` | Target one-click Play UX and edge cases |
| `multi-game-separation` | Game abstraction and isolation invariants |
| `windrose-save-format` | Windrose RocksDB saves |
| `dragonwilds-save-format` | Dragonwilds `.sav` worlds versus character JSON |
| `dragonwilds-world-sharing` | Rotating host, portability, dedicated-server alternative |
| `dragonwilds-steam-cloud` | Coexisting with Steam Cloud |
| `dragonwilds-process-and-launch` | App id, process names, launch |
| `dragonwilds-fantasy-theme` | Palette, typography, ornaments, theme mechanics |

## 6. Compliance and external services

- Google Drive (existing) is an external cloud service. It is fine for a private hobby group, but any wider or organisational use requires a compliance review (GDPR: player and machine names are stored in shared JSON).
- The Cinzel font is a third-party asset (SIL OFL 1.1); include its licence file.
- No new external services or APIs are introduced by these plans.

## 7. Research sources (Dragonwilds)

Community and hosting-provider sources, collected 2026-10-05. No primary Jagex technical documentation was found, so details marked **[verify]** in the skills must be confirmed locally (plan 03 Phase 0).
- steamcommunity.com/app/1374490 discussions (save location, Steam Cloud, transfers)
- dragonwilds.runescape.wiki — Update: Eye on Ashenfall 0.7.3 (Steam Cloud saves)
- gameserverkings.com — worlds, saves and backups; 1.0 changes for server owners
- xgamingserver.com — world management; update 0.11 dedicated servers
- survivalservers.com — transfer local save to hosted server
- gamever.io — where Dragonwilds keeps your save
