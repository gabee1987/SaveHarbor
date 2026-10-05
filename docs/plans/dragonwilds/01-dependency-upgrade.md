# Plan 01 — Dependency and Framework Upgrade

**Branch:** `chore/dotnet10-upgrade` (from `feature/dragonwilds-support`), or done directly on the feature branch as an isolated first change set.
**Depends on:** nothing. **Blocks:** all other plans (they assume `net10.0-windows`).
**Skills:** `dependency-versions`, `build-and-verify`, `git-workflow`.

## 1. Objective

Move SaveHarbor from .NET 8 to the latest LTS (.NET 10) and bring every NuGet package to its latest stable version, with no behaviour change. This applies to the whole app, so Windrose and Dragonwilds both use the same upgraded base.

## 2. Version targets (verified 2026-10-05)

| Item | From | To |
|---|---|---|
| SDK | 8.0.425 | 10.0.401 |
| TFM | `net8.0-windows` | `net10.0-windows` |
| Google.Apis.Auth | 1.74.0 | 1.77.0 |
| Google.Apis.Drive.v3 | 1.74.0.4135 | 1.77.0.4276 |
| Microsoft.Extensions.Configuration | 10.0.8 | 10.0.12 |
| Microsoft.Extensions.Configuration.Json | 10.0.8 | 10.0.12 |
| Microsoft.Extensions.DependencyInjection | 10.0.8 | 10.0.12 |
| Microsoft.Extensions.Hosting | 10.0.8 | 10.0.12 |
| Serilog | 4.3.1 | 4.4.0 |
| CommunityToolkit.Mvvm | 8.4.2 | unchanged (latest) |
| Serilog.Sinks.File | 7.0.0 | unchanged (latest) |

.NET 11 (RC1, STS) is deliberately excluded until GA, and LTS remains preferred afterwards.

## 3. Prerequisites (owner action)

1. Install the .NET 10 SDK (10.0.401 or later feature band) on the development machine:
   `winget install Microsoft.DotNet.SDK.10`. This is a machine change. The agent must not perform it unasked.
2. Visual Studio 2026 / VS Code C# Dev Kit with .NET 10 support (only if the IDE is used for builds).

## 4. Steps

### 4.1 Baseline
- `dotnet build SaveHarbor.sln -c Debug` on .NET 8. Record the warning count and list in the PR notes.
- Launch the app once against `LocalTest` to confirm the baseline smoke flow works (see `build-and-verify`).

### 4.2 Pin the SDK
Create `global.json` at the repo root:
```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestFeature"
  }
}
```

### 4.3 Project file (`SaveHarbor.App/SaveHarbor.App.csproj`)
- `<TargetFramework>net10.0-windows</TargetFramework>`.
- Update the `PackageReference` versions per §2.
- Leave `<Version>1.0.2</Version>` unchanged here. The version bump to `1.1.0` belongs to the release that ships Dragonwilds.
- No other property changes in this step (no `LangVersion`, no analyzers). Keep the diff minimal.

### 4.4 Bootstrap script
`bootstrap-saveharbor.ps1` contains `-f net8.0`. It is a historical scaffold script. Either update it to `net10.0` or leave it and mention it. **Recommendation:** update the single argument so the script stays truthful.

### 4.5 Build and fix
- `dotnet restore` → `dotnet build`. Expected breakages are low risk:
  - New analyzer warnings in .NET 10 (for example CA/IDE rules enabled by default). Fix only those caused by the upgrade, and only where trivial. Otherwise list them.
  - WPF API obsoletions: none are expected for the APIs used (standard controls, `DispatcherTimer`, `ResourceDictionary`).
  - Google.Apis 1.77: check the release notes for breaking changes in `GoogleWebAuthorizationBroker` and `FileDataStore`. Both are used in `GoogleDriveCloudProvider` (`CreateInteractiveCredentialAsync` and `CreateSilentCredentialAsync`).
- `dotnet list package --vulnerable --include-transitive` must be clean.

### 4.6 Publish verification
`dotnet publish SaveHarbor.App -c Release -r win-x64 --self-contained true -o artifacts/publish/SaveHarbor`, then run the published exe. The self-contained output now carries the .NET 10 runtime, so friends need no runtime install.

## 5. Acceptance criteria

- [ ] Builds on SDK 10.0.401 with no new warnings compared with the baseline (or each new warning documented).
- [ ] `--outdated` shows no updates and `--vulnerable` shows no findings.
- [ ] Manual smoke test passes: start, list worlds, backup, LocalTest upload and download, Play launches Steam, game-close auto-end.
- [ ] Google Drive connect (silent token reuse) still works with the existing token store. The token format must remain compatible. Otherwise users are prompted to sign in again; that is acceptable but must be noted.
- [ ] Published self-contained build starts on a machine without .NET installed.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Google auth library changes token store format | Test with the existing `%LOCALAPPDATA%\SaveHarbor\google-drive-token`; document a one-time re-login if needed |
| Friends run the old .NET 8 build against the same cloud | No cloud format change in this plan → fully compatible |
| Larger publish size | Accepted; optionally evaluate `PublishTrimmed` later (WPF trimming is unsupported, so do not enable it) |
