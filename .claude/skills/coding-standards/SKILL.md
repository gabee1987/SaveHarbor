---
name: coding-standards
description: SaveHarbor C#/WPF code-quality rules distilled from AGENT.md and the existing codebase (naming, file size, partial classes, error handling, surgical edits). Use whenever writing or reviewing C# or XAML in this repo.
---

# Coding Standards (SaveHarbor)

`AGENT.md` at the repo root is the authoritative ruleset. This skill translates it into
concrete C#/WPF practice for this codebase. If they conflict, the stricter rule wins.

## Where project-specific rules live

These skills plus `docs/plans/` are the project-specific rules `AGENT.md` refers to.
Verification gate: the `build-and-verify` skill. (`AGENT.md` §10 mentions Husky and
`apply_patch`; neither exists here. Use the Edit tool, and ignore Husky.)

## Principles (from AGENT.md, applied)

- **Think first**: state assumptions; ask when ambiguous; push back on complexity.
- **Simplicity**: no speculative configurability, no single-use abstractions.
  Exception: the `IGameDefinition` abstraction is *requested* (two games), see `multi-game-separation`.
- **Surgical changes**: every changed line traces to the request. Do not reformat neighbours.
  Mention unrelated issues instead of fixing them.
- **Performance**: no extra polling/timers; reuse the existing 5 s `DispatcherTimer` game monitor.
  Avoid enumerating whole save trees on UI-thread hot paths.
- **Verification is part of the task** (see `build-and-verify`).

## File and type size

- Hard ceiling ~700 lines per file. Split earlier by responsibility.
- Existing pattern for large classes: **partial classes split by concern**, file named
  `TypeName.Concern.cs` (e.g. `MainWindowViewModel.CloudCommands.cs`,
  `CloudSyncService.Sessions.cs`). Continue this pattern; do not create new god files.
- `GoogleDriveCloudProvider.cs` (~690 lines) is at the limit. Any growth must first split it
  (e.g. `GoogleDriveCloudProvider.Folders.cs`, `.Json.cs`).

## C# conventions observed in the codebase

- File-scoped namespaces: `namespace SaveHarbor.App.<Folder>;`
- `Nullable` and `ImplicitUsings` enabled. No `!` suppressions without a proven non-null reason.
- `sealed` for all concrete classes unless inheritance is designed.
- Primary constructors are acceptable for small services (`WindroseGameLauncherService`);
  larger services use explicit constructors with `private readonly` fields.
- Field naming: the codebase mixes `_camelCase` (view model) and `camelCase` (services).
  **Match the file you are editing.** New files: `_camelCase` in ViewModels, `camelCase` in Infrastructure.
- Immutable value data → `sealed record` (`WindroseWorld`, `GameLaunchResult`).
  Serialized JSON documents → `sealed class` with `{ get; set; }` and a `SchemaVersion`.
- Collection expressions (`[]`) and `IReadOnlyList<T>` for return types.
- Every async API takes `CancellationToken cancellationToken = default` and passes it down.
- Results over exceptions for expected outcomes: return `CloudSyncResult`, `GameLaunchResult`
  (`IsSuccess` + user message). Throw only for unexpected failures; they surface via
  `IAppErrorHandler` and `RunBusyAsync`.
- Logging via `IAppLogger` with an `AppLogKeyword`; structured templates (`{WorldId}`), never
  string interpolation in templates. Add a keyword to `AppLogKeyword` and `appsettings.json` together.

## Layering

`Domain` (pure data) ← `Services` (interfaces) ← `Infrastructure` (implementations, IO) ←
`ViewModels` ← `Views`. ViewModels never touch `System.IO` directly except trivial path checks;
put IO in a service. Views contain no logic beyond visual behaviours.

## Security defaults

- Never log tokens, client secrets, or full Drive links. Player names in logs are acceptable
  (already the case) but nothing beyond `Environment.UserName`/`MachineName`.
- Keep `google-client-secret.enc` handling via `GoogleClientSecretsProtector`; never commit plaintext secrets.
- Validate every path derived from cloud data (world IDs, archive names) with
  `FileNameSanitizer` before combining with local paths (zip-slip / path traversal).
- Examples and test fixtures use placeholder IDs (`12345`, `TEST_WORLD_ID`), never real Steam IDs.

## UI text

All user-visible strings in XAML go through `{ui:UiText Key}` and `Resources/Texts/ui-text.en.json`.
Game-specific strings use a game prefix (see `multi-game-separation`). ViewModel toast/status strings
are currently inline; keep them consistent in tone and short (≤ 1 sentence title, ≤ 2 sentence body).
