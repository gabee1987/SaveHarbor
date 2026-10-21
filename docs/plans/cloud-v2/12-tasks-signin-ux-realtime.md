# 12 — Tasks: Sign-in, Groups, Settings, Speed, Realtime (Plans 04, 05)

Read [10-implementation-handoff.md](10-implementation-handoff.md) first. `App/` = `SaveHarbor.App/`, `Tests/` = `SaveHarbor.Tests/`.

---

## T04 — Sign-in and groups UX (CP3) · plan 04

### T04.1 Provider choice dialog
- `App/Views/Dialogs/CloudProviderChoiceWindow.xaml(.cs)`: two cards (plan 04 §2), "Decide later". Result: `string? Kind`.
- `IDialogService.ChooseCloudProvider(Uri skinDictionary, IReadOnlyList<CloudProviderChoice> choices) : string?`; `WpfDialogService` implements.
- Texts: `CloudProviderChoice.*` keys.

### T04.2 Group setup dialog
- `App/ViewModels/CloudGroupSetupViewModel.cs` (CommunityToolkit, ≤ 300 lines): states `Email`, `Code`, `Group`; properties `Email`, `Code`, `InviteCode`, `GroupName`, `DisplayName`, `ApplyToAllGames`, `Groups` (observable), `Message`, `IsBusy`, `ResendAvailableIn`; commands `SendCodeCommand`, `VerifyCommand`, `ResendCommand`, `UseDifferentEmailCommand`, `UseGroupCommand(group)`, `JoinCommand`, `CreateCommand`, `SignOutCommand`, `CopyInviteCodeCommand`. Depends on `ICloudGroupService`, `IPlayerIdentity`, `GameId`, `bool otherGameHasGroup`.
- `App/Views/Dialogs/CloudGroupSetupWindow.xaml(.cs)`: three panels switched by state; code box = six `TextBox`es or one masked box with digit filtering (choose one masked box with `MaxLength=6`, digits only, paste support); Enter advances; skin dictionary merged like `CloudFolderSetupWindow`.
- `IDialogService.ConfigureCloudGroup(Uri skinDictionary, string gameDisplayName, CloudGroupSetupViewModel viewModel) : CloudGroupSetupResult?`.
- Texts: `CloudGroup.*` keys (plan 04 §6).

### T04.3 View model flow
- `MainWindowViewModel.CloudCommands.cs`: rename `PromptCloudFolderSetupIfNeededAsync` → `PromptCloudSetupIfNeededAsync` implementing plan 04 §2 steps 1–4; `SetupCloudFolderCommand` → `SetupCloudCommand` choosing the dialog by `Capabilities`; `ConnectCloudCommand`: for Supabase, if `Status.NeedsSetup` open the group dialog instead of showing a warning.
- After a successful group selection: `RefreshCloudStatusAsync(false)`, `RefreshCloudOverviewAsync()`, toast "Connected to <group>".
- `SettingsViewModel`: `ProviderChoices`, `SelectedProviderKind` (confirm on change, then `ICloudProviderSelector.SwitchAsync`), `SignedInEmail`, `ActiveGroupName`, `IsGroupOwner`, `InviteCode` (loaded on demand), commands `ChangeGroup`, `SignOut`, `ShowInviteCode`, `CopyInviteCode`, `RotateInviteCode`, `LeaveGroup`, `CleanUpUnusedFiles`, property `DeviceLabel` (saved like `PlayerName`), `StorageUsedText`.

### T04.4 Views
- `ShellSharingView.xaml`: hero states from plan 04 §4 bound to new computed properties `SharingHeroText`, `SharingActionText`, `SharingActionCommand`; per-world lock lines "Lock expires in N min" / "expired at … — you can take over".
- `ShellSettingsView.xaml`: SHARING section from plan 04 §5; Drive rows visible when `ShowSharedFolderSettings`, group rows when `ShowGroupSettings`.
- `ShellFooterView.xaml`: unchanged here (update text comes in T06).
- Texts: `Settings.Sharing*`, `Sharing.State.*`.

### T04.5 Orphan clean-up and storage used
`ICloudSyncService.GetStorageUsageAsync(GameId)` → `CloudStorageUsage(long UsedBytes, long? QuotaBytes)`; Supabase sums `archive_size_bytes` over the group (one PostgREST aggregate call `select=archive_size_bytes.sum()`), quota 1 GB from options (`FreePlanStorageBytes`); Drive returns null quota (hidden). `CleanUpUnusedFilesAsync(GameId)` → list objects, subtract version paths, confirm, delete; returns count and bytes.

### T04.6 Tests
`ViewModels/CloudGroupSetupViewModelTests` (state machine with a fake `ICloudGroupService`: send → code; wrong code keeps state; join normalises the code; create shows invite code; apply-to-all default), `Settings/CloudProviderSettingsMigrationTests`, `Localization/UiTextKeysTests` for the new keys.

**Done when:** build clean; tests pass; **owner step:** real URL/key into the local `appsettings.json`, then the two-user smoke test steps 1–4 and 6–7 of plan 09 §2. Report anything that needed a [verify].

### CHECKPOINT CP3 → stop, report. (MVP)

---

## T05a — Speed and convenience (CP6) · plan 05 §1–§5, §7

Order within the task: cache → coalesced refresh → no-op skip → upload on close → tray → single instance → keyboard. Each sub-task leaves the app working.

### T05a.1 Snapshot cache
`Infrastructure/Cloud/CloudSnapshotCache.cs(IAppDataPathProvider paths)`: `Load(providerKey, game) : CachedSnapshot?` (`CloudGroupSnapshot Snapshot, DateTimeOffset CachedAtUtc`), `Save(...)`. Path `cloud-cache\<provider>\<game>.json`. `MainWindowViewModel.InitializeAsync`: after local scan, load cache → `ApplyOverviewFromSnapshot(cached, isStale: true)` → background `RefreshCloudAsync("startup")`. UI: an "as of HH:mm" chip on the Sharing hero while `IsCloudStale`. `CloudSyncService` never reads the cache.

### T05a.2 Coalesced refresh
`MainWindowViewModel.SyncOverview.cs`: `RefreshCloudAsync(string reason)` fetches one snapshot via `_cloudSyncService.GetOverviewAsync` (which already takes the snapshot path from T02.6) and updates `CloudStatus` for the selected world from the same snapshot; replace every paired `RefreshCloudStatusAsync` + `RefreshCloudOverviewAsync` call with it; debounce 250 ms using a `CancellationTokenSource` swap.

### T05a.3 No-op upload skip
`CloudSyncService.Transfers.UploadCurrentAsync`: before zipping, `LocalChangeDetector.HasChangesAsync(localState, world, adapter)`; if no changes and `LocalBaseVersionNumber == latest` → `CloudSyncResult(true, UpToDate, "Nothing changed since v{n}; nothing to upload.")`. Test in `Cloud/UploadSkipTests`.

### T05a.4 Upload on game close
`MainWindowViewModel.GameMonitor.AutoEndCloudSessionAfterGameClosedAsync` → plan 05 §4: settle wait (`IGameSaveAdapter` last-write polling, 3 s stable, 30 s max) → `UploadCurrentAsync` → `EndSessionAsync` → toasts. Failure path: release the lock, set `LocalSyncState.PendingUploadSinceUtc`, show the persistent banner (`ShellHeroView` region bound to `PendingUploadText`, commands `RetryPendingUploadCommand`, `DiscardPendingUploadCommand`). On startup, if any sync state has `PendingUploadSinceUtc` and the game is not running → banner. Tests: `Cloud/PendingUploadTests` for the state flag and clearing.

### T05a.5 Tray while playing
- `App/Views/Behaviors/TrayIcon.cs`: P/Invoke `Shell_NotifyIconW` (`NIM_ADD/MODIFY/DELETE`, `NIF_MESSAGE|NIF_ICON|NIF_TIP|NIF_INFO`), `WM_APP + 1` callback through `HwndSource.AddHook`, context menu as a WPF `ContextMenu` opened at the cursor; icon from the window's icon or a generated 16×16 bitmap when none (until the owner supplies an `.ico`). Methods: `Show(tooltip)`, `Hide()`, `Balloon(title, text)`.
- `MainWindowViewModel.Launch.cs`: after a successful launch and when `MinimizeToTrayWhilePlaying`, raise `MinimizeToTrayRequested`; `MainWindow` hides and shows the tray icon; restore on double-click, on game exit (after the upload), or from the menu. Balloons for "Shared v{n}" and failures.
- Settings toggle `Settings.MinimizeToTray` + hint.

### T05a.6 Single instance and protocol handler
- `App/Infrastructure/SingleInstanceGuard.cs`: `Mutex("Local\\SaveHarbor.App")` + `EventWaitHandle("Local\\SaveHarbor.App.Activate")`; second instance writes its args to `%LOCALAPPDATA%\SaveHarbor\activate.args` and signals; the first instance reads, deletes, and handles (`--game`, `saveharbor://join/<code>`).
- `App/Infrastructure/ProtocolRegistration.cs`: `HKCU\Software\Classes\saveharbor` (`URL Protocol`, `shell\open\command = "<exe>" "%1"`), `Register()`/`Unregister()`; called from Velopack hooks (T06) and from Settings "Register invite links" as a fallback.
- `Program.cs` exists from T06; if T05a runs before T06, create it now with the single-instance part only.

### T05a.7 Keyboard and pre-flight
Key bindings in `GameShellView.xaml` (`Enter` → Play on the list, `Ctrl+U`, `Ctrl+R`); Play button tooltip `PlayPreflightText` computed from the cached status ("Cloud has v13 by Anna, you have v12: download, lock, launch"). `--provider` CLI argument in `AppOptionsLoader.ReadProviderArgument`.

**Done when:** build clean; tests pass; manual: cold start shows cached state immediately; Play → tray → close game → upload → balloon → window restores; second start of the exe activates the first; `start saveharbor://join/TESTCODE22` opens the group dialog at step 3.

### CHECKPOINT CP6 → stop, report.

---

## T05b — Realtime (CP7) · plan 05 §6

### T05b.1 Client
`Infrastructure/Cloud/Supabase/SupabaseRealtimeClient.cs(SupabaseOptions options, SupabaseAuthClient auth, IAppLogger logger)`: `ConnectAsync(groupId, userId, presenceState)`, `DisconnectAsync()`, events `ChangeReceived(table, eventType)`, `PresenceChanged(IReadOnlyList<PresenceEntry>)`, `StateChanged(Connected/Polling)`. `ClientWebSocket`, JSON frames per **V2**, join payload from plan 05 §6, heartbeat 30 s, `access_token` message on token refresh, reconnect backoff 1→60 s, cancellation on dispose. Split into `.Protocol.cs` if over 300 lines.

### T05b.2 Wiring
- `MainWindowViewModel.SyncOverview.cs`: when the active provider `SupportsRealtime` and a group is set → connect; on `ChangeReceived` debounce 500 ms → `RefreshCloudAsync("realtime")`; toasts for version insert ("<player> shared v{n} of <world>") and lock insert/delete; on `StateChanged(Polling)` start a 60 s poll timer, stop it on `Connected`. Disconnect on provider/game/group switch and on exit.
- Presence state: `{ display_name, device_label, game, playing_world_id }` updated on Play/end.
- Sharing hero: "live" / "polling" dot; member list shows an "online" dot from presence.
- Game monitor: cloud polling of the status is removed from the 5 s tick for Supabase (process detection stays).

### T05b.3 Tests
`Cloud/Supabase/SupabaseRealtimeClientTests` with an in-process `System.Net.WebSockets` echo server (`HttpListener` + `AcceptWebSocketAsync`) scripted to send a join reply, a `postgres_changes` event, and a close; asserts events, heartbeat frames and reconnect.

**Done when:** two apps on two Windows users see each other's upload and lock within 2 s; killing the network shows "polling" and recovers.

### CHECKPOINT CP7 → stop, report.
