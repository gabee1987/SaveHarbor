---
name: dragonwilds-process-and-launch
description: RuneScape Dragonwilds Steam app id, launch URI, executable/process names for running-detection, settle timing, and storefront support limits. Use when implementing the Dragonwilds game definition, launcher, or process detection.
---

# Dragonwilds Process and Launch

| Item | Value | Confidence |
|---|---|---|
| Steam app id | **1374490** | High (Steam community URLs) |
| Launch URI | `steam://rungameid/1374490` | High (derived; test once) |
| Install dir | `steamapps\common\RSDragonwilds\` | Medium-high |
| Game process | `RSDragonwilds-Win64-Shipping` (in `Binaries\Win64\`) | Medium-high |
| Bootstrap exe | `RSDragonwilds` (root stub, UE convention) | Medium — **[verify]** |
| Dedicated server | `RSDragonwildsServer` (app id 4019830) | Medium |

## Game definition values

```csharp
StorageKey      = "dragonwilds";
DisplayName     = "RuneScape: Dragonwilds";
LaunchUri       = "steam://rungameid/1374490";   // overridable via SaveHarbor:Games:Dragonwilds:LaunchUri
ProcessNames    = ["RSDragonwilds-Win64-Shipping", "RSDragonwilds", "RSDragonwildsServer"];
ProcessMatch    = Exact;                          // Process.GetProcessesByName, case-insensitive
SaveSettleDelay = TimeSpan.FromSeconds(15);       // autosave cadence unknown; err on safe side
```

Including `RSDragonwildsServer` means a locally running dedicated server also blocks
backup/restore — correct, because it writes the same kind of files (but in its own folder).
Keep it unless verification shows it never shares the client save folder; then drop it.

## Launch behaviour

- Steam may show a launch-options dialog or updates before the process appears → the
  `GameStartTimeout` (3 min) from plan 05 applies; update downloads can exceed it, so on
  timeout show "Dragonwilds did not start. If Steam is updating the game, press Play again when ready."
  and release the lock.
- Epic Games / Jagex Launcher / PC Game Pass: no known launch URI or save path → unsupported in v1.
  `ExecutablePath` override exists for advanced users; document as "Steam supported".

## Verify on a real install

1. Task Manager → Details while in-game: exact process names (bootstrap + shipping).
2. Does the bootstrap exit after spawning the shipping exe? (affects "observed running" logic — any name counts).
3. Time from Steam launch to process appearance.
