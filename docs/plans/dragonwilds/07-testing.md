# Plan 07 — Automated Test Project and Verification Gate

**Branch:** `feature/dragonwilds-support` (it can also be started as `chore/test-project` before plan 02, so that the refactor runs under tests)
**Depends on:** 01. **Recommended start:** right after 01, before 02.
**Skills:** `build-and-verify`, `coding-standards`.

## 1. Objective

Introduce the repository's first automated tests, so that the game abstraction refactor, migration and lock logic can be verified rather than only tested by hand. AGENT.md §7–8 require verification. The npm/Stryker gate it mentions does not apply, so this plan defines the .NET equivalent.

## 2. Project

- `SaveHarbor.Tests/SaveHarbor.Tests.csproj`: `net10.0-windows`, `OutputType Exe` (xunit v3), `UseWPF` **true** so the test host loads the Windows Desktop runtime the referenced app assembly needs (tests still target services, not views), `ProjectReference` to `SaveHarbor.App`.
- Packages (latest stable at implementation time; check with `dotnet list package --outdated`): `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`. **No mocking library.** Hand-written fakes keep dependencies small (AGENT.md §6).
- Added to `SaveHarbor.sln`.
- `SaveHarbor.App` gets `<InternalsVisibleTo Include="SaveHarbor.Tests" />` only if an internal type needs it.

## 3. Test infrastructure

| Helper | Purpose |
|---|---|
| `TempDirectory` (`IDisposable`) | Unique folder under `Path.GetTempPath()/SaveHarbor.Tests`, deleted on dispose |
| `SaveFixtures` | Builders that create fake save trees: Windrose RocksDB world (`WorldDescription.json` with `islandId: "TEST_WORLD_ID"`, dummy `.sst`, `CURRENT`, `MANIFEST-000001`) and Dragonwilds layout (dummy `.sav` files per plan 03) |
| `FakeCloudProvider : ICloudProvider` | In-memory manifests, versions and locks; hooks to simulate concurrent lock writes, failures and duplicate lock files |
| `FakeClock` | Only if needed for lock-expiry tests. Prefer passing `DateTimeOffset now` into pure policy functions instead of adding a clock abstraction |
| `TestPathProvider : IAppDataPathProvider` | Roots everything in a `TempDirectory` |

Discovery adapters currently read `Environment.SpecialFolder.LocalApplicationData` directly. Plan 02 must route the save root through the game definition or options (constructor parameter), so that tests can point at a fixture. This is a **required testability seam**.

## 4. Test inventory (minimum)

| Area | Tests |
|---|---|
| Windrose adapter | discovers worlds; prefers `RocksDB_v2`; falls back to the folder name when `islandId` is empty; ignores folders without `WorldDescription.json`; payload excludes `LOCK` |
| Dragonwilds adapter | per plan 03 §7 |
| Sync state resolution | each `CloudSyncState` branch from §"State resolution" of the `cloud-sync-and-locking` skill, with a fake provider |
| Session lock | own vs foreign; expired foreign; verify-after-write winner; duplicate lock files resolve to the same winner from both sides; heartbeat extends expiry; take-over detection |
| Play flow | each `PlayPreparationResult.Outcome`; finish flow retries then sets `PendingUpload` |
| Scoping and migration | legacy layout migrates once; second run no-op; nothing is deleted on failure; folder marker refusal for the wrong game; manifest `Game` mismatch rejected |
| Path safety | `SafePath.CombineUnderRoot` rejects `..`, separators, rooted paths and reserved names |
| Options | `Games` section is parsed; legacy `GameLauncher` fallback; `--game` argument |

Naming: `MethodOrScenario_Condition_ExpectedResult`. One assertion focus per test.

## 5. Gate

```powershell
dotnet build SaveHarbor.sln -c Debug
dotnet test SaveHarbor.sln --no-build
```
A plan is complete only when both pass and the manual smoke test in `build-and-verify` has been performed for each game the plan touches.

## 6. Acceptance criteria

- [ ] `dotnet test` runs green locally in under 30 s.
- [ ] Tests do not touch the real `%LOCALAPPDATA%` (assert by running with a read-only check, or by review).
- [ ] No real IDs or player names in fixtures.
