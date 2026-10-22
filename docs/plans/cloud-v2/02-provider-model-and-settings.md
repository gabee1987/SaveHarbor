# 02 — Provider Model and Settings

How the app chooses a cloud provider at runtime, how "shared folder" generalises to "group", how local state is kept apart per provider, and the new device identity. Pure C# changes; no Supabase calls yet.

## 1. Runtime provider selection

Today `App.xaml.cs:63-69` resolves `ICloudProvider` once from `CloudProviderOptions.Provider` (appsettings). Target:

- `AppSettings.CloudProvider` (string: `"GoogleDrive"` | `"Supabase"`; empty = not chosen yet). Add `CloudProviderKind.Supabase = "Supabase"`.
- **Default for new installs:** empty → the first-run flow (plan 04 §2) asks. **Default for existing installs:** the settings migration sets `GoogleDrive` when `cloud-provider-settings.json` contains any shared folder, so nobody's setup breaks on update.
- `appsettings.json` `Provider: LocalTest` keeps its meaning for developers: it **forces** `FolderCloudProvider` and hides the provider switch. `Provider: GoogleDrive` in appsettings becomes a no-op default; remove the ambiguity by renaming the appsettings key to `DeveloperProviderOverride` (`""` | `"LocalTest"`), with a one-line migration note in `CloudProviderOptions`.
- New `Infrastructure/CloudProviderSelector.cs` (`sealed class CloudProviderSelector : ICloudProvider, ICloudProviderSelector`) registered as the `ICloudProvider` singleton. It holds the three concrete providers, exposes `Active`, `ActiveKind`, `Available` (list with display names), `SwitchAsync(kind)` and an event `ActiveChanged`. Every `ICloudProvider` member forwards to `Active`. This keeps `CloudSyncService`, `CloudProviderSettingsService` and the view model unchanged in their dependencies.
- `SwitchAsync` order: cancel realtime subscriptions (plan 05), `DisconnectAsync` nothing (sessions stay cached per provider), set `AppSettings.CloudProvider`, raise `ActiveChanged`. The view model handles `ActiveChanged` like a game switch: clear `CloudStatus`/overview, re-run `PromptCloudSetupIfNeededAsync`, refresh.

`Services/ICloudProviderSelector.cs`:

```csharp
public interface ICloudProviderSelector
{
    string ActiveKind { get; }
    IReadOnlyList<CloudProviderChoice> Available { get; }   // record CloudProviderChoice(string Kind, string DisplayName, string Tagline)
    bool IsDeveloperOverride { get; }
    Task SwitchAsync(string kind, CancellationToken cancellationToken = default);
    event EventHandler? ActiveChanged;
}
```

## 2. Provider capabilities (so the UI stops assuming Google Drive)

Add to `ICloudProvider`:

```csharp
CloudProviderCapabilities Capabilities { get; }
```

```csharp
public sealed record CloudProviderCapabilities(
    bool HasSharedFolders,        // Drive, LocalTest
    bool HasGroups,               // Supabase
    bool HasAtomicLocks,          // Supabase: acquire returns the winner
    bool SupportsHeartbeat,       // Supabase (Drive: rewrite lock file, plan 05 §3)
    bool SupportsRealtime,        // Supabase
    bool HasRecycleBin,           // Drive
    long MaxArchiveBytes);        // Supabase 45 MB; Drive/LocalTest long.MaxValue
```

Texts that mention Google Drive by name (`Settings.SharedFolder`, `Settings.KeepSharedVersionsHint`, the setup prompt in `MainWindowViewModel.CloudCommands.cs:15-17`, `CloudSetup.*`) become provider-conditional: keep the Drive wording under the existing keys and add `Settings.Group*`, `CloudSetup.Group*` keys for Supabase (plan 04 §6). Visibility binds to `Capabilities.HasSharedFolders` / `HasGroups` through two new computed properties on the main view model (`ShowSharedFolderSettings`, `ShowGroupSettings`).

## 3. Interface extensions

Two additions to `ICloudProvider` (default implementations in Drive/LocalTest keep today's behaviour):

1. **Atomic lock acquisition.** Today `WriteSessionLockAsync` is fire-and-forget and `CloudSyncService.StartSessionAsync` decides the winner before writing. Add:
   ```csharp
   Task<CloudLockAttempt> TryAcquireSessionLockAsync(GameId game, CloudSessionLock requested, CancellationToken cancellationToken = default);
   Task<CloudSessionLock?> HeartbeatSessionLockAsync(GameId game, string worldId, string lockId, TimeSpan ttl, CancellationToken cancellationToken = default);
   ```
   `record CloudLockAttempt(bool Acquired, CloudSessionLock Current)`. Drive/LocalTest implement `TryAcquire` as write → delay (1.5 s, configurable, 0 in tests) → re-read → compare `LockId` (the mitigation from the `cloud-sync-and-locking` skill). Supabase calls the RPC. `StartSessionAsync` switches to `TryAcquireSessionLockAsync` for every provider; `WriteSessionLockAsync` is removed once nothing else calls it.
2. **One-call overview.** `ListWorldManifestsAsync` already exists; add
   ```csharp
   Task<CloudGroupSnapshot> GetSnapshotAsync(GameId game, CancellationToken cancellationToken = default);
   ```
   `record CloudGroupSnapshot(DateTimeOffset ServerTimeUtc, IReadOnlyList<CloudWorldManifest> Manifests, IReadOnlyDictionary<string, CloudSessionLock> LocksByWorldId, IReadOnlyList<CloudMember> Members)`. Drive/LocalTest compose it from the existing calls (no speed-up, but one code path); Supabase uses `get_group_snapshot`. `CloudSyncService.GetOverviewAsync` and `RefreshStatusAsync` read from the snapshot; `RefreshStatusAsync` takes an optional pre-fetched snapshot so a refresh of 8 worlds is one request, not 24.

`CloudSessionLock` gets `DeviceId` (string, empty in legacy locks) and `SchemaVersion = 2`. `CloudVersionMetadata` gets `UploaderDevice` label semantics unchanged (`UploaderMachine` stays as the field name for Drive compatibility; Supabase maps it to `uploader_device`).

## 4. Setup generalisation: folders and groups

Keep `ICloudSetupService` (folder-based, Drive + LocalTest) untouched. Add a sibling for groups:

```csharp
public interface ICloudGroupService
{
    bool IsSignedIn { get; }
    string? SignedInEmail { get; }
    Task<CloudAuthResult> RequestSignInCodeAsync(string email, CancellationToken ct = default);
    Task<CloudAuthResult> VerifySignInCodeAsync(string email, string code, CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);

    IReadOnlyList<CloudGroupSummary> KnownGroups { get; }                 // cached from last ListGroupsAsync
    Task<IReadOnlyList<CloudGroupSummary>> ListGroupsAsync(CancellationToken ct = default);
    Task<CloudGroupSummary> CreateGroupAsync(string name, string displayName, CancellationToken ct = default);
    Task<CloudGroupSummary> JoinGroupAsync(string inviteCode, string displayName, CancellationToken ct = default);
    Task LeaveGroupAsync(string groupId, CancellationToken ct = default);
    Task<string> GetInviteCodeAsync(string groupId, CancellationToken ct = default);
    Task<string> RotateInviteCodeAsync(string groupId, CancellationToken ct = default);

    string? GetActiveGroupId(GameId game);
    bool HasGroupConfigured(GameId game);
    Task SetActiveGroupAsync(GameId game, string groupId, bool applyToAllGames, CancellationToken ct = default);
}
```

`record CloudGroupSummary(string Id, string Name, string MyRole, int MemberCount, DateTimeOffset CreatedAtUtc)`; `record CloudAuthResult(bool IsSuccess, string Message, bool NeedsCode = false)`.

Implementation lives in `Infrastructure/Cloud/Supabase/SupabaseGroupService.cs` (plan 03 §3). The view model's `SetupCloudFolderCommand` becomes `SetupCloudCommand`: it opens `CloudFolderSetupWindow` when `Capabilities.HasSharedFolders`, otherwise `CloudGroupSetupWindow` (plan 04 §3).

Settings storage: `LocalCloudProviderSettings` gains `ActiveGroups` (`Dictionary<string game, string groupId>`) and `SchemaVersion = 3`. `SavedFolders` and `SharedFolders` stay for Drive.

## 5. Provider-scoped local state

`LocalSyncState` files live at `%LOCALAPPDATA%\SaveHarbor\sync-state\<game>\<worldId>.json` and record `LocalBaseVersionNumber` against **Drive** version numbers. Supabase numbers start at 1 again, so the file must be per provider:

- `IAppDataPathProvider.GetSyncStateRoot(GameId game)` → `GetSyncStateRoot(GameId game, string providerKind)` = `sync-state\<game>\<provider-key>\` with keys `googledrive`, `supabase`, `localtest`.
- `LocalJsonSyncStateService` receives `ICloudProviderSelector` and uses `ActiveKind`.
- Migration (`Infrastructure/Migrations/SyncStateProviderScopeMigrator.cs`, run once at startup like `LegacyLayoutMigrator`): move `sync-state\<game>\*.json` into `sync-state\<game>\googledrive\`. Idempotent; logs count; never overwrites an existing target.
- Also scope the snapshot cache (plan 05 §1): `cloud-cache\<provider-key>\<game>.json`.

Backups are provider-agnostic and stay where they are. `BackupManifest` gets an optional `CloudProvider` string for display only.

## 6. Moving a world from Google Drive to Supabase (helper, not a migration)

No history is copied. In the Sharing view, when the active provider is Supabase and the selected world has Drive sync state with a base version but no Supabase version, show the hint "This world was shared on Google Drive before. Upload it once to share it with this group." The normal upload does the rest. The reverse direction needs nothing.

## 7. Settings and defaults touched

| Setting | Change |
|---|---|
| `AppSettings.CloudProvider` | new, see §1 |
| `AppSettings.DeviceId` | new GUID (`"D"` format) generated on first read when empty; never changes |
| `AppSettings.DeviceLabel` | new, defaults to `Environment.MachineName`, editable in Settings ("This PC's name for friends"), max 32 chars, same normalisation as `PlayerIdentity.Normalize` |
| `AppSettings.CloudVersionRetentionCount` | unchanged semantics; the **effective default** when 0 is provider-specific: Drive keeps "keep all" (today's behaviour), Supabase uses 10 (free-plan storage). Implemented in `CloudSyncService.Retention` via `Capabilities.MaxArchiveBytes == long.MaxValue ? 0 : 10` is too clever; use an explicit `DefaultRetentionCount` on `CloudProviderCapabilities` instead |
| `AppSettings.MinimizeToTrayWhilePlaying` | new, default true (plan 05 §5) |
| `AppSettings.SkippedUpdateVersion` | new (plan 06 §4) |

`IDeviceIdentity` (`Services/IDeviceIdentity.cs`: `string DeviceId`, `string DeviceLabel`) implemented by `Infrastructure/DeviceIdentity.cs` over `IAppSettingsStore`. `CloudSyncService` uses it for locks; `IsOwnLock` = `DeviceId` equal when the lock has one, else `MachineName` equal (legacy Drive locks).

## 8. Files touched (summary)

New: `Services/ICloudProviderSelector.cs`, `Services/ICloudGroupService.cs`, `Services/IDeviceIdentity.cs`, `Domain/CloudProviderCapabilities.cs`, `Domain/CloudLockAttempt.cs`, `Domain/CloudGroupSnapshot.cs`, `Domain/CloudGroupSummary.cs`, `Domain/CloudAuthResult.cs`, `Infrastructure/CloudProviderSelector.cs`, `Infrastructure/DeviceIdentity.cs`, `Infrastructure/Migrations/SyncStateProviderScopeMigrator.cs`.
Changed: `ICloudProvider`, `FolderCloudProvider` (+capabilities, TryAcquire, snapshot), `GoogleDriveCloudProvider.*` (same), `CloudSyncService.Sessions/SessionLocks/Overview/Retention`, `CloudSessionLock`, `LocalCloudProviderSettings`, `AppSettings`, `AppDataPathProvider` + interface, `LocalJsonSyncStateService`, `CloudProviderOptions`, `App.xaml.cs`, `MainWindowViewModel.CloudCommands/ComputedProperties/GameMonitor`, `ui-text.en.json`, tests under `SaveHarbor.Tests/Cloud` and `Support/FakeCloudProvider.cs`.
