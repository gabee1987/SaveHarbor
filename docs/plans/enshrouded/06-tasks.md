# 06 — Tasks and Implementation Handoff

**Audience:** the implementing agent. **Owner:** Gabor. **Branch:** `feature/enshrouded-support`.
The rules of engagement, conventions and definition of done from the Dragonwilds handoff ([`../dragonwilds/09-implementation-handoff.md`](../dragonwilds/09-implementation-handoff.md) §1, §3, §5) apply unchanged. Where this file and plans 01–05 differ, this file wins.

Fixed decisions (owner may override): ~~Local folder is the only write target~~ (replaced 2026-10-28: SaveHarbor manages the signed-in account's Steam Cloud folder, see FINDINGS.md); GUID is the world id; slot reuse by GUID; ~~no Steam `remote` writes~~ (replaced, same entry); Cinzel reused as display font; no new NuGet packages.

Placeholder data only: `12345` for a Steam id folder, `TEST_WORLD_ID`, GUIDs like `00000000000000000000000000000001`.

## Order and checkpoints

```
CP0  T0  Backend row + web list                       → owner commits, pushes migration
CP1  T1  Game definition, registration, leak fixes    → owner commits (switch shows Enshrouded; discovery read-only)
CP2  T2  Header, roots, slot map, adapter, tests      → owner commits
CP3  T3  Restore/import path for slot renaming        → owner commits (end-to-end on LocalTest provider)
CP4  T4  Theme and shell view                         → owner commits
CP5  T5  Texts, hints, health notice, manual checks   → owner commits
```

At each checkpoint stop and report: files changed, commands run with results, what could not be verified, risks.

---

## T0 — Backend and web (CP0) · plan 03

### T0.1 `supabase/migrations/20261028000000_enshrouded_game.sql`
The one-line insert from plan 03 §1.1 with its comment.

### T0.2 `supabase/tests/games.sql`
Update the initial-list assertion; add the two Enshrouded assertions; bump `plan(n)`. Run `supabase test db` (if the local stack is not running, **stop and ask**; do not start Docker yourself).

### T0.3 `web/src/lib/games.ts`, `web/public/about.html`, `web/src/styles.css`, tests
As in plan 03 §2. Styles use the palette from the `enshrouded-theme` skill. `npm test` and `npm run build` in `web/`.

**Done when:** pgTAP passes, web tests pass, `git diff --stat` touches only the listed files.

### CHECKPOINT CP0 → stop, report. The owner pushes the migration before CP3's end-to-end test.

---

## T1 — Game definition and registration (CP1) · plan 02 §2

### T1.1 `App/Domain/GameId.cs`, `GameIdExtensions.cs`
Add `Enshrouded`; `ToStorageKey` → `"enshrouded"`.

### T1.2 Leak fix: default launch URI
Add `string DefaultLaunchUri` is **not** added to `IGameDefinition` (definitions need options first). Instead create `App/Infrastructure/Games/GameDefaults.cs`:
```csharp
internal static class GameDefaults
{
    public static string LaunchUri(GameId game) => game switch
    {
        GameId.Windrose => "steam://rungameid/3041230",
        GameId.Dragonwilds => "steam://rungameid/1374490",
        GameId.Enshrouded => "steam://rungameid/1203620",
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null)
    };
}
```
`AppOptionsLoader.DefaultLaunchUri` delegates to it. This keeps the one table of per-game constants next to the game code and makes the switch exhaustive (the `_` arm throws, so a missing game fails fast in tests).

### T1.3 Leak fix: shell template selection
`App/Views/Shell/GameShellTemplateSelector.cs` (`DataTemplateSelector`): looks up a `DataTemplate` resource named `$"{ActiveGameId}ShellTemplate"` on the `MainWindow`. `MainWindow.xaml` keeps one `DataTemplate` per game (resources are per-game XAML, acceptable) but the `Style.Triggers` block goes away; `ContentControl.ContentTemplateSelector` is set. Add `EnshroudedShellTemplate` → `enshrouded:EnshroudedShellView` (created in T4.1; until then a placeholder `WindroseShellView` is **not** acceptable, so order T4.1 before T1.3 if you prefer, or create the shell view file in this task with the Dragonwilds one as a template and only the names changed).

### T1.4 `App/Infrastructure/Games/Enshrouded/EnshroudedGameDefinition.cs`
Values from plan 02 §2. `ProcessNames = ["enshrouded"]`.

### T1.5 `App/appsettings.json`
`SaveHarbor:Games:Enshrouded` with `LaunchUri`, `ExecutablePath`, `SaveRoot`.

### T1.6 `App/App.xaml.cs`
`services.AddSingleton<EnshroudedSaveAdapter>(); services.AddSingleton<IGameDefinition, EnshroudedGameDefinition>();` — the adapter is a stub in this task (`DiscoverWorldsAsync` returns `[]`, every other member mirrors the Dragonwilds no-ops) so the solution builds. T2 fills it.

### T1.7 Tests
`SaveHarbor.Tests/Games/GameRegistryTests.cs` (new or extend `ActiveGameContextTests`): three games; `ToStorageKey` round-trips for every enum value; `LoadGameOptions()` has every enum value with a non-empty launch URI. Update `Support/StubGameDefinition.cs` if the interface changed (it should not).

**Done when:** build and tests pass; the header switch shows three games; selecting Enshrouded shows an empty world list and the Windrose palette (theme comes in T4).

### CHECKPOINT CP1.

---

## T2 — Header, roots, slot map, adapter (CP2) · plan 02 §3–5

All files under `App/Infrastructure/Games/Enshrouded/`.

### T2.1 `EnshroudedSaveHeader.cs`
```csharp
internal sealed record EnshroudedSaveHeader(Guid WorldGuid, ushort FormatVersion, uint SaveCounter, DateTimeOffset SavedAtUtc)
{
    public const int Size = 48;
    public string WorldId => ...; // 32 lowercase hex chars of the 16 raw bytes, in file order (not Guid.ToString, whose byte order differs)
    public static EnshroudedSaveHeader? TryRead(string path);
}
```
`TryRead` opens the file with `FileShare.ReadWrite`, reads exactly 48 bytes, checks `SV2C`, version `2`, header size `48`; returns null on any mismatch or IO error. Store the raw 16 bytes for the id; expose `Guid` only for display.

### T2.2 `IEnshroudedSaveRootLocator.cs`, `EnshroudedSaveRootLocator.cs`
Returns `(string LocalRoot, IReadOnlyList<(string SteamUserFolder, string Root)> SteamRoots)`. Steam path from the registry (`Microsoft.Win32.Registry`, already available on Windows targets); userdata enumeration; a `SaveRoot` override from options replaces everything with one Local root. Registered as a singleton; tests substitute it.

### T2.3 `EnshroudedSlotMap.cs`
Pure static class: `ResolveTargetPath(string worldsPath, string worldId)` and `EnumerateWorldFiles(string worldsPath)`; both only use `Directory`/`File` and `EnshroudedSaveHeader.TryRead`. Name pattern `^world(\d+)\.ksd$` (`RegexOptions.IgnoreCase`); the slot number is parsed with `int.TryParse` and anything above 9999 is ignored.

### T2.4 `GameSaveRoot.CanReceiveWorlds`
Add `bool CanReceiveWorlds = true` as the last positional parameter with a default (`App/Domain/GameSaveRoot.cs`). Find every place that chooses a root for a download or import (search for `DiscoverSaveRootsAsync` callers) and filter to `CanReceiveWorlds`; when none is left, surface `AppErrorCode.NotConfigured` with the existing "no save folder" message.

### T2.5 `EnshroudedSaveAdapter.cs`
Replace the T1 stub with plan 02 §4–5. Target under 250 lines; split `EnshroudedWorldFacts.cs` / `EnshroudedWorldInspector.cs` as Dragonwilds does.

### T2.6 World name spike (time-box: 2 hours of agent effort, then stop)
Try to locate the world name inside `world<N>.ksd`: parse the `S2BH` block table (counts and offsets right after the header), decompress each zstd frame with a **test-only** reference (no NuGet in the app; if decoding needs a library, the spike is over) and search for a UTF-8/UTF-16 string that matches a known test world name. If the name is found at a stable, cheap-to-read location **without** zstd, implement `EnshroudedWorldNameReader`; otherwise record the findings in `FINDINGS.md` and leave `WorldName = "Enshrouded world <N>"`.

### T2.7 Tests
Everything in plan 05 §2 except the restore and import rows.

**Done when:** build and tests pass; on the owner's PC the Enshrouded view lists the Local and Steam Cloud worlds with slot, counter and location.

### CHECKPOINT CP2.

---

## T3 — Restore and import with slot renaming (CP3) · plan 02 §4

### T3.1 `IGameSaveAdapter.GetArchiveFileName(GameWorld world)`
New member; default implementations in Windrose and Dragonwilds return `Path.GetFileName(world.SavePath)`. Enshrouded returns `"world.ksd"`.

### T3.2 `FileSetPayloadStrategy`
- `Stage`: use `adapter.GetArchiveFileName` for `RelativePath` (the adapter is already a parameter).
- `Restore`: drop the "different name" refusal when `manifest.Files.Count == 1` and the adapter's archive name equals the entry name; the target is `target.SavePath` (already the resolved slot, because `ReadWorldAsync`/`GetExpectedWorldPath` resolved it). Keep the refusal for adapters whose archive name is the real name (Dragonwilds), so their behaviour is unchanged.
- `Import`: target = `adapter.GetExpectedWorldPath(root, manifest.WorldId)` (already so); the entry is copied under that name.
- `ValidateEntries`: the required extension check stays (`.ksd`).

### T3.3 `ValidatePayload`
Enshrouded: header check + GUID equality with `expectedWorldId`; make sure the download path calls it before `Restore` (it does for Windrose; confirm for `FileSet`, add the call if the strategy skips it).

### T3.4 Tests
The restore and import rows of plan 05 §2, plus a Dragonwilds regression test that a renamed archive is still refused.

### T3.5 End-to-end on the LocalTest provider
Two Windows users or two temp roots via `SaveRoot` override: share from A, Play from B (file lands in B's Local root slot 0), modify, share, Play from A (same slot reused). Record the run in the checkpoint report.

**Done when:** the end-to-end run succeeds and every other game's tests still pass.

### CHECKPOINT CP3.

---

## T4 — Theme and shell (CP4) · plan 04

### T4.1 `Views/Enshrouded/EnshroudedShellView.xaml(.cs)`
Copy of the Dragonwilds shell view with names changed.

### T4.2 `Resources/Themes/Enshrouded.Colors.xaml`
All keys from the skill.

### T4.3 `Resources/Themes/Enshrouded/*.xaml`
Theme entry point, Styles (filigree frame), Controls, Inputs, Effects (mist), Sync. Start from the Dragonwilds files; change only what the skill says differs. The corner ornament is a `ControlTemplate` for the panel style: a `Grid` with the content `Border` and four `Path`s aligned to the corners, `IsHitTestVisible="False"`, `Visibility="{DynamicResource ShowOrnamentGlyphs}"`.

### T4.4 `GameIconGeometry`
A small original flame-over-line mark.

### T4.5 Verification
Switch between all three games at runtime; before/after screenshots of Windrose and Dragonwilds; 150 % DPI; 1100×720.

**Done when:** acceptance criteria of plan 04 §7.

### CHECKPOINT CP4.

---

## T5 — Texts, hints and health (CP5) · plan 02 §3, §6; plan 04 §4

### T5.1 `ui-text.en.json`
`Enshrouded.JoinHint`, `Enshrouded.SaveRootsHint`, `Inspector.PrivacyNote.Enshrouded`; update `Settings.AmbientEffectsHint`.

### T5.2 Share-from-Steam notice
In the sharing flow, after a successful upload, if the world's root provider key is `"steam"` and the game is the active definition's `StorageKey == "enshrouded"` — **no**: do not compare keys. Add `string? PostShareHint` to `IGameDefinition` (null for the other games; Enshrouded returns the notice) and `GameSaveRoot.CanReceiveWorlds == false` as the trigger. Show as toast + activity line once per world (remember in sync state: `SharedFromReadOnlyRootNoticeShown`).

### T5.3 Health notice
`CheckHealth`: duplicate GUID across roots → one `SaveHealthNotice`.

### T5.4 Settings page
Show `Enshrouded.SaveRootsHint` in the Enshrouded skin only (bind to the active definition, as the existing per-game settings text does).

### T5.5 Manual checks
Plan 05 §5 items 1–3 and 5. Item 4 (two-player session) is the owner's.

**Done when:** acceptance criteria of plan 02 §7 are all checked.

### CHECKPOINT CP5 → final report; owner opens the PR to `develop`.
