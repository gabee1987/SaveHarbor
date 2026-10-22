# 11 — Tasks: Backend, Provider Model, Supabase Provider (Plans 01, 02, 03)

Read [10-implementation-handoff.md](10-implementation-handoff.md) first. Paths are relative to the repo root; `App/` means `SaveHarbor.App/`, `Tests/` means `SaveHarbor.Tests/`.

---

## T01 — Supabase backend files (CP0) · plan 01

No C# in this task. Nothing here runs against the hosted project; the owner applies it.

### T01.1 Folder and migration
Create `supabase/migrations/20261020000000_initial.sql` with, in this order: extension; tables (`groups`, `group_members`, `worlds`, `world_versions`, `session_locks`, `app_meta`) exactly as plan 01 §2; `touch_updated_at` trigger; `is_group_member`; view `group_summaries`; RLS enable + policies (plan 01 §3); `worlds_guard_latest` trigger; functions `generate_invite_code`, `create_group`, `join_group`, `leave_group`, `rotate_invite_code`, `get_invite_code`, `rename_group`, `set_display_name`, `get_group_snapshot`, `publish_world_version`, `acquire_session_lock`, `heartbeat_session_lock`, `release_session_lock`, `remove_world` (plan 01 §4); storage policies on `storage.objects` for bucket `saves`; realtime publication. Every function: `security definer`, `set search_path = public`, `revoke all on function ... from public`, `grant execute ... to authenticated`.

Shapes to get right:
- `get_group_snapshot` returns `jsonb_build_object('server_time', now(), 'members', (...), 'worlds', (select coalesce(jsonb_agg(...), '[]'::jsonb) from worlds w where w.group_id = p_group and w.game = p_game))` where each world object embeds `latest` (`to_jsonb(v)` of the row with `version_number = w.latest_version_number`, or `null`) and `lock` (`to_jsonb(l)` or `null`).
- `acquire_session_lock` in plpgsql:
  ```sql
  insert into session_locks (...) values (...)
  on conflict (world_uuid) do update set lock_id = excluded.lock_id, user_id = excluded.user_id, player_name = excluded.player_name,
      device_id = excluded.device_id, device_label = excluded.device_label, started_at = excluded.started_at,
      last_heartbeat_at = excluded.last_heartbeat_at, expires_at = excluded.expires_at,
      based_on_version_number = excluded.based_on_version_number, based_on_version_id = excluded.based_on_version_id
    where session_locks.expires_at < now() or session_locks.device_id = excluded.device_id
  returning * into v_row;
  if not found then select * into v_row from session_locks where world_uuid = p_world; end if;
  return v_row;
  ```
- `publish_world_version`: `insert into worlds ... on conflict (group_id, game, world_id) do update set world_name = excluded.world_name returning id into v_world`; then `select latest_version_number into v_latest from worlds where id = v_world for update`; conflict check; insert version; `perform set_config('saveharbor.rpc', 'on', true); update worlds set latest_version_number = v_new where id = v_world;`.

### T01.2 Seed and tests
- `supabase/seed.sql`: two auth users (`test-a@example.invalid`, `test-b@example.invalid`) inserted the way the Supabase CLI docs show for local seeds; group `TEST_GROUP` with invite code `TESTCODE22`, both members.
- `supabase/tests/version_cas.sql`, `lock_race.sql`, `rls_isolation.sql` as plan 09 §4, pgTAP style (`begin; select plan(n); ... select * from finish(); rollback;`), impersonating users with `set local role authenticated; select set_config('request.jwt.claims', '{"sub":"<uuid>","role":"authenticated"}', true);`.
- `supabase/scripts/smoke-test.ps1`: end-to-end check of the local stack, including a parallel two-user lock race (the `Testing` folder is git-ignored).

### T01.3 Docs
`docs/runbooks/supabase.md` with the items from plan 07 §7 and the owner's one-time setup list from plan 01 §1 (dashboard settings, e-mail templates, bucket).

**Owner steps (write them in the report):** `supabase init` (only if `supabase/config.toml` does not exist; keep the generated file), `supabase link --project-ref <ref>`, `supabase db push`, dashboard settings from plan 01 §1, `supabase start && supabase test db` locally to run the pgTAP tests.

**Done when:** the three SQL files parse (`supabase db lint` if the CLI is installed; otherwise careful review), the runbook exists, and the report lists the owner steps.

### CHECKPOINT CP0 → stop, report.

---

## T02 — Provider model (CP1) · plan 02

### T02.1 Domain additions (`App/Domain/`)
- `CloudProviderCapabilities.cs`: record from plan 02 §2 plus `int DefaultRetentionCount`.
- `CloudLockAttempt.cs`: `sealed record CloudLockAttempt(bool Acquired, CloudSessionLock Current)`.
- `CloudGroupSnapshot.cs`: `sealed record CloudGroupSnapshot(DateTimeOffset? ServerTimeUtc, IReadOnlyList<CloudWorldManifest> Manifests, IReadOnlyDictionary<string, CloudSessionLock> LocksByWorldId, IReadOnlyList<CloudMember> Members)`; `sealed record CloudMember(string Id, string DisplayName, string Role)`.
- `CloudSessionLock.cs`: add `public string DeviceId { get; set; } = string.Empty;`, bump default `SchemaVersion` to 2.
- `CloudSyncState.cs`: add `UpdateRequired` (used from T06; adding it now avoids a second enum change).
- `CloudConnectionStatus.cs`: add `bool NeedsSetup = false` (positional, after `Message`) and `string Reason = ""`.
- `CloudProviderKind`: add `Supabase = "Supabase"`.

### T02.2 Settings and identity
- `AppSettings`: `CloudProvider`, `DeviceId`, `DeviceLabel`, `MinimizeToTrayWhilePlaying = true`, `SkippedUpdateVersion`.
- `Services/IDeviceIdentity.cs` + `Infrastructure/DeviceIdentity.cs(IAppSettingsStore settings)`: `DeviceId` generates `Guid.NewGuid().ToString("D")` through `settings.Update` when empty; `DeviceLabel` = normalised `settings.Current.DeviceLabel` or `Environment.MachineName`; reuse `PlayerIdentity.Normalize`.
- `LocalCloudProviderSettings`: `SchemaVersion = 3`, `Dictionary<string, string> ActiveGroups` (OrdinalIgnoreCase). `CloudProviderSettingsService.ReadExistingSettings` tolerates v2 files.
- `CloudProviderOptions`: rename `Provider` → `DeveloperProviderOverride` (default empty); keep reading the old key `Provider` for one release when it equals `LocalTest`; nested `SupabaseOptions` (plan 03 §1) bound in `AppOptionsLoader.LoadCloudProviderOptions`. Update `appsettings.json` accordingly with placeholder URL/key.

### T02.3 Interface changes (`App/Services/ICloudProvider.cs`)
Add `CloudProviderCapabilities Capabilities { get; }`, `TryAcquireSessionLockAsync`, `HeartbeatSessionLockAsync`, `GetSnapshotAsync` (signatures in plan 02 §3). Keep `WriteSessionLockAsync` for now.

### T02.4 FolderCloudProvider and GoogleDriveCloudProvider
- `Capabilities`: Folder → `(HasSharedFolders: true, HasGroups: false, HasAtomicLocks: false, SupportsHeartbeat: true, SupportsRealtime: false, HasRecycleBin: false, MaxArchiveBytes: long.MaxValue, DefaultRetentionCount: 0)`; Drive → same but `HasRecycleBin: true`.
- `TryAcquireSessionLockAsync`: write lock → `await Task.Delay(VerifyDelay)` → re-read → `Acquired = LockId equal`; `VerifyDelay` is a `TimeSpan` constructor parameter defaulting to 1.5 s (tests pass `TimeSpan.Zero`). Lock written includes `DeviceId` from `IDeviceIdentity` (constructor injection; both providers gain the dependency).
- `HeartbeatSessionLockAsync`: read lock; if `LockId` matches, rewrite with `LastHeartbeatAtUtc = now`, `ExpiresAtUtc = now + ttl`; return it; else null.
- `GetSnapshotAsync`: `ListWorldManifestsAsync` + `GetSessionLockAsync` per world → snapshot with `ServerTimeUtc = null`. Put it in a new partial `GoogleDriveCloudProvider.Snapshot.cs` / `FolderCloudProvider.Snapshot.cs`.

### T02.5 Selector and DI
- `Services/ICloudProviderSelector.cs` and `Infrastructure/CloudProviderSelector.cs` (plan 02 §1). Constructor: `(CloudProviderOptions options, IAppSettingsStore settings, FolderCloudProvider folder, GoogleDriveCloudProvider drive, Func<ICloudProvider> supabaseFactory)`; the factory is `() => new NotConfiguredCloudProvider("SaveHarbor Cloud")` until T03 replaces it. `ActiveKind` resolution: developer override → LocalTest; else `settings.Current.CloudProvider`; empty → `NotConfiguredCloudProvider` with `NeedsSetup = true`.
- Legacy default: in `CloudProviderSelector` constructor, if `CloudProvider` is empty and `CloudProviderSettingsService` reports any shared folder → set `GoogleDrive` through `settings.Update` and log at Information.
- `App.xaml.cs`: register `IDeviceIdentity`, `CloudProviderSelector` as itself, `ICloudProviderSelector` and `ICloudProvider` → the same instance.

### T02.6 CloudSyncService
- `StartSessionAsync` uses `TryAcquireSessionLockAsync`; on `!Acquired` return `SomeonePlaying` with `attempt.Current.PlayerName`; on own live lock, the attempt renews it (Supabase) or returns it (Drive). Delete `WriteSessionLockAsync` from the interface and both providers.
- `CloudSyncService.SessionLocks.cs`: `IsOwnLock` prefers `DeviceId`; expiry uses `snapshot.ServerTimeUtc ?? DateTimeOffset.UtcNow` (pass the time in; make the helpers instance methods taking `now`).
- New `HeartbeatSessionAsync(GameWorld world)` on `ICloudSyncService`: loads status, if own lock → `HeartbeatSessionLockAsync(…, TimeSpan.FromSeconds(options.LockTtlSeconds))`; null → return `CloudSyncResult(false, Conflict, "Your session lock expired and another player took over.")`.
- `RefreshStatusAsync(GameWorld world, CloudGroupSnapshot? snapshot = null, …)`: when a snapshot is given, take manifest and lock from it instead of two provider calls. `GetOverviewAsync` fetches one snapshot and passes it to each world's status.
- Lock TTL at acquire: `now.AddSeconds(options.LockTtlSeconds)` (default 1200) instead of 6 h; `CloudProviderOptions.LockTtlSeconds` top-level (not only under Supabase).
- `CloudSyncService.Retention.cs`: when `settings.Current.CloudVersionRetentionCount == 0` use `cloudProvider.Capabilities.DefaultRetentionCount`.

### T02.7 Provider-scoped sync state
- `IAppDataPathProvider.GetSyncStateRoot(GameId game, string providerKey)`; `AppDataPathProvider` implements; `TestPathProvider` in tests too. Provider keys: `CloudProviderKeys.ToKey(kind)` → `googledrive` / `supabase` / `localtest` (new static class in `Domain/`).
- `LocalJsonSyncStateService` takes `ICloudProviderSelector` and uses `ToKey(ActiveKind)` for the folder.
- `Infrastructure/Migrations/SyncStateProviderScopeMigrator.cs` following `LegacyLayoutMigrator` (marker key `SyncStateProviderScopeV1` in `migrations.json`): moves `sync-state\<game>\*.json` → `sync-state\<game>\googledrive\`. Register and run from `App.xaml.cs` right after `LegacyLayoutMigrator`.

### T02.8 View model minimal wiring
- `MainWindowViewModel`: subscribe to `ICloudProviderSelector.ActiveChanged` → clear `CloudStatus`, `CloudOverview`, call `RefreshCloudStatusAsync(false)` + `RefreshCloudOverviewAsync()`. Computed `ShowSharedFolderSettings` / `ShowGroupSettings` from `Capabilities`. No new dialogs yet.
- Game monitor: every tick while `IsGameRunning && HasOwnCloudSession()`, if `DateTimeOffset.UtcNow - lastHeartbeatAt >= 5 min` → `await _cloudSyncService.HeartbeatSessionAsync(SelectedWorld)` (non-busy, exceptions logged, result `Conflict` → `StatusText` + activity).

### T02.9 Tests
`Cloud/CloudProviderSelectorTests`, `Migrations/SyncStateProviderScopeMigratorTests`, `Settings/DeviceIdentityTests`, `Cloud/CloudSyncServiceLockTests`, `Cloud/HeartbeatTests` (plan 09 §1). Extend `Support/FakeCloudProvider` with `Capabilities`, scripted `TryAcquire`, `Heartbeat`, `GetSnapshot`. Existing tests that construct `CloudSyncService` or providers get the new constructor parameters (`FixedDeviceIdentity` fake in `Support/`).

**Done when:** build clean, all tests pass, Drive manual smoke (upload, Play with LocalTest lock from `TEST_DEVICE`, heartbeat visible in the lock file after 5 min with a shortened interval in a debug run) works.

### CHECKPOINT CP1 → stop, report.

---

## T03 — Supabase provider (CP2) · plan 03

### T03.1 `SupabaseHttp`
`Infrastructure/Cloud/Supabase/SupabaseHttp.cs(SupabaseOptions options, IAppLogger logger)`: one `HttpClient` (`SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10), AutomaticDecompression = All }`), `BaseAddress = options.Url`, default header `apikey`. Methods: `SendAsync(HttpMethod, string path, HttpContent?, string? bearer, IDictionary<string,string>? headers, TimeSpan? timeout, ct)` returning `SupabaseResponse(HttpStatusCode Status, string Body, HttpResponseHeaders Headers)`; helpers `GetJsonAsync<T>`, `PostJsonAsync<T>`, `RpcAsync<T>(string fn, object args, string bearer)`, `DeleteJsonAsync`. Errors → `SupabaseException(string Code, HttpStatusCode? Status, string Message)` with `Code` from the PostgREST body `message` when it matches `^[a-z_]+$`, else from the status (`unauthorized`, `rate_limited`, `payload_too_large`, `unreachable`, `server_error`). JSON options as handoff §3.

### T03.2 Session store and auth
- `SupabaseSessionStore.cs(IAppDataPathProvider paths)`: `SupabaseSession? Load()`, `Save(SupabaseSession)`, `Clear()`. `SupabaseSession` record: `AccessToken, RefreshToken, ExpiresAtUtc, Email, UserId`. Serialise JSON → `ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)` → file `paths.SupabaseSessionPath` (`%LOCALAPPDATA%\SaveHarbor\supabase-session.bin`, add to `IAppDataPathProvider`). Any exception on load → null + warning log. **V1** applies: try without the package first.
- `SupabaseAuthClient.cs(SupabaseHttp http, SupabaseSessionStore store, IAppLogger logger, TimeProvider time)`: `RequestCodeAsync(email)`, `VerifyCodeAsync(email, code)`, `GetAccessTokenAsync()` (refresh when `ExpiresAtUtc - now < 60 s`, single-flight), `SignOutAsync()`, `IsSignedIn`, `Email`, `UserId`. Use `TimeProvider` so tests control time.

### T03.3 Models, mapping, REST, storage
- `SupabaseModels.cs`: DTOs (plan 03 §2). `SupabaseMapping.cs`: DTO ↔ `CloudWorldManifest`, `CloudVersionMetadata`, `CloudSessionLock`, `CloudMember`, `CloudGroupSummary`; validates `world_id`, `version_id`, `archive_path` with `SafePath.IsSafeSegment` per segment and throws `SupabaseException("invalid_data")` otherwise.
- `SupabaseRest.cs` (+ `SupabaseRest.Groups.cs`): typed calls from plan 03 §3; every method takes the bearer from `SupabaseAuthClient`.
- `SupabaseStorage.cs`: `UploadAsync(string objectPath, string filePath, IProgress<long>? progress, ct)` streaming `StreamContent` with `Content-Type: application/zip`, `x-upsert: false`, `TransferTimeoutSeconds`; `DownloadAsync(objectPath, targetPath, progress, ct)` streaming to file; `DeleteAsync(IReadOnlyList<string> paths)` in chunks of 100 (**V7**); `ListAsync(prefix)`.

### T03.4 `SupabaseCloudProvider` (+ `.Versions.cs`, `.Locks.cs`)
Constructor `(SupabaseOptions options, SupabaseAuthClient auth, SupabaseRest rest, SupabaseStorage storage, ICloudGroupService groups, IDeviceIdentity device, IPlayerIdentity player, IAppLogger logger)`. `ProviderName = "SaveHarbor Cloud"`. `Capabilities = (false, true, true, true, true, false, options.MaxArchiveBytes, 10)`.
- `GetConnectionStatusAsync`: configured? → signed in? (`NeedsSetup = true`, `Reason = "SignIn"`) → group for game? (`NeedsSetup = true`, `Reason = "Group"`) → `app_meta` min version check (cached 1 h; `Reason = "UpdateRequired"` when `CurrentVersion < min`) → connected with `AccountEmail`.
- `ConnectAsync`: returns the status; never opens UI.
- `GetSnapshotAsync`: RPC → map; cache `worldId → uuid` per (group, game) in a dictionary.
- `GetWorldManifestAsync`, `ListWorldManifestsAsync`, `GetSessionLockAsync`: via a fresh snapshot (simple; the view model calls the snapshot path anyway).
- `UploadVersionAsync`: plan 03 §4 exactly, including the size guard and conflict clean-up.
- `DownloadVersionAsync`: `archive_path` from the version row (stored in `CloudVersionMetadata.ArchiveFileName` as the **object path**; `SafePath` check per segment) → `storage.DownloadAsync` → `CloudDownloadResult(true, targetPath)`.
- `ListStoredVersionsAsync` → `CloudStoredVersion(ArchiveFileName = version_id + ".zip")` so `CloudVersionRetention` keeps working; `RemoveVersionAsync` resolves the row by `version_id`, deletes object then row; `RemoveWorldAsync` deletes objects then RPC.
- Locks: `TryAcquireSessionLockAsync` → `acquire_session_lock`; `HeartbeatSessionLockAsync` → RPC, `lock_lost` → null; `ClearSessionLockAsync` → `release_session_lock`.
- `DisconnectAsync` → `auth.SignOutAsync()`.

### T03.5 `SupabaseGroupService` and error mapper
- `SupabaseGroupService.cs(SupabaseAuthClient auth, SupabaseRest rest, CloudProviderSettingsService settings, IAppLogger logger) : ICloudGroupService` (plan 02 §4). Invite code normalisation: upper-case, strip everything not `[A-Z2-9]`, must be 10 chars. Active groups persisted in `LocalCloudProviderSettings.ActiveGroups`; `KnownGroups` cached in memory and in `cloud-provider-settings.json` (`KnownGroups` list: id, name, role) so the Settings view can show the group name offline.
- `SupabaseErrorMapper.cs`: `SupabaseException` → `AppError` (`AppErrorCode.CloudAuth`, `CloudProviderFailure`, new `CloudUpdateRequired`, `CloudConflict`) with the texts from plan 04 §7. `AppErrorHandler` gets the new codes.

### T03.6 DI and selector
`App.xaml.cs`: register `SupabaseHttp`, `SupabaseSessionStore`, `SupabaseAuthClient`, `SupabaseRest`, `SupabaseStorage`, `SupabaseGroupService` (as itself and `ICloudGroupService`), `SupabaseCloudProvider`; the selector's factory now returns it. `TimeProvider.System` registered as `TimeProvider`.

### T03.7 Tests
`Support/RecordingHttpMessageHandler.cs` (route → scripted responses, records requests), fixtures under `Tests/Cloud/Supabase/Fixtures/` (`snapshot.json`, `publish_ok.json`, `publish_conflict.json`, `lock_acquired.json`, `lock_held.json`, `session.json`, `app_meta.json`), and the test classes from plan 09 §1 for mapping, error mapper, provider, auth client, session store, group service, minimum version gate.

**Done when:** build clean; tests pass; with the owner's real URL/key in a local `appsettings.json` (not committed) and a group created through `race.ps1`-style REST calls or the dashboard, a console-less check works: set `AppSettings.CloudProvider = "Supabase"` by hand, start the app, Sharing shows "Sign in to share" (no dialog yet). Report any API mismatch against the plan.

### CHECKPOINT CP2 → stop, report.
