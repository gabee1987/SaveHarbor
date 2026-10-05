# 10 — Tasks: Foundation (Plans 01, 07, 02)

Read [09-implementation-handoff.md](09-implementation-handoff.md) first. Paths are relative to the repo root; `App/` means `SaveHarbor.App/`.

---

## T01 — Dependency upgrade (CP0)  · plan 01

**Pre-check (stop if it fails):** `dotnet --list-sdks` must list a `10.0.x` SDK. If only 8.x is installed, **stop and tell the owner** to install it (`winget install Microsoft.DotNet.SDK.10`). Do not install it yourself.

### T01.1 Baseline
1. `dotnet build SaveHarbor.sln -c Debug 2>&1 | tail -5` on the *current* state (before `global.json` exists; the 10 SDK can still build `net8.0-windows`). If the baseline cannot build, record "baseline not buildable: <reason>" and continue.
2. Record the warning count in `docs/plans/dragonwilds/FINDINGS.md` under a heading `## Build baseline` (create the file). Format: `Warnings: <n> (SDK <version>, TFM net8.0-windows)`.

### T01.2 `global.json` (new, repo root)
```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestFeature"
  }
}
```
If the installed 10.x SDK is older than 10.0.401, use the installed version number and say so in the report.

### T01.3 `App/SaveHarbor.App.csproj`
- `<TargetFramework>net10.0-windows</TargetFramework>`.
- Package versions (exactly):

| Package | Version |
|---|---|
| CommunityToolkit.Mvvm | 8.4.2 (unchanged) |
| Google.Apis.Auth | 1.77.0 |
| Google.Apis.Drive.v3 | 1.77.0.4276 |
| Microsoft.Extensions.Configuration | 10.0.12 |
| Microsoft.Extensions.Configuration.Json | 10.0.12 |
| Microsoft.Extensions.DependencyInjection | 10.0.12 |
| Microsoft.Extensions.Hosting | 10.0.12 |
| Serilog | 4.4.0 |
| Serilog.Sinks.File | 7.0.0 (unchanged) |

- Do not change anything else in the csproj (`<Version>`, content items stay).

### T01.4 `bootstrap-saveharbor.ps1`
Change the single occurrence `-f net8.0` → `-f net10.0`. Nothing else.

### T01.5 Build and fix
`dotnet restore SaveHarbor.sln` → `dotnet build SaveHarbor.sln -c Debug`. Fix only errors/warnings *caused by the upgrade* and only minimally. If Google.Apis 1.77 changed an API used in `GoogleDriveCloudProvider` (`GoogleWebAuthorizationBroker`, `FileDataStore`, `UserCredential`), adapt the call sites minimally and list them in the report.
Then: `dotnet list App/SaveHarbor.App.csproj package --outdated` (expect none) and `dotnet list App/SaveHarbor.App.csproj package --vulnerable --include-transitive` (expect none).

### T01.6 Publish check
`dotnet publish App/SaveHarbor.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish-check/SaveHarbor` (git-ignored via `artifacts/`). Confirm `SaveHarbor.App.exe` exists. **Do not delete** the folder (AGENT.md §10 forbids recursive deletion without an explicit request); mention it in the report so the owner can remove it.

**Done when:** build succeeds, outdated/vulnerable lists empty, publish produced the exe. Report the new warning count vs baseline.

### CHECKPOINT CP0 → stop, report.

---

## T07a — Test project skeleton (CP1) · plan 07

### T07a.1 Project
Create `SaveHarbor.Tests/SaveHarbor.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <OutputType>Exe</OutputType>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="LATEST" />
    <PackageReference Include="xunit.v3" Version="LATEST" />
    <PackageReference Include="xunit.runner.visualstudio" Version="LATEST" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\SaveHarbor.App\SaveHarbor.App.csproj" />
  </ItemGroup>
</Project>
```
Replace `LATEST` with the current stable versions: run `dotnet package search xunit.v3 --take 1` (or `dotnet add package <name>` which resolves latest) rather than guessing.
`OutputType Exe` is required by xunit v3 (test projects are executables). `UseWPF` is set so the test host loads the Windows Desktop runtime that the referenced WPF app assembly needs; the test project contains no XAML and tests never create windows. Add it to the solution: `dotnet sln SaveHarbor.sln add SaveHarbor.Tests/SaveHarbor.Tests.csproj`.

If referencing the WPF `WinExe` project fails to build or run (duplicate entry point, XAML compilation, `Application` already running), **stop and ask the owner** — the fallback (extract a `SaveHarbor.Core` class library) is a larger decision.

### T07a.2 Helpers (`SaveHarbor.Tests/Support/`)
`TempDirectory.cs`:
```csharp
namespace SaveHarbor.Tests.Support;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SaveHarbor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
```
`TestPathProvider.cs` — implements `IAppDataPathProvider` with every property rooted under a `TempDirectory`. (Update it whenever the interface gains members in T02/T04; the compiler will tell you.)

`NullAppLogger.cs` — implements `IAppLogger` with empty methods.

`FakeCloudProvider.cs` — added in T04/T05 when first needed (do not create it now).

### T07a.3 Smoke test
`SaveHarbor.Tests/Support/TempDirectoryTests.cs`: one test creating and disposing a `TempDirectory` and asserting the folder is gone. Purpose: prove the test pipeline works.

**Done when:** `dotnet test SaveHarbor.sln` runs 1 test, passes. Tests never touch the real `%LOCALAPPDATA%`.

### CHECKPOINT CP1 → stop, report.

---

## T02 — Game abstraction (CP2) · plan 02

Goal: Windrose-only behaviour, zero functional change, every Windrose literal behind the abstraction. Work in the sub-task order; each ends with a green build.

### T02.1 Domain types (`App/Domain/`)

`GameId.cs`
```csharp
namespace SaveHarbor.App.Domain;

public enum GameId
{
    Windrose,
    Dragonwilds
}
```
`GameIdExtensions.cs`
```csharp
namespace SaveHarbor.App.Domain;

public static class GameIdExtensions
{
    public static string ToStorageKey(this GameId game) => game switch
    {
        GameId.Windrose => "windrose",
        GameId.Dragonwilds => "dragonwilds",
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null)
    };

    public static bool TryParseStorageKey(string? value, out GameId game)
    {
        foreach (var candidate in Enum.GetValues<GameId>())
        {
            if (string.Equals(candidate.ToStorageKey(), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                game = candidate;
                return true;
            }
        }

        game = default;
        return false;
    }
}
```
`WorldPayloadKind.cs`: `public enum WorldPayloadKind { Directory, FileSet }`

`GameWorld.cs`
```csharp
namespace SaveHarbor.App.Domain;

public sealed record GameWorld(
    GameId Game,
    string WorldId,
    string WorldName,
    string Subtitle,
    string SavePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    long SizeBytes,
    int FileCount);
```
`GameSaveRoot.cs`
```csharp
namespace SaveHarbor.App.Domain;

public sealed record GameSaveRoot(
    GameId Game,
    string RootId,
    string RootPath,
    string WorldsPath,
    string Detail,
    DateTimeOffset LastModifiedAt);
```
Delete `Domain/WindroseWorld.cs` and `Domain/WindroseProfile.cs`.

**Rename pass (compile-driven).** Replace `WindroseWorld` → `GameWorld` and `WindroseProfile` → `GameSaveRoot` in exactly these files (found with `rg -l "WindroseWorld|WindroseProfile" App`):
`Domain/CloudDownloadRequest.cs`, `Domain/CloudUploadRequest.cs`, `Domain/LocalSyncState.cs`, `Infrastructure/CloudSyncService.cs`, `.Sessions.cs`, `.Transfers.cs`, `Infrastructure/LocalJsonSyncStateService.cs`, `Infrastructure/ZipBackupService.cs`, `Services/IBackupService.cs`, `Services/ICloudSyncService.cs`, `Services/ILocalSyncStateService.cs`, all `ViewModels/MainWindowViewModel*.cs` that matched, and `Resources/Styles/DarkTheme.xaml` line ~327 (`DataType="{x:Type domain:WindroseWorld}"` → `domain:GameWorld`).
Field mapping for `GameSaveRoot`: `profile.ProfileId` → `root.RootId`; `profile.WorldsPath` unchanged; `profile.RocksDbVersion` → use `root.Detail` only for display (`RefreshProfileStatusAsync`).
`CloudSyncService.Transfers.cs` line ~114 constructs a `WindroseWorld` — becomes (same argument values as today, plus the game):
```csharp
var world = new GameWorld(
    profile.Game,
    manifest.WorldId,
    string.IsNullOrWhiteSpace(manifest.WorldName) ? manifest.WorldId : manifest.WorldName,
    "Unknown",
    targetWorldPath,
    DateTimeOffset.MinValue,
    DateTimeOffset.MinValue,
    0,
    0);
```
Line ~157 message `profile.ProfileId` → `profile.RootId`.
`XAML`: `Views/Controls/WorldSectionView.xaml` lines 63 and 67: `SelectedWorld.WorldPresetType` → `SelectedWorld.Subtitle`.

### T02.2 Game options and the options loader

`App/Infrastructure/Games/GameOptions.cs`
```csharp
namespace SaveHarbor.App.Infrastructure.Games;

public sealed class GameOptions
{
    public string LaunchUri { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string SaveRoot { get; init; } = string.Empty;
}

public sealed class GameOptionsProvider(IReadOnlyDictionary<GameId, GameOptions> optionsByGame)
{
    public GameOptions Get(GameId game) =>
        optionsByGame.TryGetValue(game, out var options) ? options : new GameOptions();
}
```
(add `using SaveHarbor.App.Domain;`)

`App/Infrastructure/AppOptionsLoader.cs` (static): **move** from `App.xaml.cs` — verbatim logic, no behaviour change — `LoadLoggingOptions`, `LoadCloudProviderOptions`, and the private `ReadLevel/ReadBool/ReadInt`, making them `public static`/`internal static`; and replace `LoadGameLauncherOptions` with:
```csharp
public static GameOptionsProvider LoadGameOptions()
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .Build();

    var byGame = new Dictionary<GameId, GameOptions>();
    foreach (var game in Enum.GetValues<GameId>())
    {
        var section = configuration.GetSection($"SaveHarbor:Games:{game}");
        var legacy = game == GameId.Windrose ? configuration.GetSection("SaveHarbor:GameLauncher") : null;
        byGame[game] = new GameOptions
        {
            LaunchUri = FirstNonEmpty(section["LaunchUri"], legacy?["LaunchUri"], DefaultLaunchUri(game)),
            ExecutablePath = FirstNonEmpty(section["ExecutablePath"], legacy?["ExecutablePath"], string.Empty),
            SaveRoot = section["SaveRoot"] ?? string.Empty
        };
    }

    return new GameOptionsProvider(byGame);
}

public static GameId? ReadGameArgument(IReadOnlyList<string> args)
{
    for (var index = 0; index < args.Count - 1; index++)
    {
        if (string.Equals(args[index], "--game", StringComparison.OrdinalIgnoreCase) &&
            GameIdExtensions.TryParseStorageKey(args[index + 1], out var game))
        {
            return game;
        }
    }

    return null;
}

private static string DefaultLaunchUri(GameId game) => game switch
{
    GameId.Windrose => "steam://rungameid/3041230",
    GameId.Dragonwilds => "steam://rungameid/1374490",
    _ => string.Empty
};

private static string FirstNonEmpty(params string?[] values) =>
    values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
```
(The only `switch` on `GameId` outside definitions is this default-URI helper; it is config defaulting. Acceptable; do not add others.)

Delete `Infrastructure/GameLauncherOptions.cs`. In `App/appsettings.json` replace the `GameLauncher` section with:
```json
"Games": {
  "Windrose": { "LaunchUri": "steam://rungameid/3041230", "ExecutablePath": "" },
  "Dragonwilds": { "LaunchUri": "steam://rungameid/1374490", "ExecutablePath": "", "SaveRoot": "" }
}
```
`App.xaml.cs` keeps: ctor/DI/OnStartup/OnExit/ConfigureLogging/GetLowestConfiguredLevel/LogAppInformation and the three exception handlers, and calls `AppOptionsLoader.*`.
**Done when:** build green; `App.xaml.cs` is ~120 lines; options values identical to before (Windrose launch URI unchanged).

### T02.3 Game definition and registry

`App/Services/IGameDefinition.cs`
```csharp
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public enum ProcessMatch
{
    Exact,
    Contains
}

public interface IGameDefinition
{
    GameId Id { get; }
    string StorageKey { get; }
    string DisplayName { get; }
    string LaunchUri { get; }
    string ExecutablePath { get; }
    IReadOnlyList<string> ProcessNames { get; }
    ProcessMatch ProcessMatch { get; }
    TimeSpan SaveSettleDelay { get; }
    Uri ThemeDictionary { get; }
    string? PostRestoreHint { get; }
    IGameSaveAdapter SaveAdapter { get; }
}
```
`App/Services/IGameRegistry.cs`: `IReadOnlyList<IGameDefinition> All { get; }` and `IGameDefinition Get(GameId id);`.
`App/Infrastructure/Games/GameRegistry.cs`: `public sealed class GameRegistry(IEnumerable<IGameDefinition> definitions) : IGameRegistry` — stores `definitions.ToArray()`; `Get` throws `InvalidOperationException` if missing.

`App/Infrastructure/Games/Windrose/WindroseGameDefinition.cs`
```csharp
public sealed class WindroseGameDefinition(GameOptionsProvider optionsProvider, WindroseSaveAdapter saveAdapter) : IGameDefinition
{
    public GameId Id => GameId.Windrose;
    public string StorageKey => Id.ToStorageKey();
    public string DisplayName => "Windrose";
    public string LaunchUri => optionsProvider.Get(Id).LaunchUri;
    public string ExecutablePath => optionsProvider.Get(Id).ExecutablePath;
    public IReadOnlyList<string> ProcessNames { get; } = ["Windrose", "R5"];
    public ProcessMatch ProcessMatch => ProcessMatch.Contains;
    public TimeSpan SaveSettleDelay => TimeSpan.FromSeconds(10);
    public Uri ThemeDictionary { get; } = new("pack://application:,,,/Resources/Styles/DarkTheme.xaml");
    public string? PostRestoreHint => null;
    public IGameSaveAdapter SaveAdapter => saveAdapter;
}
```
(`ThemeDictionary` is changed in T06.)

### T02.4 Save adapter

`App/Services/IGameSaveAdapter.cs`
```csharp
public interface IGameSaveAdapter
{
    WorldPayloadKind PayloadKind { get; }
    string SaveRootPath { get; }
    Task<IReadOnlyList<GameSaveRoot>> DiscoverSaveRootsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameWorld>> DiscoverWorldsAsync(CancellationToken cancellationToken = default);
    Task<GameWorld?> ReadWorldAsync(string savePath, CancellationToken cancellationToken = default);
    IReadOnlyList<string> GetPayloadFiles(GameWorld world);
}
```
Delete `Services/IWindroseSaveDiscoveryService.cs`. Move (plain file move, **no git**) `Infrastructure/WindroseSaveDiscoveryService.cs` → `Infrastructure/Games/Windrose/WindroseSaveAdapter.cs`, rename the class to `WindroseSaveAdapter : IGameSaveAdapter`, namespace `SaveHarbor.App.Infrastructure.Games.Windrose`. Changes inside:
- Constructor: `public WindroseSaveAdapter(GameOptionsProvider optionsProvider)`; store `optionsProvider`.
- `GetProfilesRoot()` becomes an instance method: if `optionsProvider.Get(GameId.Windrose).SaveRoot` is non-empty use it (expanded with `Environment.ExpandEnvironmentVariables`), else the existing `%LOCALAPPDATA%\R5\Saved\SaveProfiles`. `SaveRootPath` returns the same value.
- `DiscoverProfilesAsync` → `DiscoverSaveRootsAsync`, returning `GameSaveRoot(GameId.Windrose, profileDirectory.Name, profileDirectory.FullName, worldsPath, $"RocksDB {rocksDbVersion}", lastModified)`. `CreateProfile` becomes `CreateSaveRoot`.
- `ReadWorldAsync` constructs `GameWorld(GameId.Windrose, worldId, name, preset, ...)` (preset → `Subtitle`).
- `PayloadKind => WorldPayloadKind.Directory`.
- `GetPayloadFiles(world)` = `Directory.EnumerateFiles(world.SavePath, "*", SearchOption.AllDirectories).Where(file => !string.Equals(Path.GetFileName(file), "LOCK", StringComparison.OrdinalIgnoreCase)).ToArray()` (matches `CopyDirectory`'s LOCK exclusion).
- Everything else (RocksDB root probing, Unreal timestamp conversion) unchanged.

### T02.5 Active game context

`App/Services/IActiveGameContext.cs`
```csharp
public interface IActiveGameContext
{
    IGameDefinition Current { get; }
    event EventHandler<IGameDefinition>? ActiveGameChanged;
    void SetActive(GameId game);
}
```
`IAppDataPathProvider` (+ `AppDataPathProvider`, + `TestPathProvider`): add `string AppSettingsPath { get; }` → `Path.Combine(AppDataRoot, "app-settings.json")`.

`App/Domain/AppSettings.cs`: `public sealed class AppSettings { public int SchemaVersion { get; set; } = 1; public string LastGame { get; set; } = string.Empty; }`

`App/Infrastructure/ActiveGameContext.cs`
```csharp
public sealed class ActiveGameContext : IActiveGameContext
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IGameRegistry registry;
    private readonly IAppDataPathProvider pathProvider;

    public ActiveGameContext(IGameRegistry registry, IAppDataPathProvider pathProvider, GameId? startupOverride)
    {
        ...
        Current = registry.Get(startupOverride ?? ReadLastGame() ?? GameId.Windrose);
    }

    public IGameDefinition Current { get; private set; }
    public event EventHandler<IGameDefinition>? ActiveGameChanged;

    public void SetActive(GameId game)
    {
        if (Current.Id == game) return;
        Current = registry.Get(game);
        SaveLastGame(game);
        ActiveGameChanged?.Invoke(this, Current);
    }
    // ReadLastGame: try/catch (IOException, JsonException) → null; SaveLastGame: try/catch → warning, never throw
}
```
This class deliberately does **not** take `IAppLogger` (T04.8 makes the logger depend on this context; a logger parameter here would create a DI cycle). It logs its two warnings through `Serilog.Log.ForContext("Keyword", "App").Warning(...)`.
DI cannot inject `GameId?` automatically → register with a factory in `App.xaml.cs`:
`services.AddSingleton<IActiveGameContext>(sp => new ActiveGameContext(sp.GetRequiredService<IGameRegistry>(), sp.GetRequiredService<IAppDataPathProvider>(), AppOptionsLoader.ReadGameArgument(Environment.GetCommandLineArgs())));`

### T02.6 Process detection and launcher

`IProcessDetectionService`: `bool IsGameRunning(IGameDefinition game);` (remove `IsWindroseRunning`).
`WindowsProcessDetectionService`:
```csharp
public bool IsGameRunning(IGameDefinition game)
{
    return game.ProcessMatch == ProcessMatch.Exact
        ? game.ProcessNames.Any(IsProcessRunningExact)
        : IsAnyProcessNameContaining(game.ProcessNames);
}

private static bool IsProcessRunningExact(string processName)
{
    var processes = Process.GetProcessesByName(processName);
    try { return processes.Length > 0; }
    finally { foreach (var process in processes) process.Dispose(); }
}

private static bool IsAnyProcessNameContaining(IReadOnlyList<string> hints)
{
    var processes = Process.GetProcesses();
    try
    {
        return processes.Any(process =>
        {
            try { return hints.Any(hint => process.ProcessName.Contains(hint, StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        });
    }
    finally { foreach (var process in processes) process.Dispose(); }
}
```
`IGameLauncherService.LaunchAsync(IGameDefinition game, CancellationToken cancellationToken = default)`. Rename `Infrastructure/WindroseGameLauncherService.cs` → `SteamGameLauncherService.cs`, class `SteamGameLauncherService(IAppLogger logger) : IGameLauncherService`; target = `string.IsNullOrWhiteSpace(game.ExecutablePath) ? game.LaunchUri : game.ExecutablePath`; messages use `game.DisplayName` (`$"{game.DisplayName} launcher is not configured."`, `$"{game.DisplayName} launch requested."`, `$"Could not launch {game.DisplayName}. Check that Steam is installed, or configure an executable path."`). Log text likewise.

### T02.7 Backup + cloud (game-aware, **still unscoped paths**)

- `IBackupService.ReadManifestAsync(string backupPath, GameId expectedGame, CancellationToken ct = default)`. In `ZipBackupService.ReadManifestAsync` replace the `"Windrose"` check with `!string.Equals(manifest.Game, expectedGame.ToStorageKey(), OrdinalIgnoreCase)` and message `$"This backup does not look like a {expectedGame} SaveHarbor backup."`. Update the internal caller (line ~127: `ImportBackupAsNewWorldAsync` → pass `profile.Game`) and `MainWindowViewModel.LocalCommands.cs` line ~145 (pass `_activeGame.Current.Id`).
- `CreateArchive`: `Game = world.Game.ToStorageKey()` (anonymous manifest).
- `FolderCloudProvider` line ~116 and `GoogleDriveCloudProvider` line ~227: `Game = request.World.Game.ToStorageKey()`.
- `CloudWorldManifest.Game` default stays `"Windrose"`; add a one-line comment: `// Legacy manifests written before multi-game support have no Game and are Windrose.`
- Everything else in these classes unchanged. (Per-game paths come in T04.)

### T02.8 View model

1. `MainWindowViewModel.cs`: replace `IWindroseSaveDiscoveryService _saveDiscoveryService` with `IActiveGameContext _activeGame` and add `IGameRegistry _gameRegistry` (ctor parameters replace `saveDiscoveryService`; subscribe `_activeGame.ActiveGameChanged += OnActiveGameChanged;`). Observable `profileStatus` initial text: `"Checking save profile..."`. `ObservableCollection<GameWorld> Worlds`.
2. Every `_saveDiscoveryService.X` → `_activeGame.Current.SaveAdapter.X` with the renames (`DiscoverProfilesAsync` → `DiscoverSaveRootsAsync`).
3. `UpdateGameStatus()` → `IsGameRunning = _processDetectionService.IsGameRunning(_activeGame.Current);`
4. `_gameLauncherService.LaunchAsync()` → `LaunchAsync(_activeGame.Current)`.
5. New partial `MainWindowViewModel.GameSelection.cs`:
```csharp
public partial class MainWindowViewModel
{
    public string ActiveGameName => _activeGame.Current.DisplayName;

    public IReadOnlyList<IGameDefinition> AvailableGames => _gameRegistry.All;   // inject IGameRegistry

    public bool CanSwitchGame => !IsBusy && !IsGameRunning && !HasOwnCloudSession();

    public string SwitchGameDisabledReason => IsBusy ? "Wait for the current operation to finish."
        : IsGameRunning ? $"Close {ActiveGameName} before switching games."
        : HasOwnCloudSession() ? $"End your {ActiveGameName} session before switching games."
        : string.Empty;

    [RelayCommand(CanExecute = nameof(CanSwitchGame))]
    private void SwitchGame(GameId game) => _activeGame.SetActive(game);

    private async void OnActiveGameChanged(object? sender, IGameDefinition game)
    {
        ...reset: Worlds.Clear(); SelectedWorld = null; CloudStatus = null; hasObservedGameRunningDuringSession = false;
        OnPropertyChanged(nameof(ActiveGameName)); OnPropertyChanged(nameof(SafetyHint)); ...
        await RefreshAsync();
    }
}
```
Notify `CanSwitchGame`/`SwitchGameDisabledReason` when `IsBusy`, `IsGameRunning`, `CloudStatus` change (extend `PropertyChangeHandlers` — `OnIsBusyChanged` partial may not exist yet; add it) and add `SwitchGameCommand` to `NotifyCommandStates()`. (`async void` here is an event handler; wrap the body in try/catch → `_errorHandler.Handle(ex, "Switch game", AppLogKeyword.Ui)`.)
6. String neutralisation — replace `"Windrose"` with `ActiveGameName` (interpolate) in: `CloudCommands.cs` lines 96–97, 127–128, 133, 184, 215, 224, 226, 231, 250–251; `ComputedProperties.cs` 59–60 (`SafetyHint`); `GameMonitor.cs` 48, 64, 65; `LocalCommands.cs` 20, 45, 63–64, 90–91, 129–130, 160–164, 200; `Operations.cs` 80.
   Wording that mentions "RocksDB" must become game-neutral: `"…so the save files are not copied while they are changing."`.
   `LocalSaveRoot` in `ComputedProperties.cs` → `_activeGame.Current.SaveAdapter.SaveRootPath`.
   `"Start Windrose once…"` messages → `$"Start {ActiveGameName} once…"`.
7. `ui-text.en.json` neutral edits: `App.Subtitle` → `"Co-op world sharing helper"`; replace the word "Windrose" by "game" in the keys listed by `rg -n Windrose App/Resources/Texts/ui-text.en.json` (lines 5, 14, 19, 21, 23, 24, 28, 51, 53, 57), e.g. `"World.PresetTooltip": "Secondary world detail (preset or type), when the game provides one."`. Do **not** add `GameText` extensions.

### T02.9 DI (`App/App.xaml.cs`)
```csharp
services.AddSingleton(AppOptionsLoader.LoadGameOptions());
services.AddSingleton<WindroseSaveAdapter>();
services.AddSingleton<IGameDefinition, WindroseGameDefinition>();
services.AddSingleton<IGameRegistry, GameRegistry>();
services.AddSingleton<IActiveGameContext>(/* factory from T02.5 */);
```
Remove `IWindroseSaveDiscoveryService`, `GameLauncherOptions` registrations; `IGameLauncherService` → `SteamGameLauncherService`.

### T02.10 Tests (`SaveHarbor.Tests/`)
- `Games/WindroseSaveAdapterTests.cs`, using `TempDirectory` and a helper `SaveFixtures.CreateWindroseWorld(string profilesRoot, string profileId, string worldFolder, string islandId, string worldName)` that creates `<profilesRoot>/<profileId>/RocksDB_v2/0.10.0/Worlds/<worldFolder>/WorldDescription.json` (valid JSON: `{"Version":1,"WorldDescription":{"islandId":"…","WorldName":"…","CreationTime":0,"WorldPresetType":"Medium"}}`) plus `000001.sst`, `CURRENT`, `MANIFEST-000001`, `LOCK`. Adapter built with `new GameOptionsProvider(new Dictionary<GameId, GameOptions>{ [GameId.Windrose] = new(){ SaveRoot = profilesRoot } })`.
  Tests: discovers the world and maps `Game/WorldId/WorldName/Subtitle`; falls back to folder name when `islandId` empty; ignores folders without `WorldDescription.json`; `GetPayloadFiles` excludes `LOCK`; prefers `RocksDB_v2` over `RocksDB`.
- `Options/AppOptionsLoaderTests.cs`: `ReadGameArgument(["--game","dragonwilds"])` → Dragonwilds; unknown value → null; missing → null.
- `Games/ActiveGameContextTests.cs`: with a `TestPathProvider` and a fake `IGameRegistry` (two stub definitions), default is Windrose; `SetActive(Dragonwilds)` persists and a new instance restores it; startup override wins; corrupt `app-settings.json` → Windrose, no throw.
  Write minimal hand-rolled stubs (`StubGameDefinition : IGameDefinition`) in `SaveHarbor.Tests/Support/`.

### T02.11 Verification
- `dotnet build` + `dotnet test` green.
- `rg -n "Windrose" App --glob "*.cs"` → matches only under `Infrastructure/Games/Windrose/`, `Domain/CloudWorldManifest.cs` (the legacy comment/default), `AppOptionsLoader.cs` (the default URI helper + legacy section name), and `GameId`/`GameIdExtensions`.
- Manual smoke test of Windrose (see `build-and-verify`): start, worlds listed, backup, restore, LocalTest upload/download, Play launches, game-close auto-end. Report which steps you could and could not perform (the game may not be installed — say so).

### CHECKPOINT CP2 → stop, report.
