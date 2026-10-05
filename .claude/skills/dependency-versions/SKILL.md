---
name: dependency-versions
description: Target framework and NuGet package versions for SaveHarbor, how to check for updates, and upgrade rules. Use when changing the csproj, SDK, target framework, or any package reference.
---

# Dependency Versions

## Target (researched 2026-10-05)

| Item | Current | Target | Notes |
|---|---|---|---|
| .NET SDK | 8.0.425 | **10.0.401** (`global.json`, `rollForward: latestFeature`) | .NET 10 = LTS, support until Nov 2028 |
| TargetFramework | `net8.0-windows` | **`net10.0-windows`** | WPF ships with the Windows Desktop runtime |
| CommunityToolkit.Mvvm | 8.4.2 | 8.4.2 | already latest |
| Google.Apis.Auth | 1.74.0 | 1.77.0 | keep in lockstep with Drive.v3 |
| Google.Apis.Drive.v3 | 1.74.0.4135 | 1.77.0.4276 | |
| Microsoft.Extensions.Configuration(.Json) | 10.0.8 | 10.0.12 | match runtime patch |
| Microsoft.Extensions.DependencyInjection | 10.0.8 | 10.0.12 | |
| Microsoft.Extensions.Hosting | 10.0.8 | 10.0.12 | |
| Serilog | 4.3.1 | 4.4.0 | |
| Serilog.Sinks.File | 7.0.0 | 7.0.0 | already latest |

**.NET 11** is at RC1 (go-live, STS, GA expected Nov 2026). Do **not** target it until GA, and
even then prefer the LTS (10) for a small app shared with friends — fewer forced runtime installs.

## How to check

```powershell
dotnet --list-sdks
dotnet list SaveHarbor.App/SaveHarbor.App.csproj package --outdated
dotnet list SaveHarbor.App/SaveHarbor.App.csproj package --vulnerable --include-transitive
```
Release index: `https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json`.

## Rules

- Upgrades go on their own `chore/` branch (or a dedicated step of a feature plan), never mixed
  with behaviour changes, so regressions are bisectable.
- Keep `Microsoft.Extensions.*` on the same version as each other.
- Keep `Google.Apis.*` on the same minor version as each other.
- No new packages for small logic (AGENT.md §6). New packages require a stated reason.
- After upgrade: clean build with zero new warnings, `--vulnerable` clean, manual smoke test
  (see `build-and-verify`).
- The SDK install itself is a machine change — ask the owner before installing anything.
- Publish settings (self-contained vs framework-dependent) affect friends' installs; currently
  self-contained publish output is in `artifacts/publish/`. Keep self-contained so friends need
  no runtime install.
