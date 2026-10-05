---
name: build-and-verify
description: Commands and checklists to build, test, publish, and manually verify SaveHarbor (replaces AGENT.md's npm/Stryker gate, which does not apply to this .NET repo). Use before declaring any code change or plan phase complete.
---

# Build and Verify

## Automated gate (run from repo root)

```powershell
dotnet restore SaveHarbor.sln
dotnet build SaveHarbor.sln -c Debug --no-restore
dotnet test SaveHarbor.sln --no-build   # once SaveHarbor.Tests exists (plan 07)
```

A phase is complete only when the build passes with no new warnings (compare the warning
count against the pre-change build) and all tests pass.
If something can't be run (e.g. SDK not installed), say exactly what and why.

## Test project (planned, `docs/plans/dragonwilds/07-testing.md`)

`SaveHarbor.Tests` (xUnit, `net10.0-windows`). Priorities: game definitions & discovery
against temp-dir fixtures, sync state resolution, lock acquire/verify logic with a fake
`ICloudProvider`, path sanitising. Use placeholder IDs (`12345`, `TEST_WORLD_ID`).

## Manual smoke test (LocalTest provider)

Set `SaveHarbor:CloudProvider:Provider` to `LocalTest` in the **output** `appsettings.json`
(never commit that change), then for each game:
1. App starts, correct theme, worlds listed.
2. Upload v1 → `%LOCALAPPDATA%\SaveHarbor\cloud-test` contains manifest + version.
3. Simulate friend: edit `locks/active-session.json` `MachineName` to `TEST_MACHINE` → Play is blocked with name shown.
4. Remove lock, bump manifest → Play auto-downloads, creates backup, launches.
5. Close game → upload happens, lock cleared.
6. Switch game → other game's worlds/theme/cloud status; no cross-contamination of files.

## Publish

```powershell
dotnet publish SaveHarbor.App -c Release -r win-x64 --self-contained true -o artifacts/publish/SaveHarbor
```
`artifacts/` is git-ignored. Never publish with a plaintext client secret.

## Running the app

`dotnet run --project SaveHarbor.App` (Windows only). The game itself is not required for
discovery/backup tests if a fixture save folder exists.
