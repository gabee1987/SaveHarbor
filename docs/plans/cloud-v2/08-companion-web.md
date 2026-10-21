# 08 — Companion Web Page (read-only, phone-first)

A small React page, hosted free on GitHub Pages, that answers "is anyone playing, and what is the newest version?" from a phone. It does not replace the desktop app (it cannot touch save files, processes or Steam) and it never writes to the cloud in v1.

## 1. Scope

- Sign in with the same e-mail code flow (supabase-js `signInWithOtp` + `verifyOtp({ type: 'email' })`).
- Pick a group (if more than one). Per game tab: worlds with latest version (number, by whom, when, size), lock state ("Anna is playing since 20:12, lock until 20:52" / "free"), presence ("Ben has SaveHarbor open"), version history (last 20).
- Live updates through Supabase Realtime (postgres changes + presence), same channel as the desktop app (plan 05 §6).
- Owner only: show the invite code (RPC `get_invite_code`) with a copy button and the `saveharbor://join/<code>` link.
- Out of scope v1: releasing locks, deleting versions, downloading archives (egress), notifications.

## 2. Stack

- `web/` folder in the repo: Vite + React 19 + TypeScript, `@supabase/supabase-js` v2. No UI framework; one `styles.css` using the same colour tokens as `Resources/Themes/*.Colors.xaml` (copied by hand, documented at the top of the file), light and dark via `prefers-color-scheme`.
- Hosting: GitHub Pages from the `pages.yml` workflow (plan 07), base path `/SaveHarbor/`. URL becomes `https://<owner>.github.io/SaveHarbor/`.
- Config: `VITE_SUPABASE_URL`, `VITE_SUPABASE_PUBLISHABLE_KEY` from repository variables at build time (public values).
- Auth redirect allowlist is **not** needed (code-based OTP, no links).
- Bundle target: under 150 kB gzipped; no analytics, no fonts fetched from third parties (system font stack).

## 3. Data access (RLS does the work)

Reads: `group_summaries` view, `rpc('get_group_snapshot', { p_group, p_game })`, `world_versions` select for history. Realtime: `supabase.channel('saveharbor:<groupId>')` with `postgres_changes` filters on `group_id` and presence with the user's display name. The page never uses the `service_role` key and never calls write RPCs except `set_display_name` (optional, v1.1).

## 4. Pages

1. `/` — sign-in (e-mail → code), remembers the session in `localStorage` (supabase-js default).
2. `/groups` — list, auto-skips when there is exactly one.
3. `/g/:groupId` — tabs Windrose / Dragonwilds; cards per world; a "Live" dot when the channel is joined; pull-to-refresh fallback.
4. `/g/:groupId/w/:worldUuid` — version history and lock timeline.

## 5. Security and privacy

- The page shows e-mails of nobody; members are listed by display name only (`get_group_snapshot` returns `user_id` and `display_name`).
- Sessions in `localStorage` on a phone: acceptable for a hobby group; a "Sign out everywhere" is available from the desktop app's Settings (calls `POST /auth/v1/logout?scope=global`).
- Content-Security-Policy meta tag: `default-src 'self'; connect-src https://<ref>.supabase.co wss://<ref>.supabase.co; img-src 'self' data:; style-src 'self' 'unsafe-inline'`.
- GitHub Pages and Supabase are third-party services; the same compliance note as in plan 00 §6 applies.

## 6. Tests

Vitest for the snapshot-to-view mapping and the lock-state text; a Playwright smoke test (sign-in page renders, group list renders with a mocked supabase-js) is optional. CI job in `pages.yml` runs `npm ci && npm test && npm run build`.
