# SaveHarbor Cloud (Supabase) Runbook

Everything about the Supabase backend that is done by hand: the local Docker stack for development and testing, the
one-time setup of the hosted project, and recurring operations. The schema itself lives in
`supabase/migrations/`; its design is described in `docs/plans/cloud-v2/01-supabase-backend.md`.

## 1. Local stack (Docker)

The local stack is a complete SaveHarbor Cloud on your PC: Postgres with the schema and test data, sign-in by e-mail
code, storage, realtime, the Studio dashboard and a mail catcher. Nothing leaves the PC.

**Requirements:** Docker Desktop running, Node.js 20 or later. The Supabase CLI is pinned in `package.json`, so no
global install is needed.

```powershell
npm install            # once, installs the pinned Supabase CLI into node_modules
npm run db:start       # starts the stack; the first start downloads the images (several GB)
npm run db:status      # prints URLs and the local keys
npm run db:stop        # stops the stack and keeps its data
```

| Service | Address |
| --- | --- |
| API (REST, auth, storage, realtime) | `http://127.0.0.1:54321` |
| Studio dashboard | `http://127.0.0.1:54323` |
| Mailpit (sign-in codes land here) | `http://127.0.0.1:54324` |
| Postgres | `postgresql://postgres:postgres@127.0.0.1:54322/postgres` |

The local publishable key is printed by `npm run db:status`. It is the same well-known development key on every
machine and only works against the local stack.

**Test data (`supabase/seed.sql`):** users `test-a@example.invalid` (owner) and `test-b@example.invalid`, both in
group `TEST_GROUP`, invite code `TESTCODE22`. To sign in as one of them, request a code for the address and read it in
Mailpit. Any other address also works: the first code creates the account.

## 2. Tests

```powershell
npm run db:test                                  # pgTAP tests in supabase/tests (database rules)
npm run db:lint                                  # static check of all functions
pwsh supabase/scripts/smoke-test.ps1             # end-to-end over the real APIs, including a parallel lock race
npm run db:reset                                 # rebuild the database from migrations + seed
```

Run `db:reset` after changing a migration. The smoke test only runs against `127.0.0.1` and cleans up after itself.
It requests sign-in codes, so running it twice within 30 seconds hits the per-address code limit; wait and rerun.

## 3. Changing the schema

1. Add a new file `supabase/migrations/<yyyyMMddHHmmss>_<what>.sql`. Never edit a migration that has been pushed to the
   hosted project.
2. `npm run db:reset`, then `npm run db:test` and the smoke test.
3. Extend the pgTAP tests for the new rule.
4. Breaking change for older apps? Raise `min_supported_app_version` in the same migration, and release the fixed app
   **before** pushing the migration.
5. Adding a game: a migration with `insert into public.games (key) values ('<key>');` only. The key must equal the
   app's storage key (for example `enshrouded`). Push it **before** releasing the app that supports the game. Older
   apps are not affected: they never ask for a game they do not know.

## 4. Hosted project (one-time setup, owner)

1. Create a new project named `saveharbor` in an EU region (Frankfurt). Keep the database password in a password
   manager. Never put the `service_role` or secret key into the repository or the app.
2. Link and push from the repository root:
   ```powershell
   npx supabase login
   npx supabase link --project-ref <project-ref>
   npx supabase db push
   ```
3. Dashboard → Authentication → Sign In / Providers → Email: enabled, "Confirm email" on. Leave sign-ups enabled; the
   invite code controls who joins a group.
4. Dashboard → Authentication → Emails → Templates: set **Magic Link** and **Confirm signup** to the content of
   `supabase/templates/sign-in-code.html`, subject `SaveHarbor sign-in code`. The `{{ .Token }}` placeholder becomes
   the 6-digit code.
5. Dashboard → Authentication → Settings: e-mail OTP expiry 600 seconds.
6. Dashboard → Storage: check that bucket `saves` exists (the migration creates it), private, 45 MB limit.
7. Dashboard → Realtime → Settings: turn **off** "Allow public access", so only private channels exist. The app's live
   channel `saveharbor:<group id>` is private; migration `20261022000000_realtime.sql` lets only that group's members
   join it. Check after `db push` that `select policyname from pg_policies where schemaname = 'realtime';` lists
   `saveharbor_members_receive` and `saveharbor_members_track_presence`.
8. Copy the project URL and the **publishable** key into `SaveHarbor.App/appsettings.json` (plan 03 §1). Both are
   public by design; row-level security protects the data.

9. Companion web page (`web/`, plan 08): set the repository variables `SUPABASE_URL` and `SUPABASE_PUBLISHABLE_KEY`
   (Settings → Secrets and variables → Actions → Variables; the keepalive workflow uses the same two), then Settings →
   Pages → Source: **GitHub Actions**. Every push to `main` that changes `web/` runs the `pages` workflow, which tests,
   builds and publishes `https://<owner>.github.io/SaveHarbor/`. The page signs in with the same e-mail code and never
   creates an account. GitHub Pages is a third-party host; the compliance note of plan 00 §6 applies.

The built-in e-mail sender is rate-limited and meant for development. If codes arrive late, configure custom SMTP
(see plan 07 §6). A custom SMTP provider is a third-party service and needs a compliance review outside hobby use.

## 5. Operations

- **Paused project (free plan, after 1 week without activity):** Dashboard → project → Resume. The weekly
  `keepalive` workflow (`.github/workflows/keepalive.yml`, Mondays 06:00 UTC) should prevent it. It needs the
  repository variables `SUPABASE_URL` and `SUPABASE_PUBLISHABLE_KEY`; check its run history if the project pauses,
  and run it by hand (Actions → keepalive → Run workflow) after setting the variables.
- **Storage use:** Dashboard → Storage, or `select sum(archive_size_bytes) from world_versions` in the SQL editor.
  The free plan includes 1 GB.
- **Remove a member:** delete the row from `group_members` in the dashboard. The `remove_member` function exists, but
  the app has no button for it yet (planned with T12.5).
- **Delete a user on request (GDPR):** delete or reassign that user's rows in `world_versions` (they reference the
  uploader), then delete the user under Authentication → Users. Memberships and locks are removed with the user.
- **Backup before risky migrations:** download the `saves` bucket in the dashboard and run
  `npx supabase db dump --linked -f backup.sql` (keep the file outside the repository).
- **Rotate the publishable key:** requires a new app release because the key ships in `appsettings.json`; disable the
  old key only after friends have updated. Update the `SUPABASE_PUBLISHABLE_KEY` repository variable too, and run the
  `pages` workflow by hand so the companion page uses the new key.
- **Raise `min_supported_app_version`:** only together with a breaking schema migration, and only after the app
  release that understands it is live (README, "Releasing a new version"). Older apps then show "Update required"
  and stop writing until they update.
