# Cloud v2 — Supabase provider, robust updates, faster UX: Overview and Roadmap

**Branch:** `feature/supabase-cloud` (from `develop`). The owner performs all commits and pull requests manually (see the `git-workflow` skill).
**Status:** planning only. No production code has been changed yet.
**Date:** 2026-10-14.
**Baseline:** `develop` at version 2.2.2 (.NET 10, two games, Google Drive provider, `SaveHarbor.Tests` present).

## 1. Goals

1. **A second cloud provider, Supabase**, selectable next to Google Drive at runtime. Google Drive keeps working unchanged for groups that already use it.
2. **Faster and simpler sharing**: one request instead of a folder walk per world, atomic session locks (no more "two friends both won the lock"), heartbeats, instant "X is playing" through realtime, cached status at startup.
3. **Sign-in that friends can do in under a minute** and never repeat: e-mail one-time code, session kept encrypted on the PC, groups joined by an invite code (no Google test-user list, no shared-folder links).
4. **Robust versioning and updates**: the app detects a new release, downloads it in the background, applies it on restart or on exit, shows release notes, and refuses to sync against a cloud schema it is too old for.
5. **One-command releases**: a git tag builds, packages, and publishes the installer and the update feed through GitHub Actions.
6. **Convenience**: minimise to tray while playing, upload on game close, skip unchanged uploads, toasts when a friend shares, a read-only phone page showing who is playing.

Non-goals: replacing WPF (see §4), dedicated-server export, migrating Google Drive history into Supabase (a "start fresh from the latest version" helper is in scope, see plan 02 §6).

## 2. Current state (analysis summary)

- `ICloudProvider` ([Services/ICloudProvider.cs](../../../SaveHarbor.App/Services/ICloudProvider.cs)) is already a clean seam: manifests, immutable versions (zip + metadata), one session lock per world, retention and removal. `CloudSyncService` and the view model depend only on it. **This seam is kept.** Supabase is a new implementation behind it, with two small extensions (plan 02 §3).
- Provider choice is **static** (`appsettings.json` → `Provider`) and resolved once in `App.xaml.cs`. It has to become a per-user runtime setting.
- Setup is **folder-centric**: `ICloudSetupService`, `ISharedFolderCloudProvider`, `CloudFolderSetupWindow`, `LocalCloudProviderSettings.SharedFolders`. Supabase has no folders; it has **groups** (a friend circle with an invite code). Setup becomes provider-specific (plan 02 §4, plan 04).
- `LocalSyncState` stores `LocalBaseVersionNumber` **per world, not per provider**. Switching providers without scoping this would misreport "in sync". Sync state must be scoped by provider (plan 02 §5).
- Lock identity is `Environment.MachineName`; there is no heartbeat; auto-end after game close only clears the lock (no upload). Known weaknesses are listed in the `cloud-sync-and-locking` skill; Supabase fixes 1, 2, 3 and 5 structurally (plan 01 §4, plan 03 §5), and the upload-on-close part of 4 is included in plan 05 §4.
- Distribution is a hand-built single-file zip (README). There is **no update check** of any kind. Friends on the old zip must be told once, by hand, to install the new build (plan 06 §7).
- Archive sizes on the owner's PC: 3–18 MB per version. Far below Supabase's 50 MB free-plan file limit; resumable (TUS) uploads are not needed for the MVP.

## 3. Plans and order

| # | Plan | Size | Depends on |
|---|---|---|---|
| 01 | [Supabase backend](01-supabase-backend.md) — project, schema, RLS, RPCs, storage, auth settings, migrations | M | — |
| 02 | [Provider model and settings](02-provider-model-and-settings.md) — runtime provider choice, group concept, provider-scoped sync state, device identity | M | — |
| 03 | [Supabase provider implementation](03-supabase-provider-implementation.md) — REST client, auth session, uploads/downloads, locks, retention | L | 01, 02 |
| 04 | [Sign-in and groups UX](04-sign-in-and-groups-ux.md) — e-mail code sign-in, create/join group, first-run flow, Settings | M | 02, 03 |
| 05 | [Speed, realtime and convenience](05-speed-realtime-and-convenience.md) — snapshot caching, heartbeat, realtime, tray, upload on close, unchanged-skip | L | 03, 04 |
| 06 | [App updates (Velopack)](06-app-updates-velopack.md) — update check, download, apply, notes, minimum-version gate | M | — (gate needs 01) |
| 07 | [Release pipeline](07-release-pipeline.md) — GitHub Actions: CI, tag → release, Supabase migrations, keep-alive | M | 06 |
| 08 | [Companion web page](08-companion-web.md) — read-only React page on GitHub Pages: who is playing, versions | M | 01, 04 |
| 09 | [Testing](09-testing.md) — fakes, HTTP replay tests, SQL tests for lock/version CAS, update service tests | M | parallel |
| 10–13 | **Implementation handoff**: [10 rules and checkpoints](10-implementation-handoff.md), [11 backend + provider tasks](11-tasks-backend-and-provider.md), [12 sign-in + UX + realtime tasks](12-tasks-signin-ux-realtime.md), [13 updates + release + web tasks](13-tasks-updates-release-web.md). Task files win over plans 01–09 where they differ | — | — |

Recommended execution: **01 → 02 → 03 → 04 (MVP: friends can share through Supabase) → 06 → 07 (first installer release) → 05 → 08**. Plan 09 runs alongside each step. Plans 06/07 can be done first if a release is wanted before the Supabase work lands; they do not depend on it (the minimum-version gate simply stays dormant until plan 01 exists).

## 4. Key decisions

| Decision | Choice | Why / alternative |
|---|---|---|
| Second provider | **Supabase** (dedicated new project, free plan first) | Postgres gives atomic locks and one-call snapshots; Auth, Storage and Realtime in one place; owner already uses it. Alternative kept in mind: Cloudflare R2 + Worker if the free-plan pause becomes a problem |
| Free-plan pause (1 week idle) | Weekly keep-alive ping from GitHub Actions; **[verify]** that an authenticated REST call counts as activity. Fallback: Pro plan (25 USD/month) | Pausing surfaces as "cloud unreachable" with a clear message and a link to the dashboard |
| Supabase client | **Hand-written REST client over `HttpClient` + `System.Text.Json`**, no `supabase-csharp` | The umbrella package pulls Postgrest, Gotrue, Realtime, Functions, Storage and Newtonsoft.Json into an app that must stay small. The surface we need is about ten endpoints. Realtime is a ~200-line `ClientWebSocket` client (plan 05) |
| Sign-in | **E-mail one-time 6-digit code** typed into the app; refresh token stored with DPAPI (`ProtectedData`, current user) | No browser redirect, no password, works on any PC. Magic links under PKCE only open in the requesting browser, which a desktop app does not have. Discord login is a possible later addition. Anonymous sign-in rejected (unrecoverable identity, abuse surface) |
| Group model | One **group** = one friend circle with a 10-character invite code; worlds are scoped by `(group, game, world_id)`. A group may be used for both games; a different group per game is allowed | Mirrors "one shared Drive folder per game" without the folder |
| Lock identity | New per-install `DeviceId` (GUID in `app-settings.json`) in the lock; `MachineName` becomes a display label | Fixes "two users on one PC" and stops using machine names as identity. Drive provider writes both; `IsOwnLock` prefers `DeviceId` |
| Version numbering | Server-assigned through `publish_world_version` RPC with compare-and-set on the expected latest version | Two uploads can never both become v7 |
| Lock TTL | 20 minutes, heartbeat every 5 minutes from the existing game-monitor timer | A crash leaves a lock for at most 20 minutes; UI shows "expired at" and offers take-over |
| Updates | **Velopack 1.2.x** with `GithubSource` on the public repo; check at startup + every 6 h; download in background; apply on restart or on exit; never while the game runs | Maintained successor of Squirrel; delta updates; packages our existing self-contained publish |
| Install location | Velopack `packId` **`SaveHarborApp`** → `%LOCALAPPDATA%\SaveHarborApp\` | Must not collide with the data root `%LOCALAPPDATA%\SaveHarbor\` |
| Portable build | Kept: `vpk pack` also emits `SaveHarborApp-win-Portable.zip` (updates work from it too) | For friends who dislike installers |
| Code signing | Out of scope now; the plan leaves a hook for Azure Trusted Signing (`vpk pack --signParams`) | SmartScreen "Run anyway" remains, as today |
| WPF vs web | **Keep WPF.** A browser cannot read save folders, detect the game process or launch Steam without a local agent; Electron is too heavy next to a game. The web part is a **read-only companion page** (plan 08) | Tauri would be the only credible rewrite target and gains nothing functionally |

## 5. Skills to add after implementation (not part of this planning step)

`supabase-cloud-provider` (schema, RPC contracts, REST endpoints, RLS invariants), `app-updates-and-release` (Velopack, tag → release, version gate). Update `cloud-sync-and-locking`, `architecture-overview`, `build-and-verify`, `dependency-versions` when the code lands.

## 6. Compliance and external services

- **Supabase** (database, auth, storage, realtime), **GitHub** (releases, Actions, Pages) and **Velopack** (library; its hosted "Flow" service is **not** used) are third-party services. They are acceptable for this private hobby group. Any wider or organisational use requires a compliance review (GDPR: e-mail addresses, display names, device labels and IP addresses are processed; the Supabase region must be in the EU, see plan 01 §1).
- Data minimisation: the cloud stores e-mail (auth only), a display name, a device label, timestamps, and save archives. No Steam IDs, no world passwords, no machine names as identity.
- Secrets: the Supabase **publishable (anon) key is public by design** and ships in `appsettings.json`; safety comes from row-level security. The `service_role` key never leaves the owner's machine or GitHub secrets. Refresh tokens are stored DPAPI-encrypted and never logged.

## 7. Research sources (collected 2026-10-14)

- supabase.com/pricing — Free: 500 MB DB, 1 GB storage, 5 GB egress, 50 MB max file, 200 realtime connections, 2 active projects, **paused after 1 week of inactivity**. Pro: 25 USD/month, 100 GB storage, 250 GB egress, 500 GB max file, never paused.
- supabase.com/docs: storage file limits; auth magic link vs OTP (`signInWithOtp` sends a code when the e-mail template contains `{{ .Token }}`); anonymous sign-ins (rejected); resumable uploads (TUS, JS only in docs).
- docs.velopack.io: integrating/overview, update-sources (GithubSource, SimpleWebSource; unauthenticated GitHub limit 60 requests/hour/IP), `UpdateManager` reference. NuGet `Velopack` 1.2.161 (updated 2026-09-29), targets net8.0 and later.
- nuget.org/profiles/supabase — `supabase-csharp` 8.0.0 (2026-09-03); considered and rejected for size (see §4).
