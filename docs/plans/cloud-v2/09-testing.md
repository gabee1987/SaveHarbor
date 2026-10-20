# 09 — Testing

What proves each plan works. Builds on `SaveHarbor.Tests` (xUnit v3, fakes in `Support/`). Placeholder data only (`TEST_GROUP`, `TEST_USER`, `12345`, `test@example.invalid`).

## 1. Unit tests (SaveHarbor.Tests)

| Area | Test class | Covers |
|---|---|---|
| Provider selection | `Cloud/CloudProviderSelectorTests` | default resolution (empty → none chosen; legacy folder → GoogleDrive), developer override forces LocalTest, `SwitchAsync` raises `ActiveChanged`, forwarding of every `ICloudProvider` member to the active provider |
| Sync-state scoping | `Migrations/SyncStateProviderScopeMigratorTests` | moves legacy files into `googledrive/`, idempotent, never overwrites, logs counts |
| Device identity | `Settings/DeviceIdentityTests` | id generated once and persisted; label normalisation and max length |
| Lock semantics | `Cloud/CloudSyncServiceLockTests` | `StartSessionAsync` with `FakeCloudProvider.TryAcquire` returning `Acquired=false` → `SomeonePlaying` with holder name; own live lock → re-acquire; expired foreign lock → take over; `IsOwnLock` prefers `DeviceId`, falls back to `MachineName` for legacy locks; server time used for expiry when present |
| Heartbeat | `Cloud/HeartbeatTests` | throttle (5 min), `lock_lost` → `Conflict` state and message; Drive/LocalTest rewrite of the lock file |
| Snapshot mapping | `Cloud/Supabase/SupabaseMappingTests` | snapshot JSON → manifests/locks/members; null latest; unknown fields ignored; invalid `world_id`/`archive_path` rejected |
| Error mapping | `Cloud/Supabase/SupabaseErrorMapperTests` | each RPC code and HTTP status → `AppErrorCode` + text; paused-project hint only for `*.supabase.co` |
| Provider over HTTP | `Cloud/Supabase/SupabaseCloudProviderTests` | `RecordingHttpMessageHandler` (new in `Support/`) with canned responses from `Cloud/Supabase/Fixtures/*.json`: upload happy path (object PUT then RPC), `version_conflict` deletes the object, size guard makes no request, download verifies SHA-256 via existing service, remove world deletes objects before the row, 401 → one refresh then retry, 429 → `RateLimited` |
| Auth client | `Cloud/Supabase/SupabaseAuthClientTests` | request code, verify, refresh 60 s before expiry (fake clock), single-flight refresh, sign-out clears the store |
| Session store | `Cloud/Supabase/SupabaseSessionStoreTests` | DPAPI round trip (Windows), corrupt file → treated as signed out, file attributes |
| Group service | `Cloud/Supabase/SupabaseGroupServiceTests` | invite code normalisation (`"test code-22"` → `TESTCODE22`), active group per game, apply-to-all |
| Snapshot cache | `Cloud/CloudSnapshotCacheTests` | write/read per provider and game, stale badge age, never used for upload decisions (asserted through `CloudSyncService`) |
| No-op upload skip | `Cloud/UploadSkipTests` | unchanged content hash and base == latest → no provider call |
| Updates | `Updates/VelopackUpdateServiceTests` | with a fake `IUpdateSource`/checker: states, skip version, apply blocked while game running, disabled when not installed, interval scheduling |
| Minimum version gate | `Cloud/MinimumVersionGateTests` | `2.3.0 < 2.4.0` → `UpdateRequired` before `NotConnected`; Drive manifest `SchemaVersion` too high → `UpdateRequired` |
| Settings migration | `Settings/CloudProviderSettingsMigrationTests` | v2 → v3 file, `ActiveGroups` added, folders kept |
| Texts | `Localization/UiTextKeysTests` | every new key referenced in XAML exists in `ui-text.en.json` (extend the existing check if present) |

`FakeCloudProvider` gains `Capabilities`, `TryAcquireSessionLockAsync` (scripted outcome), `HeartbeatSessionLockAsync`, `GetSnapshotAsync`.

## 2. Manual smoke test (per provider)

Run with `DeveloperProviderOverride` empty and the Supabase project pointing at a **test group**. Use two Windows user accounts on one PC to get two device ids, or one PC plus the owner's second machine.

1. First start → provider choice → SaveHarbor Cloud → e-mail → code → create group → invite code shown.
2. Second user → join with the code → sees the group, no worlds.
3. User A uploads v1 → B sees it within 2 s (realtime) or on refresh; B downloads; B presses Play → lock; A sees "B is playing" with expiry; A's Play says "B is playing".
4. B closes the game → upload v2 → lock released → A gets the toast and "Cloud is newer".
5. Kill B's app during play → after 20 min A can take over (shorten `LockTtlSeconds` to 120 in a dev build to test).
6. Switch A to Google Drive and back → sync state per provider is intact; nothing deleted.
7. Set `min_supported_app_version` to `99.0.0` in the dashboard → both apps show "Update required"; reset.
8. Disconnect network → cached snapshot shows with "as of" badge; commands fail with the unreachable text; reconnect → recovers.
9. Updates: pack two local versions with `vpk pack` into a folder, start the installed app with `--update-source <folder>` → banner → download → restart → new version; repeat with the game running → "finish playing first" path → apply on exit.

## 3. Performance checks

Log-based (`AppLogKeyword.Ui`, `Debug`): first paint, snapshot RPC round trip, upload of a 15 MB archive, download + verify + restore. Targets: < 300 ms, < 600 ms, < 15 s on a 10 Mbit/s uplink, < 10 s. Record the numbers in `docs/plans/cloud-v2/FINDINGS.md` for the owner's PC.

## 4. Backend tests (`supabase/tests/*.sql`, pgTAP, run by `supabase test db`)

- `version_cas.sql`: publish with expected 0 → number 1; publish again with expected 0 → raises `version_conflict`; expected 1 → number 2; trigger blocks a direct `update worlds set latest_version_number`.
- `lock_race.sql`: acquire by user A → own id; acquire by user B → returns A's row; expire A (update `expires_at` as the test role) → B acquires; A heartbeat → `lock_lost`; B release → true; B release again → false. Uses `set local role authenticated; set local request.jwt.claims` to impersonate users.
- `rls_isolation.sql`: user C (not a member) sees no rows in `group_summaries`, `worlds`, `world_versions`, `session_locks`, and cannot read `invite_code`; `anon` can read `app_meta` only.
- Real concurrency (two sessions at once) is not expressible in pgTAP; `supabase/scripts/smoke-test.ps1` (local stack) fires two parallel `acquire_session_lock` requests per round for ten rounds and asserts exactly one winner each time, together with sign-in, upload, publish, download and clean-up over the real APIs.

## 5. Web page tests

See plan 08 §6.
