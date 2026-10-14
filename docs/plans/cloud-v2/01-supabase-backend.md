# 01 — Supabase Backend

Everything that lives in the Supabase project: schema, row-level security, RPC functions, storage bucket, auth configuration, migrations, and operational rules. The app code is in plan 03.

## 1. Project setup (owner, by hand, once)

1. Create a **new dedicated project** in the Supabase dashboard. Region: an **EU region** (Frankfurt). Name it `saveharbor`. Record the project ref, the project URL (`https://<ref>.supabase.co`) and the **publishable (anon) key**. Never copy the `service_role` key into the repo.
2. Auth → Providers → Email: enabled. **Confirm email: on. Secure email change: on.** Leave "Allow new users to sign up" **on**; the invite code gates group access, not account creation.
3. Auth → Email Templates → **Magic Link**: replace the body so it contains the one-time code, not a link:
   ```html
   <h2>Your SaveHarbor sign-in code</h2>
   <p>Enter this code in SaveHarbor within 10 minutes:</p>
   <p style="font-size:28px;letter-spacing:4px"><strong>{{ .Token }}</strong></p>
   <p>If you did not request this, ignore this e-mail.</p>
   ```
   Set the **Confirm signup** template to the same `{{ .Token }}` form (the first sign-in of a new address uses it). Subject: `SaveHarbor sign-in code`.
4. Auth → Rate limits: keep defaults. Auth → Sessions: default; **do not** enable "single session per user" (a friend may use two PCs).
5. Auth → SMTP: the built-in sender is rate-limited and meant for development. **[verify]** the current limit in the dashboard. For a 4–8 person group it is acceptable at first; plan 07 §6 notes a free SMTP option (Resend or Brevo free tier) if codes arrive late. Any such provider needs the same compliance note as Supabase.
6. Storage → create bucket **`saves`**, private, file size limit 45 MB (below the 50 MB plan cap so the app's own check matches), allowed MIME types `application/zip`.
7. Database → Extensions: enable `pgcrypto` (`gen_random_bytes`) if not already enabled.
8. Install the Supabase CLI on the owner's PC (`winget install Supabase.Supabase` or `scoop install supabase`), run `supabase login`, then `supabase link --project-ref <ref>` from the repo root after §6 adds the `supabase/` folder. **The implementing agent must not install tools or link projects**; it writes the migration files and the owner applies them.

## 2. Schema (migration `supabase/migrations/20261014000000_initial.sql`)

Design rules: every table has RLS enabled; the app only talks to `public` tables and functions; identifiers are stable `uuid`s; game keys are the app's storage keys (`windrose`, `dragonwilds`); world ids are validated with the same character class as `SafePath.IsSafeSegment`.

```sql
create extension if not exists pgcrypto;

create table public.groups (
  id uuid primary key default gen_random_uuid(),
  name text not null check (char_length(name) between 1 and 60),
  invite_code text not null unique check (invite_code ~ '^[A-Z2-9]{10}$'),
  created_by uuid not null references auth.users(id) on delete restrict,
  created_at timestamptz not null default now()
);

create table public.group_members (
  group_id uuid not null references public.groups(id) on delete cascade,
  user_id uuid not null references auth.users(id) on delete cascade,
  display_name text not null check (char_length(display_name) between 1 and 32),
  role text not null default 'member' check (role in ('owner', 'member')),
  joined_at timestamptz not null default now(),
  primary key (group_id, user_id)
);
create index group_members_user_idx on public.group_members(user_id);

create table public.worlds (
  id uuid primary key default gen_random_uuid(),
  group_id uuid not null references public.groups(id) on delete cascade,
  game text not null check (game in ('windrose', 'dragonwilds')),
  world_id text not null check (world_id ~ '^[A-Za-z0-9][A-Za-z0-9 _.()-]{0,127}$'),
  world_name text not null check (char_length(world_name) between 1 and 120),
  latest_version_number integer not null default 0,
  keep_latest_versions integer not null default 10,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (group_id, game, world_id)
);

create table public.world_versions (
  id uuid primary key default gen_random_uuid(),
  world_uuid uuid not null references public.worlds(id) on delete cascade,
  group_id uuid not null references public.groups(id) on delete cascade,
  version_number integer not null check (version_number > 0),
  version_id text not null check (version_id ~ '^[A-Za-z0-9_.-]{1,160}$'),
  uploaded_by uuid not null references auth.users(id) on delete restrict,
  uploader_name text not null,
  uploader_device text not null default '',
  archive_path text not null check (archive_path ~ '^[A-Za-z0-9/_.()-]{1,400}$'),
  archive_sha256 text not null check (archive_sha256 ~ '^[0-9a-f]{64}$'),
  archive_size_bytes bigint not null check (archive_size_bytes >= 0),
  source_world_modified_at timestamptz,
  based_on_version_number integer not null default 0,
  based_on_version_id text not null default '',
  created_at timestamptz not null default now(),
  unique (world_uuid, version_number),
  unique (world_uuid, version_id)
);
create index world_versions_world_idx on public.world_versions(world_uuid, version_number desc);

create table public.session_locks (
  world_uuid uuid primary key references public.worlds(id) on delete cascade,
  group_id uuid not null references public.groups(id) on delete cascade,
  lock_id uuid not null,
  user_id uuid not null references auth.users(id) on delete cascade,
  player_name text not null,
  device_id uuid not null,
  device_label text not null default '',
  started_at timestamptz not null default now(),
  last_heartbeat_at timestamptz not null default now(),
  expires_at timestamptz not null,
  based_on_version_number integer not null default 0,
  based_on_version_id text not null default '',
  status text not null default 'playing' check (status in ('playing'))
);

create table public.app_meta (
  key text primary key,
  value text not null,
  updated_at timestamptz not null default now()
);
insert into public.app_meta (key, value) values ('min_supported_app_version', '2.3.0'), ('schema_version', '1');

create function public.touch_updated_at() returns trigger language plpgsql as $$
begin new.updated_at = now(); return new; end $$;
create trigger worlds_touch before update on public.worlds for each row execute function public.touch_updated_at();
```

Realtime: `alter publication supabase_realtime add table public.session_locks, public.worlds, public.world_versions;`. Plan 05 filters by `group_id`, which is why `world_versions` and `session_locks` carry a denormalised `group_id`.

## 3. Row-level security

Membership helper (avoids recursive policies on `group_members`):

```sql
create function public.is_group_member(p_group uuid) returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.group_members where group_id = p_group and user_id = auth.uid());
$$;
revoke all on function public.is_group_member(uuid) from public;
grant execute on function public.is_group_member(uuid) to authenticated;
```

Policies (all `to authenticated`):

| Table | select | insert | update | delete |
|---|---|---|---|---|
| `groups` | **none** directly; members read through the view `group_summaries` | none (only `create_group`) | none (only `rename_group`) | none in v1 |
| `group_members` | member of the same group | none (only `join_group`) | own row, `display_name` only | own row (leave); owner may remove others |
| `worlds` | member | member, `group_id` membership checked | member: `world_name`, `keep_latest_versions`; `latest_version_number` guarded by trigger (see §4) | member |
| `world_versions` | member | none (only `publish_world_version`) | none | member (retention) |
| `session_locks` | member | none (RPC) | none (RPC) | none (RPC) |
| `app_meta` | `anon` and `authenticated` | none | none | none |

`invite_code` must not be readable by plain members: `public.group_summaries` is a `security_invoker = false` view over `groups` + member counts + `my_role` that **omits** `invite_code`; grant select on the view to `authenticated` and filter by `is_group_member(id)`. Owners obtain the code through `get_invite_code`.

Storage policies on `storage.objects` for `bucket_id = 'saves'`: path layout `<group_id>/<game>/<world_id>/<archive>.zip`. `select`, `insert`, `delete` are allowed when `public.is_group_member(((storage.foldername(name))[1])::uuid)`. No `update` (objects are immutable; uploads send `x-upsert: false`).

## 4. RPC functions

All `security definer`, `set search_path = public`, `revoke all ... from public; grant execute ... to authenticated`. Error convention: `raise exception using errcode = 'P0001', message = '<code>'` with codes the app maps 1:1: `not_member`, `invite_invalid`, `version_conflict`, `lock_lost`, `not_owner`, `owner_must_stay`, `world_missing`, `direct_update_forbidden`. PostgREST returns them as HTTP 400 with `{ "message": "<code>" }`.

| Function | Signature | Behaviour |
|---|---|---|
| `create_group` | `(p_name text, p_display_name text) returns group_summaries` | Insert group with a fresh invite code (`generate_invite_code()`: 10 chars from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` built from `gen_random_bytes`), insert owner membership. |
| `join_group` | `(p_invite_code text, p_display_name text) returns group_summaries` | Upper-case and trim the code; find the group; `insert ... on conflict (group_id, user_id) do update set display_name`; raise `invite_invalid` when not found. |
| `leave_group` | `(p_group uuid)` | Delete own membership; raise `owner_must_stay` when an owner tries to leave while other members exist. |
| `rotate_invite_code` | `(p_group uuid) returns text` | Owner only. |
| `get_invite_code` | `(p_group uuid) returns text` | Owner only. |
| `rename_group` | `(p_group uuid, p_name text)` | Owner only. |
| `set_display_name` | `(p_group uuid, p_display_name text)` | Own membership row. |
| `get_group_snapshot` | `(p_group uuid, p_game text) returns jsonb` | Membership check, then one JSON document: `{ "server_time": ..., "members": [ {user_id, display_name, role} ], "worlds": [ { ...world row, "latest": version row or null, "lock": lock row or null } ] }`. Replaces listing manifests, reading each manifest and each lock. |
| `publish_world_version` | `(p_group uuid, p_game text, p_world_id text, p_world_name text, p_expected_latest integer, p_version jsonb) returns world_versions` | Upsert the world row and `select ... for update`; if `latest_version_number <> p_expected_latest` raise `version_conflict`; `new_number = latest + 1`; insert the version from `p_version` (keys: `version_id, uploader_name, uploader_device, archive_path, archive_sha256, archive_size_bytes, source_world_modified_at, based_on_version_number, based_on_version_id`) with `version_number = new_number`, `uploaded_by = auth.uid()`; `perform set_config('saveharbor.rpc', 'on', true)`; update `latest_version_number`; return the row. The archive must already exist in storage (plan 03 §4). |
| `acquire_session_lock` | `(p_world uuid, p_lock_id uuid, p_player_name text, p_device_id uuid, p_device_label text, p_based_on_number integer, p_based_on_id text, p_ttl_seconds integer) returns session_locks` | Membership check through the world's group. `insert ... on conflict (world_uuid) do update set <all columns> where session_locks.expires_at < now() or session_locks.device_id = excluded.device_id returning *`. If nothing is returned, return the existing row so the caller sees the holder. The caller compares `lock_id`; only its own id means success. TTL clamped to 60..3600 s. |
| `heartbeat_session_lock` | `(p_world uuid, p_lock_id uuid, p_ttl_seconds integer) returns session_locks` | `update ... set last_heartbeat_at = now(), expires_at = now() + ttl where world_uuid = p_world and lock_id = p_lock_id and user_id = auth.uid()`; raise `lock_lost` on zero rows. |
| `release_session_lock` | `(p_world uuid, p_lock_id uuid) returns boolean` | Delete own lock by id; `true` when a row was deleted. Never deletes another user's lock. |
| `remove_world` | `(p_world uuid)` | Member. Deletes the world row (cascades versions and lock). The client deletes storage objects **first** (plan 03 §7). |

Not needed as RPCs: listing versions (`GET /rest/v1/world_versions?world_uuid=eq.<id>&order=version_number.desc`), deleting a version row for retention (direct `DELETE` under RLS), taking over an expired lock (`acquire_session_lock` already wins against an expired lock).

Trigger `worlds_guard_latest` (before update on `worlds`): if `new.latest_version_number <> old.latest_version_number and coalesce(current_setting('saveharbor.rpc', true), '') <> 'on'` then raise `direct_update_forbidden`.

## 5. Invariants the backend guarantees (tested in plan 09 §4)

1. Two concurrent `publish_world_version` calls with the same `p_expected_latest`: exactly one succeeds; the other gets `version_conflict`.
2. Two concurrent `acquire_session_lock` calls on an unlocked world: exactly one returns its own `lock_id`.
3. `acquire_session_lock` against an unexpired foreign lock returns the foreign row unchanged.
4. `acquire_session_lock` against an expired foreign lock replaces it.
5. The same device re-acquiring its own live lock gets a fresh TTL and keeps the lock.
6. A non-member cannot read, insert or delete anything in another group, including storage objects and the invite code.
7. `anon` can read `app_meta` and nothing else.

## 6. Repository layout and migrations

```
supabase/
  config.toml            (from `supabase init`, run by the owner; committed)
  migrations/
    20261014000000_initial.sql
  seed.sql               (local dev only: group TEST_GROUP with invite code TESTCODE22, user test@example.invalid)
  tests/
    lock_race.sql        (pgTAP, plan 09 §4)
    version_cas.sql
    rls_isolation.sql
```

Apply with `supabase db push` (owner). CI (plan 07 §4) runs `supabase db lint` and the pgTAP tests against a local stack when Docker is available; it never pushes to the hosted project automatically.

## 7. Operational rules

- **Keep-alive:** `.github/workflows/keepalive.yml` runs weekly and performs `GET /rest/v1/app_meta?select=key` with the publishable key. **[verify]** after the first idle week that the project stayed active; if not, switch to Pro or accept manual unpausing. The app maps a paused project (HTTP 540 or a connection failure with a `*.supabase.co` host) to the message "The group's cloud project is paused. Ask the group owner to resume it in the Supabase dashboard."
- **Quotas:** the app shows storage used per group (sum of `archive_size_bytes`) in Settings and warns above 800 MB. 8 worlds × 20 versions × 10 MB would exceed the free plan, so the default retention for Supabase groups is **10** (`keep_latest_versions` default above; the user setting `CloudVersionRetentionCount` still overrides per upload, plan 02 §7).
- **Backups:** the free plan has no point-in-time recovery. The app's local backups and the immutable version rows are the safety net; the owner may export the `saves` bucket occasionally. Document in README.
- **Account deletion (GDPR request):** a member can leave a group in the app; deleting an auth user is done by the owner in the dashboard. Versions reference `uploaded_by` with `on delete restrict`, so the owner reassigns or deletes those rows first; document in the runbook (plan 07 §7).
