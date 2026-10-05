# 12 — Tasks: Theme (Plan 06) and Play Flow (Plan 05)

Read [09-implementation-handoff.md](09-implementation-handoff.md) first. Order: **T06 (CP5) before T05 (CP6)** — the new Play UI is built on the new theme. Prerequisite: CP4 done and green. Paths: `App/` = `SaveHarbor.App/`.

---

# T06 — Theming and game switch (CP5) · plan 06

Skill to read: `dragonwilds-fantasy-theme`. **Design simplification decided during task breakdown:** a WPF `Border` cannot host an inner inset line through a style alone, so the "double-rule panel" is dropped. Dragonwilds panels use the same shape as Windrose with warmer colours; fantasy cues are limited to colour, the display font, the diamond section glyph, the header divider glyph, and the gold primary button. (The skill and plan 06 are updated accordingly in T06.0.)

The skill and plan 06 already reflect this simplification (no double rule, no highlight overlay, plain header divider); if they ever disagree with this file, this file wins.

### T06.1 Colour dictionaries (new, `App/Resources/Themes/`)

Both files define **exactly the same key set**. Keys (all `SolidColorBrush` unless noted):
`AppBackgroundBrush, PanelBrush, PanelAltBrush, InputBrush, LineBrush, LineSoftBrush, InkBrush, MutedBrush, SubtleBrush, AccentBrush, AccentHoverBrush, AccentPressedBrush, DangerBrush, WarnBrush, SuccessBrush, InfoBrush, OrnamentBrush, PrimaryButtonForegroundBrush, PlayCardBackgroundBrush, ToneAccentBackgroundBrush, ToneAccentBorderBrush, ToneInfoBackgroundBrush, ToneInfoBorderBrush, ToneSuccessBackgroundBrush, ToneSuccessBorderBrush, ToneWarnBackgroundBrush, ToneWarnBorderBrush, ToneNeutralBackgroundBrush`,
plus `AppDisplayFontFamily` (`FontFamily`), `ShowOrnamentGlyphs` (`Visibility`), `GameIconGeometry` (`Geometry`), and — if a gradient needs a `Color` (T06.7) — `AccentColor` (`Color`).

`Windrose.Colors.xaml` values:
| Key | Value |
|---|---|
| the 16 contract brushes | current values from `DarkTheme.xaml` lines 6–21, unchanged |
| `OrnamentBrush` | `#1F2931` |
| `PrimaryButtonForegroundBrush` | copy whatever foreground `PrimaryButton` uses today (read `DarkTheme.xaml` ~106–145) |
| `PlayCardBackgroundBrush` | `#10231F` (ActionsSectionView line 22) |
| `ToneAccentBackgroundBrush` / `Border` | `#173D42` / `#19B8AA` |
| `ToneInfoBackgroundBrush` / `Border` | `#1E2E44` / `#36577D` |
| `ToneSuccessBackgroundBrush` / `Border` | `#1E332D` / `#357866` |
| `ToneWarnBackgroundBrush` / `Border` | `#352A1E` / `#8A6634` |
| `ToneNeutralBackgroundBrush` | `#1A2229` |
| `AppDisplayFontFamily` | `Segoe UI` |
| `ShowOrnamentGlyphs` | `Collapsed` |
| `GameIconGeometry` | a simple compass: `M12,2 L14,10 L22,12 L14,14 L12,22 L10,14 L2,12 L10,10 Z` |

`Dragonwilds.Colors.xaml` values:
| Key | Value |
|---|---|
| `AppBackgroundBrush` | `RadialGradientBrush` centre `#1A1611`, edge `#100D0A` (GradientOrigin/Center 0.5,0.35, RadiusX/Y 0.9) |
| `PanelBrush` / `PanelAltBrush` / `InputBrush` | `#1E1A15` / `#191510` / `#110E0B` |
| `LineBrush` / `LineSoftBrush` | `#3A3024` / `#2A231B` |
| `InkBrush` / `MutedBrush` / `SubtleBrush` | `#F1E6D0` / `#B8A88C` / `#7D6F5A` |
| `AccentBrush` / `AccentHoverBrush` / `AccentPressedBrush` | `#C9A24A` / `#DDB75E` / `#A8853A` |
| `DangerBrush` / `WarnBrush` / `SuccessBrush` / `InfoBrush` | `#E0614F` / `#E89A3C` / `#7FBF6A` / `#7FA8C9` |
| `OrnamentBrush` | `#5C4A30` |
| `PrimaryButtonForegroundBrush` | `#1A140C` |
| `PlayCardBackgroundBrush` | `#241D10` |
| `ToneAccentBackgroundBrush` / `Border` | `#3A2F17` / `#C9A24A` |
| `ToneInfoBackgroundBrush` / `Border` | `#1F2B36` / `#4F6F8A` |
| `ToneSuccessBackgroundBrush` / `Border` | `#26301F` / `#5F8A4F` |
| `ToneWarnBackgroundBrush` / `Border` | `#3A2A16` / `#B4772F` |
| `ToneNeutralBackgroundBrush` | `#1D1913` |
| `AppDisplayFontFamily` | `pack://application:,,,/Resources/Fonts/#Cinzel, Georgia` |
| `ShowOrnamentGlyphs` | `Visible` |
| `GameIconGeometry` | `M12,2 C16,6 20,7 22,7 C21,14 17,19 12,22 C7,19 3,14 2,7 C4,7 8,6 12,2 Z` (a simple shield/wing silhouette; original shape) |

Contrast (already measured on `PanelBrush`): Subtle is 3.5:1 → never use `SubtleBrush` for information the user needs (only hints/disabled).

**Test (`SaveHarbor.Tests/Themes/ThemeDictionaryTests.cs`)**: load both files with `System.Xml.Linq.XDocument`, collect `x:Key` attribute values (namespace `http://schemas.microsoft.com/winfx/2006/xaml`), assert both sets are equal, and that every key listed above exists. (No WPF types needed.) Reference the XAML files from the test project via `<Content Include="..\SaveHarbor.App\Resources\Themes\*.xaml" Link="Themes\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`.

### T06.2 Controls dictionary
1. Rename (plain `mv`, no git) `Resources/Styles/DarkTheme.xaml` → `Resources/Styles/Controls.xaml`.
2. Delete the 16 brush definitions (lines 6–21) from it (they now live in the theme dictionaries). Keep `BooleanToVisibilityConverter` and every style/template.
3. In **Controls.xaml and every file under `Views/` and `MainWindow.xaml`**, change `{StaticResource XxxBrush}` → `{DynamicResource XxxBrush}` for brush keys only (regex `StaticResource (\w+Brush)\}` → `DynamicResource $1}`). Leave `StaticResource` for styles (`PrimaryButton`, `Panel`, `MutedText`, …), converters and `BasedOn`.
4. `rg -n "FindResource|TryFindResource|StaticResource" App --glob "*.cs"` — if any code-behind resolves a brush, switch it to `TryFindResource` at the point of use (not cached).

### T06.3 Fonts (owner action — do not download anything)
- Add to the csproj: `<Resource Include="Resources\Fonts\*.ttf" />` and `<Content Include="Resources\Fonts\OFL.txt" CopyToOutputDirectory="PreserveNewest" Condition="Exists('Resources\Fonts\OFL.txt')" />`. Create `Resources/Fonts/README.md`: "Place the Cinzel TTF files (SIL OFL 1.1) here together with OFL.txt. Without them the UI falls back to Georgia."
- Tell the owner in the checkpoint report that the font is a third-party asset (licence: SIL OFL 1.1, free for embedding).

### T06.4 `ThemeService`
`App/Services/IThemeService.cs`: `void Apply(IGameDefinition game);`
`App/Infrastructure/ThemeService.cs`:
```csharp
public sealed class ThemeService : IThemeService
{
    private const int ColorsDictionaryIndex = 0;

    public ThemeService(IActiveGameContext activeGame)
    {
        activeGame.ActiveGameChanged += (_, game) => Apply(game);
        Apply(activeGame.Current);
    }

    public void Apply(IGameDefinition game)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var colors = SystemParameters.HighContrast
            ? new Uri("pack://application:,,,/Resources/Themes/HighContrast.Colors.xaml")
            : game.ThemeDictionary;
        dictionaries[ColorsDictionaryIndex] = new ResourceDictionary { Source = colors };
    }
}
```
- `App.xaml` merged dictionaries: `[0] Resources/Themes/Windrose.Colors.xaml`, `[1] Resources/Styles/Controls.xaml`; add an XML comment above: `<!-- Index 0 = game colours, replaced by ThemeService. Keep it first. -->`.
- Register `services.AddSingleton<IThemeService, ThemeService>();` and **resolve it once in `App.OnStartup` before `MainWindow` is created**: `_host.Services.GetRequiredService<IThemeService>();`.
- `WindroseGameDefinition.ThemeDictionary` → `pack://application:,,,/Resources/Themes/Windrose.Colors.xaml`; `DragonwildsGameDefinition.ThemeDictionary` → `…/Dragonwilds.Colors.xaml`.
- `HighContrast.Colors.xaml`: same key set; brushes map to system colours using `{x:Static SystemColors.WindowBrush}` etc. (`AppBackground/Panel/PanelAlt/Input` → `WindowBrush`; `LineBrush/LineSoft` → `WindowTextBrush`; `Ink/Muted/Subtle` → `WindowTextBrush`; `Accent*` → `HotTrackBrush`; `Danger/Warn/Success/Info` → `WindowTextBrush`; `Tone*Background` → `WindowBrush`; `Tone*Border` → `WindowTextBrush`; `PrimaryButtonForegroundBrush` → `HighlightTextBrush`; `OrnamentBrush` → `WindowTextBrush`; `ShowOrnamentGlyphs` Collapsed; font `Segoe UI`). The `ThemeDictionaryTests` key-set test covers this file too.

### T06.5 Game switch (header)
1. `App/ViewModels/GameOptionViewModel.cs`: `public sealed partial class GameOptionViewModel(GameId id, string displayName) : ObservableObject { public GameId Id { get; } = id; public string DisplayName { get; } = displayName; [ObservableProperty] private bool isActive; }`.
2. `MainWindowViewModel.GameSelection.cs`: replace `AvailableGames` (T02.8) with `public ObservableCollection<GameOptionViewModel> GameOptions { get; }` built in the constructor from `_gameRegistry.All`; keep `IsActive` in sync in `OnActiveGameChanged` and at construction.
3. `RadioButton` switch style `GameSwitchButton` in `Controls.xaml`: `ControlTemplate` = `Border` (CornerRadius 6, Padding `10,5`), content = `StackPanel` with a 12×12 `Path` (`Data="{DynamicResource GameIconGeometry}"`, `Fill="{DynamicResource MutedBrush}"`, `Stretch="Uniform"`) and the `DisplayName` `TextBlock` (`FontSize 12`, `FontWeight SemiBold`). Trigger `IsChecked=True` → `Background {DynamicResource PanelBrush}`, `BorderBrush {DynamicResource AccentBrush}`, text `InkBrush`, path fill `AccentBrush`. Unchecked: transparent background, `LineSoftBrush` border. `IsEnabled=False` → `Opacity 0.55`.
   Note: the icon geometry is the *active* theme's, so both buttons show the active game's glyph — acceptable and simple. (Per-game icons would need per-game resources; out of scope.)
4. `HeaderStatusView.xaml`: replace the subtitle `TextBlock` (`App.Subtitle`) in the left `StackPanel` with the switch: 
```xml
<ItemsControl ItemsSource="{Binding GameOptions}" Margin="0,6,0,0"
              ToolTip="{Binding SwitchGameDisabledReason}" ToolTipService.ShowOnDisabled="True">
    <ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <RadioButton GroupName="GameSwitch" Style="{StaticResource GameSwitchButton}" Margin="0,0,6,0"
                         Content="{Binding DisplayName}" IsChecked="{Binding IsActive, Mode=OneWay}"
                         Command="{Binding DataContext.SwitchGameCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                         CommandParameter="{Binding Id}" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```
   The title `TextBlock` gets `FontFamily="{DynamicResource AppDisplayFontFamily}"`. The tool-tip must be empty (and therefore not shown) when switching is allowed: bind `ToolTipService.IsEnabled="{Binding CanSwitchGame, Converter=<invert>}"` — use a `DataTrigger` on `CanSwitchGame` instead of a new converter (set `ToolTipService.IsEnabled=False` when `True`).
   Remove the now-unused `App.Subtitle` key from `ui-text.en.json` **only if** `rg "App.Subtitle"` has no other users.
5. Keep the header grid widths unchanged unless the title row now collides (min window width 1040): if it does, reduce the right grid's status chip column from 250 → 220 and report it.

### T06.6 Section glyph, header divider, play button
1. `Views/Controls/SectionTitleView.xaml(.cs)` (UserControl, DP `Text`): `StackPanel` horizontal: `Path` diamond (`Data="M3,0 L6,3 L3,6 L0,3 Z"`, `Width/Height 6`, `Fill="{DynamicResource OrnamentBrush}"`, `Margin="0,0,7,0"`, `VerticalAlignment="Center"`, `Visibility="{DynamicResource ShowOrnamentGlyphs}"`) + `TextBlock` styled `SectionTitle` with `FontFamily="{DynamicResource AppDisplayFontFamily}"`. Replace each `<TextBlock … Style="{StaticResource SectionTitle}" …/>` (`rg -n "SectionTitle" App/Views`) with `<controls:SectionTitleView Text="…"/>`, preserving the bound text and any ToolTip/Margin.
2. Header divider: under the header `Grid` in `MainWindow.xaml` row 0 add a 1 px `Border` (`Background="{DynamicResource LineSoftBrush}"`, `Margin="20,0"`, `VerticalAlignment="Bottom"`) — identical in both themes; no glyph (keeps minimal).
3. `Controls.xaml`: `PrimaryButton` foreground → `{DynamicResource PrimaryButtonForegroundBrush}` (Windrose value equals today's, so no visual change); new style `PlayButton` (`BasedOn PrimaryButton`): `FontFamily {DynamicResource AppDisplayFontFamily}`, `FontSize 14`, `FontWeight SemiBold`. The Play button in `ActionsSectionView.xaml` uses `PlayButton`.

### T06.7 Remove hex literals
Exact replacements (`ActionsSectionView.xaml` unless noted): line 22 `Background="#10231F"` → `{DynamicResource PlayCardBackgroundBrush}`; lines 156–157 → `ToneAccentBackgroundBrush`/`ToneAccentBorderBrush`; 166–167 and 203–204 → `ToneInfo*`; 176–177 → `ToneSuccess*`; 186–187 → `ToneWarn*`; 213 → `ToneNeutralBackgroundBrush`; `IndeterminateProgressLineView.xaml:6` `#10161A` → `{DynamicResource PanelAltBrush}`; `MainWindow.xaml:14` remove the hardcoded `Background` (the inner `Grid` already uses `AppBackgroundBrush`; set the `Window` `Background="{DynamicResource AppBackgroundBrush}"`).
If `IndeterminateProgressLineView` gradient stops use literal colours, switch them to `{DynamicResource AccentColor}` and add `AccentColor` (`Color`) to the three theme dictionaries.
**Done when:** `rg -n "#[0-9A-Fa-f]{6,8}" App/Views App/MainWindow.xaml` → no matches.

### T06.8 Optional polish (do last; skip if time-boxed, say so)
150 ms fade on theme change: in `ThemeService`, before swapping, animate `Application.Current.MainWindow.Content` opacity 1 → 0.85 → 1 using a `DoubleAnimation` (`Duration 75 ms`, `AutoReverse`), skipped when `!SystemParameters.ClientAreaAnimation`. No other animations.

### T06.9 Verification (CP5)
- Build + tests green (`ThemeDictionaryTests` included).
- `rg` checks: no hex in views; no `StaticResource …Brush` left (`rg "StaticResource \w+Brush" App` → none).
- Run the app (`dotnet run --project SaveHarbor.App`), toggle the switch both ways, and report observations. If you cannot view the UI yourself, say so explicitly and hand the owner this review checklist: Windrose looks unchanged; Dragonwilds recolours header, panels, buttons, combo dropdown, tooltips, toasts, progress line, dialogs opened afterwards; switch is disabled with a reason while busy/running/locked; layout intact at 1040×660 and at 150 % DPI; High Contrast mode usable.
- Windrose before/after: confirm `Windrose.Colors.xaml` values equal the old `DarkTheme.xaml` brushes (diff them).

### CHECKPOINT CP5 → stop, report.

---

# T05 — One-click Play flow and lock hardening (CP6) · plan 05

Skills: `play-session-flow`, `cloud-sync-and-locking`, `wpf-mvvm-conventions`. Prerequisite: T03.0 Phase 0 **V5 passed** if the Dragonwilds behaviour is to ship (the code itself is game-agnostic).

### T05.1 Lock policy (pure code, `App/Infrastructure/SessionLockPolicy.cs`)
```csharp
public static class SessionLockPolicy
{
    public static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan GameStartTimeout = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan SettleCap = TimeSpan.FromMinutes(2);

    public static bool IsExpired(CloudSessionLock sessionLock, DateTimeOffset now) => now > sessionLock.ExpiresAtUtc;

    public static bool IsOwn(CloudSessionLock? sessionLock) =>
        sessionLock is not null && string.Equals(sessionLock.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    // Active locks beat expired ones; then earliest start; then lowest LockId. Used when two lock files exist.
    public static CloudSessionLock? ResolveWinner(IEnumerable<CloudSessionLock> locks, DateTimeOffset now) =>
        locks.OrderBy(candidate => IsExpired(candidate, now) ? 1 : 0)
             .ThenBy(candidate => candidate.StartedAtUtc)
             .ThenBy(candidate => candidate.LockId, StringComparer.Ordinal)
             .FirstOrDefault();
}
```
`CloudSessionLock`: add `public string Game { get; set; } = string.Empty;` (storage key; additive). `CloudSyncService.SessionLocks.cs` helpers (`IsActiveOtherPlayerLock`, `IsOwnLock`, `IsActiveOwnLock`) delegate to the policy (keep their names so callers don't change).
**LockTtl note:** old builds still read `ExpiresAtUtc` as authoritative, so the shorter TTL is compatible *only because* heartbeats keep extending it — T05.3 must ship in the same checkpoint.

**Tests:** `ResolveWinner` — active beats expired regardless of start time; earlier start wins; tie → lower LockId; both sides computing from the same set pick the same winner (permute input order).

### T05.2 Providers: heartbeat-safe reads, verified acquire
1. `ICloudProvider`: add `TimeSpan LockVerifyDelay { get; }` (Folder 200 ms, Drive 2 s, NotConfigured `TimeSpan.Zero`, Fake `TimeSpan.Zero`).
2. `GoogleDriveCloudProvider.GetSessionLockAsync(game, worldId)`: list **all** non-trashed `active-session.json` in the locks folder (new helper `ListFileIdsByNameAsync` in `.Folders.cs` returning ids), download each, `SessionLockPolicy.ResolveWinner(...)`. Do not delete anything here.
3. `ClearSessionLockAsync(game, worldId, lockId)`: delete every `active-session.json` whose content `LockId == lockId`; if none match, do nothing (it is not ours / already replaced). Same in `FolderCloudProvider` (read file; delete only if `LockId` matches; an unreadable file is left alone).
4. `WriteSessionLockAsync` keeps update-or-create semantics.
5. `CloudSyncService.Sessions.cs` `StartSessionAsync(GameWorld world, bool allowTakeOver = false, CancellationToken ct = default)` (update `ICloudSyncService`):
   - existing precondition checks stay (connected, has latest, local base == latest, other-player active lock blocks).
   - **new:** a foreign **expired** lock and `!allowTakeOver` → `CloudSyncResult(false, CloudSyncState.SomeonePlaying, $"{lock.PlayerName}'s session expired {DisplayFormatter.FormatAge(lock.ExpiresAtUtc)}. Confirm to take over.")`.
   - build the lock with `Game = world.Game.ToStorageKey()`, `ExpiresAtUtc = now + SessionLockPolicy.LockTtl`.
   - **verified acquire:**
```csharp
await cloudProvider.WriteSessionLockAsync(world.Game, sessionLock, ct);
await Task.Delay(cloudProvider.LockVerifyDelay, ct);
var current = await cloudProvider.GetSessionLockAsync(world.Game, world.WorldId, ct);
if (current is null || !string.Equals(current.LockId, sessionLock.LockId, StringComparison.Ordinal))
{
    await cloudProvider.ClearSessionLockAsync(world.Game, world.WorldId, sessionLock.LockId, ct); // remove only our own file
    var winner = current?.PlayerName ?? "Another player";
    return new CloudSyncResult(false, CloudSyncState.SomeonePlaying, $"{winner} started a session at the same time.");
}
```
   - add a short comment explaining *why* (Drive has no compare-and-swap, so write → wait → verify is the substitute).
6. `HeartbeatAsync(GameWorld world, ct)` on `ICloudSyncService`: read lock; `null` or foreign → `CloudSyncResult(false, CloudSyncState.SomeonePlaying, "Your session lock was taken over.")`; own → rewrite same `LockId` with `LastHeartbeatAtUtc = now`, `ExpiresAtUtc = now + LockTtl` → success. (It does not call `RefreshStatusAsync` — one provider read, one write.)

**Tests (FakeCloudProvider hooks):** two clients race (`BeforeLockWrite` lets "client B" write its lock between A's write and verify) → A loses, A's file removed, B's remains; sole client acquires; expired foreign lock without `allowTakeOver` blocked, with it acquires; heartbeat extends expiry and keeps `LockId`; heartbeat reports takeover when the lock is foreign.

### T05.3 Payload hash (`App/Utilities/PayloadHasher.cs`) + local state
```csharp
public static string Compute(GameWorld world, IGameSaveAdapter adapter)
{
    var baseDirectory = adapter.PayloadKind == WorldPayloadKind.Directory ? world.SavePath : Path.GetDirectoryName(world.SavePath)!;
    using var sha = SHA256.Create();   // incremental: for each file ordered by relative path (Ordinal, '/'-normalised): append UTF-8 relative path, 0x00, file SHA-256 bytes
    ...
    return Convert.ToHexString(...);
}
```
Streaming (no whole-file reads into memory). `LocalSyncState`: add `public string LastSyncedPayloadSha256 { get; set; } = string.Empty;`. `CloudSyncService` sets it (computed after the operation, via the adapter from the registry) at the end of a successful `DownloadLatestAsync`, `DownloadLatestAvailableAsync` (after import), and `UploadCurrentAsync`.
**Tests:** same files in different enumeration order → same hash; one byte changed → different; Directory vs FileSet relative paths.

### T05.4 `PlaySessionService` (`App/Services/IPlaySessionService.cs`, `App/Infrastructure/PlaySessionService.cs`, partials `.Prepare.cs` / `.Finish.cs` if > 300 lines)

Types (in `Domain/PlayTypes.cs`):
```csharp
public enum PlayStage { CheckingCloud, Downloading, Locking, Launching, WaitingForGame, Playing, WaitingForSaves, Uploading, Releasing, Done }
public enum PlayOutcome { Launched, NotConnected, BlockedSomeonePlaying, BlockedConflict, NeedsTakeOverConfirmation,
                          NeedsFirstPublishConfirmation, NeedsReplaceUnsyncedConfirmation, LaunchFailed, Failed }
public sealed record PlayOptions(bool AllowTakeOver = false, bool ConfirmedFirstPublish = false, bool ConfirmedReplaceUnsynced = false);
public sealed record PlayPrepareResult(PlayOutcome Outcome, CloudSessionLock? Lock = null, string? Detail = null);
public enum FinishOutcome { Uploaded, NoChanges, UploadFailedLockKept, ConflictLockKept, LockReleaseFailed }
public sealed record PlayFinishResult(FinishOutcome Outcome, string Message, int? VersionNumber = null);
```
Interface:
```csharp
Task<PlayPrepareResult> PrepareAndLaunchAsync(GameWorld world, IGameDefinition game, PlayOptions options, IProgress<PlayStage> progress, CancellationToken ct = default);
Task<PlayFinishResult> FinishAfterExitAsync(GameWorld world, IGameDefinition game, IProgress<PlayStage> progress, CancellationToken ct = default);
```
Constructor deps: `ICloudSyncService`, `IGameLauncherService`, `ILocalSyncStateService`, `IProcessDetectionService`, `IAppLogger`. Messages carry **no UI wording** beyond `Detail` technical text; the VM maps outcomes to user text.

`PrepareAndLaunchAsync` (progress reported at each stage):
1. `CheckingCloud`: `status = await sync.RefreshStatusAsync(world)`.
2. `!status.Connection.IsConnected` → `NotConnected`.
3. `SomeonePlaying` → `BlockedSomeonePlaying(Lock = status.SessionLock)`.
4. foreign **expired** lock (`status.SessionLock` non-null, not own, expired) and `!AllowTakeOver` → `NeedsTakeOverConfirmation(Lock)`.
5. `Conflict` → `BlockedConflict`.
6. `ConnectedNoCloudSave` → if `!ConfirmedFirstPublish` → `NeedsFirstPublishConfirmation`; else `Uploading` stage: `UploadCurrentAsync`; failure → `Failed(Detail=message)`; success → re-run from step 1 once.
7. `CloudNewer`: if `status.LocalState.LocalBaseVersionNumber is null` and `!ConfirmedReplaceUnsynced` → `NeedsReplaceUnsyncedConfirmation`; else if `processDetection.IsGameRunning(game)` → `Failed("Game is running")`; else `Downloading`: `DownloadLatestAsync(world)`; failure → `Failed`.
8. `Locking`: `StartSessionAsync(world, options.AllowTakeOver)`; failure with `SomeonePlaying` → `BlockedSomeonePlaying` (re-read the lock for the name), other failure → `Failed`.
9. `Launching`: `launcher.LaunchAsync(game)`; failure → `EndSessionAsync(world)` (release) then `LaunchFailed(Detail)`.
10. `Launched`.

`FinishAfterExitAsync`:
1. `WaitingForSaves`: poll every 1 s until `UtcNow - max(LastWriteTimeUtc of payload files) >= game.SaveSettleDelay` or `SettleCap` elapsed.
2. Compute `PayloadHasher`; if equal to `LocalSyncState.LastSyncedPayloadSha256` (non-empty) → `Releasing`: `EndSessionAsync`; return `NoChanges` (or `LockReleaseFailed` if it fails).
3. `Uploading`: `UploadCurrentAsync(world)` with retries: attempts at t=0, +5 s, +15 s, +45 s (`Task.Delay`, cancellation-aware). Success → `Uploaded(version)` (upload already clears our lock). A `Conflict` state result → `ConflictLockKept` (no retry). After the last failure → `UploadFailedLockKept`.
Expose the delays as an injectable `IReadOnlyList<TimeSpan> RetryDelays` constructor parameter with the default above so tests run instantly.

**Tests (`PlaySessionServiceTests`, FakeCloudProvider + stub launcher/process detection + `TestPathProvider`):** one test per `PlayOutcome`; `CloudNewer` triggers a download before locking (assert call order `Download` → `WriteLock` → `Launch`); launch failure releases the lock; first-publish and replace-unsynced require confirmation flags; finish: no-change skip releases without uploading; upload fails 4× → `UploadFailedLockKept` and the lock still exists; second call succeeds; settle wait returns immediately when files are old; settle wait honours the cap (use a tiny cap via an overload/parameter for the test).

### T05.5 View model (`MainWindowViewModel.Play.cs`, new; trim `.CloudCommands.cs` and `.GameMonitor.cs`)
- Inject `IPlaySessionService` (constructor).
- State: `[ObservableProperty] string playStageText`, `[ObservableProperty] bool isPlaying`, `[ObservableProperty] bool hasPendingUpload`, fields `DateTimeOffset? launchRequestedAt`, `DateTimeOffset lastHeartbeatAt`, `bool isFinishing`, `bool isHeartbeatInFlight`.
- Computed `PlayButtonText` (raise it from the handlers of `IsBusy`, `IsGameRunning`, `CloudStatus`, `IsPlaying`, `HasPendingUpload`, `SelectedWorld`): `HasPendingUpload` → "Retry sharing"; `IsPlaying` → "Playing…"; busy → "Working…"; `CloudStatus.State == SomeonePlaying` and lock foreign → `$"{player} is playing"`; `Worlds.Count == 0 && CloudStatus?.LatestVersion is not null` → "Get shared world"; else "Play".
- `PlayCommand` (replaces `StartGameCommand`; delete `StartGameAsync`, keep its "game already running" branch semantics inside `PlayAsync`): 
  1. If no local world and a cloud version exists → run the existing no-local-world download path (`DownloadCloudWithoutLocalWorldAsync` + `RefreshAsync`) and return.
  2. `UpdateGameStatus()`; if the game is already running → start/keep the session as the old branch did (`StartSessionAsync(world, allowTakeOver:false)` then `hasObservedGameRunningDuringSession = true`).
  3. Else `RunBusyAsync` → call `PrepareAndLaunchAsync(world, _activeGame.Current, options, progress, ct)` where `progress` sets `PlayStageText` via the stage → text map (`CheckingCloud` "Checking the cloud…", `Downloading` "Downloading the latest world…", `Locking` "Locking the world…", `Launching` $"Launching {ActiveGameName}…", `WaitingForSaves` "Waiting for saves to finish…", `Uploading` "Sharing your progress…", `Releasing` "Releasing the session…").
  4. Map outcomes (single `switch` in this file only):
     - `Launched` → `launchRequestedAt = Now; IsPlaying = true; hasObservedGameRunningDuringSession = false;` status/activity/toast (existing wording style).
     - `BlockedSomeonePlaying` → `_dialogService.ShowError($"{ActiveGameName} is in use", $"{lock.PlayerName} has been hosting since {lock.StartedAtUtc.ToLocalTime():HH:mm} (from v{lock.BasedOnVersionNumber}). Try again when they have finished.")`.
     - `NeedsTakeOverConfirmation` → `Confirm("Take over the session?", $"{player}'s session expired {age}. If they are still playing, taking over can lose progress. Continue?")` → if yes re-run with `AllowTakeOver: true`.
     - `NeedsFirstPublishConfirmation` → `Confirm("Share this world?", "No shared version exists yet. SaveHarbor will upload your current world as version 1 and then start the game.")` → re-run with `ConfirmedFirstPublish: true`.
     - `NeedsReplaceUnsyncedConfirmation` → `Confirm("Replace your local world?", "Your local copy of this world was never synced. The group's latest version will replace it. A backup is created first.")` → re-run with `ConfirmedReplaceUnsynced: true`.
     - `BlockedConflict` → `ShowError("Sync conflict", "Your local copy and the cloud have diverged. Nothing was changed. Use Advanced → Backups to review.")`.
     - `NotConnected` → toast + call `ConnectCloudCommand` once, then retry the flow once on success.
     - `LaunchFailed` / `Failed` → status + activity + toast + dialog with `Detail`.
- `GameMonitor` changes (tick handler, still every 5 s, no new timers):
  - While running and own lock: `observed = true`; heartbeat when `Now - lastHeartbeatAt >= HeartbeatInterval && !isHeartbeatInFlight` → fire-and-forget `SendHeartbeatAsync()` (sets `isHeartbeatInFlight`, calls `_cloudSyncService.HeartbeatAsync`; on a takeover result show one persistent warning toast and set `HasPendingUpload = false`, `IsPlaying = false`; wrap in try/catch → `_errorHandler.Handle`).
  - Not running, `IsPlaying`, never observed, and `Now - launchRequestedAt > GameStartTimeout` → `EndSessionAsync`, `IsPlaying = false`, toast "`{ActiveGameName}` did not start. Session released. If Steam is updating the game, press Play again when it has finished."
  - Was running and now closed with own lock → `FinishAfterExitAsync` (replaces `AutoEndCloudSessionAfterGameClosedAsync`; keep the `isAutoEndingSession` guard as `isFinishing`). Map `FinishOutcome`: `Uploaded` → toast Success "Progress shared as v{n}"; `NoChanges` → toast Info "No changes to share. Session released."; `UploadFailedLockKept`/`ConflictLockKept` → `HasPendingUpload = true` (this drives the persistent banner in T05.7) + toast Error; in every case set `IsPlaying = false` (the banner, not `IsPlaying`, carries the "lock still held" state).
- `RetryUploadCommand` (`[RelayCommand]`, enabled when `HasPendingUpload && !IsBusy`): `RunBusyAsync("Sharing your progress…")` → `FinishAfterExitAsync` again; clear `HasPendingUpload` on `Uploaded`/`NoChanges`.
- `NotifyCommandStates()`: replace `StartGameCommand` with `PlayCommand`, add `RetryUploadCommand`, `SwitchGameCommand`.
- Manual buttons (Advanced): Start session now calls `StartSessionAsync(world, allowTakeOver)` with the same take-over `Confirm` when `CloudStatus.SessionLock` is foreign and expired.
- `CloudSessionText` (ComputedProperties): lock null → "Free"; own → "You are hosting"; foreign active → `$"{player} · since {start.ToLocalTime():HH:mm}"`; foreign expired → `$"Expired · {player}"`. `CloudSessionTooltip` keeps its text.

### T05.6 Shutdown guard and next-start recovery
- `MainWindow.xaml.cs`: `Closing += (_, e) => e.Cancel = !viewModel.ConfirmClose();` — `MainWindowViewModel.ConfirmClose()` returns `true` unless `IsPlaying && HasOwnCloudSession() && IsGameRunning`, in which case `_dialogService.Confirm("Game is still running", "If you close SaveHarbor now, your progress will not be shared automatically. Close anyway?")`. Also block while `isFinishing` (same style of confirm: "Sharing is in progress…").
- `InitializeAsync` (after `RefreshAsync`): if `SelectedWorld is not null && HasOwnCloudSession() && !IsGameRunning`: compute `PayloadHasher` vs `LocalState.LastSyncedPayloadSha256`; different → `HasPendingUpload = true` and a toast "You have unshared progress from your last session."; same → `EndSessionAsync` silently + activity entry.

### T05.7 UI (`Views/Controls/ActionsSectionView.xaml`, `ui-text.en.json`)
- Play card: the primary button (`Style=PlayButton`, `Command=PlayCommand`, `Content={Binding PlayButtonText}`, full width of its column, `MinHeight=44`); beneath it `PlayStageText` (`MutedText`, trimmed), and the existing hint text bound to `PlayHintText`.
- Pending-upload banner (visible when `HasPendingUpload`): a `Border` using `ToneWarnBackgroundBrush`/`ToneWarnBorderBrush` with text "Your progress is not shared yet." and a `SecondaryButton` "Retry" bound to `RetryUploadCommand`.
- Wrap the existing Local files / Cloud / Session blocks in `<Expander Header="Advanced" IsExpanded="False">` (header text via UiText `Actions.Advanced`; give the `Expander` a minimal style in `Controls.xaml` using theme brushes). No other restructuring.
- Add UiText keys: `Actions.Advanced`, `Actions.PendingUpload`, `Actions.Retry`, tooltip for each.
- Disabled Play must explain why: bind the Play button `ToolTip` to a VM `PlayDisabledReason` (e.g. "Select a world first.", "Close {game} first.", "{player} is playing.", "Connect Google Drive first.") with `ToolTipService.ShowOnDisabled="True"`.

### T05.8 Verification (CP6)
- Build + tests green (report counts).
- Manual smoke with `LocalTest` (two "friends" simulated by editing `MachineName` in `cloud-test/<game>/worlds/<id>/locks/active-session.json`; `build-and-verify` steps 1–6) for **both** games if both are installed, else for the installed one; list what was not performed.
- Lock timing check from the log: heartbeat entries appear about every 5 min while the game runs (shorten `HeartbeatInterval` temporarily via a local edit **only for the check and revert it**, or state it was not performed).
- Report remaining risks: Windrose process names still substring-based; Dragonwilds assumptions A1–A4 unverified until Phase 0 results exist; Steam Cloud interaction unverified.

### CHECKPOINT CP6 → stop, final report (summarise all checkpoints, open risks, and the list of owner actions: install SDK, Phase 0 checks, font files).
