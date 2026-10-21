-- SaveHarbor Cloud: initial schema.
-- Groups of friends share game worlds. Every world has immutable versions (zip archives in the "saves" storage bucket)
-- and at most one session lock. Clients only read tables directly; every write that must stay consistent goes through
-- a security-definer function below. Error codes raised with SQLSTATE P0001 are part of the app contract:
--   not_signed_in, not_member, not_owner, invalid_input, invite_invalid, group_full, group_limit, owner_must_stay,
--   group_not_empty, version_conflict, archive_missing, world_missing, lock_held, lock_lost, direct_update_forbidden

create schema if not exists extensions;
create extension if not exists pgcrypto with schema extensions;

-- ---------------------------------------------------------------------------------------------------------------
-- Tables
-- ---------------------------------------------------------------------------------------------------------------

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
  latest_version_number integer not null default 0 check (latest_version_number >= 0),
  keep_latest_versions integer not null default 10 check (keep_latest_versions between 0 and 1000),
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
  archive_path text not null check (
    char_length(archive_path) between 1 and 400
    and archive_path ~ '^[A-Za-z0-9 /_.()-]+$'
    and position('..' in archive_path) = 0),
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
create index world_versions_group_idx on public.world_versions(group_id);

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
create index session_locks_group_idx on public.session_locks(group_id);
-- Delete events carry the whole old row, so realtime listeners see which group a released lock belonged to.
alter table public.session_locks replica identity full;

create table public.app_meta (
  key text primary key,
  value text not null,
  updated_at timestamptz not null default now()
);
insert into public.app_meta (key, value) values
  ('min_supported_app_version', '2.3.0'),
  ('schema_version', '1');

-- ---------------------------------------------------------------------------------------------------------------
-- Internal helpers (not callable by clients)
-- ---------------------------------------------------------------------------------------------------------------

create function public.touch_updated_at() returns trigger
language plpgsql set search_path = public as $$
begin
  new.updated_at = now();
  return new;
end $$;

create trigger worlds_touch before update on public.worlds
  for each row execute function public.touch_updated_at();

-- latest_version_number only moves inside publish_world_version, which sets saveharbor.rpc for its transaction.
create function public.guard_latest_version() returns trigger
language plpgsql set search_path = public as $$
begin
  if new.latest_version_number is distinct from old.latest_version_number
     and coalesce(current_setting('saveharbor.rpc', true), '') <> 'on' then
    raise exception using errcode = 'P0001', message = 'direct_update_forbidden';
  end if;
  return new;
end $$;

create trigger worlds_guard_latest before update on public.worlds
  for each row execute function public.guard_latest_version();

-- Raises one of the error codes listed at the top. Helpers marked stable or immutable raise directly instead,
-- because this function is volatile.
create function public.app_error(p_code text) returns void
language plpgsql as $$
begin
  raise exception using errcode = 'P0001', message = p_code;
end $$;

-- Trims, removes control characters and enforces a length; raises invalid_input otherwise.
create function public.clean_text(p_value text, p_max integer) returns text
language plpgsql immutable as $$
declare
  v text := btrim(regexp_replace(coalesce(p_value, ''), '[[:cntrl:]]', '', 'g'));
begin
  if char_length(v) = 0 or char_length(v) > p_max then
    raise exception using errcode = 'P0001', message = 'invalid_input';
  end if;
  return v;
end $$;

create function public.require_game(p_game text) returns void
language plpgsql immutable as $$
begin
  if p_game is null or p_game not in ('windrose', 'dragonwilds') then
    raise exception using errcode = 'P0001', message = 'invalid_input';
  end if;
end $$;

create function public.require_member(p_group uuid) returns void
language plpgsql stable security definer set search_path = public as $$
begin
  if auth.uid() is null then
    raise exception using errcode = 'P0001', message = 'not_signed_in';
  end if;
  if not exists (select 1 from public.group_members m where m.group_id = p_group and m.user_id = auth.uid()) then
    raise exception using errcode = 'P0001', message = 'not_member';
  end if;
end $$;

create function public.require_owner(p_group uuid) returns void
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_member(p_group);
  if not exists (select 1 from public.group_members m
                 where m.group_id = p_group and m.user_id = auth.uid() and m.role = 'owner') then
    raise exception using errcode = 'P0001', message = 'not_owner';
  end if;
end $$;

-- 10 characters from a 32-letter alphabet without 0/O/1/I; 256 is a multiple of 32, so every letter is equally likely.
create function public.generate_invite_code() returns text
language plpgsql volatile set search_path = public, extensions as $$
declare
  alphabet constant text := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  random_bytes bytea := extensions.gen_random_bytes(10);
  result text := '';
begin
  for i in 0..9 loop
    result := result || substr(alphabet, (get_byte(random_bytes, i) % 32) + 1, 1);
  end loop;
  return result;
end $$;

create function public.normalize_invite_code(p_code text) returns text
language sql immutable as $$
  select upper(regexp_replace(coalesce(p_code, ''), '[^A-Za-z0-9]', '', 'g'));
$$;

-- Used by policies, so clients may execute them. They reveal only whether the caller belongs to a group.
create function public.is_group_member(p_group uuid) returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.group_members m where m.group_id = p_group and m.user_id = auth.uid());
$$;

create function public.try_uuid(p_value text) returns uuid
language plpgsql immutable as $$
begin
  return p_value::uuid;
exception when others then
  return null;
end $$;

-- ---------------------------------------------------------------------------------------------------------------
-- Views
-- ---------------------------------------------------------------------------------------------------------------

-- The groups the caller belongs to, without the invite code (owners read it through get_invite_code).
create view public.group_summaries with (security_invoker = true) as
select
  g.id,
  g.name,
  g.created_at,
  (select count(*)::integer from public.group_members m where m.group_id = g.id) as member_count,
  (select m.role from public.group_members m where m.group_id = g.id and m.user_id = auth.uid()) as my_role
from public.groups g;

-- ---------------------------------------------------------------------------------------------------------------
-- Group functions
-- ---------------------------------------------------------------------------------------------------------------

create function public.create_group(p_name text, p_display_name text) returns public.group_summaries
language plpgsql volatile security definer set search_path = public as $$
declare
  v_name text := public.clean_text(p_name, 60);
  v_display text := public.clean_text(p_display_name, 32);
  v_id uuid;
  v_row public.group_summaries;
begin
  if auth.uid() is null then
    perform public.app_error('not_signed_in');
  end if;
  if (select count(*) from public.groups where created_by = auth.uid()) >= 10 then
    perform public.app_error('group_limit');
  end if;

  for attempt in 1..5 loop
    begin
      insert into public.groups (name, invite_code, created_by)
      values (v_name, public.generate_invite_code(), auth.uid())
      returning id into v_id;
      exit;
    exception when unique_violation then
      if attempt = 5 then
        raise;
      end if;
    end;
  end loop;

  insert into public.group_members (group_id, user_id, display_name, role)
  values (v_id, auth.uid(), v_display, 'owner');

  select * into v_row from public.group_summaries where id = v_id;
  return v_row;
end $$;

create function public.join_group(p_invite_code text, p_display_name text) returns public.group_summaries
language plpgsql volatile security definer set search_path = public as $$
declare
  v_code text := public.normalize_invite_code(p_invite_code);
  v_display text := public.clean_text(p_display_name, 32);
  v_group uuid;
  v_row public.group_summaries;
begin
  if auth.uid() is null then
    perform public.app_error('not_signed_in');
  end if;

  select id into v_group from public.groups where invite_code = v_code;
  if v_group is null then
    perform public.app_error('invite_invalid');
  end if;

  if not public.is_group_member(v_group)
     and (select count(*) from public.group_members where group_id = v_group) >= 50 then
    perform public.app_error('group_full');
  end if;

  insert into public.group_members (group_id, user_id, display_name, role)
  values (v_group, auth.uid(), v_display, 'member')
  on conflict (group_id, user_id) do update set display_name = excluded.display_name;

  select * into v_row from public.group_summaries where id = v_group;
  return v_row;
end $$;

-- An owner stays while others are in the group. An owner alone may leave only an empty group, which is then deleted;
-- a group with shared worlds is removed by deleting its worlds first, so no archive is left behind in storage.
create function public.leave_group(p_group uuid) returns void
language plpgsql volatile security definer set search_path = public as $$
declare
  v_role text;
begin
  perform public.require_member(p_group);
  select role into v_role from public.group_members where group_id = p_group and user_id = auth.uid();

  if v_role = 'owner' then
    if exists (select 1 from public.group_members where group_id = p_group and user_id <> auth.uid()) then
      perform public.app_error('owner_must_stay');
    end if;
    if exists (select 1 from public.worlds where group_id = p_group) then
      perform public.app_error('group_not_empty');
    end if;
    delete from public.groups where id = p_group;
    return;
  end if;

  delete from public.session_locks where group_id = p_group and user_id = auth.uid();
  delete from public.group_members where group_id = p_group and user_id = auth.uid();
end $$;

create function public.remove_member(p_group uuid, p_user uuid) returns void
language plpgsql volatile security definer set search_path = public as $$
begin
  perform public.require_owner(p_group);
  if p_user = auth.uid() then
    perform public.app_error('invalid_input');
  end if;
  delete from public.session_locks where group_id = p_group and user_id = p_user;
  delete from public.group_members where group_id = p_group and user_id = p_user;
end $$;

create function public.get_invite_code(p_group uuid) returns text
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_owner(p_group);
  return (select invite_code from public.groups where id = p_group);
end $$;

create function public.rotate_invite_code(p_group uuid) returns text
language plpgsql volatile security definer set search_path = public as $$
declare
  v_code text;
begin
  perform public.require_owner(p_group);
  for attempt in 1..5 loop
    begin
      update public.groups set invite_code = public.generate_invite_code() where id = p_group
      returning invite_code into v_code;
      return v_code;
    exception when unique_violation then
      if attempt = 5 then
        raise;
      end if;
    end;
  end loop;
  return v_code;
end $$;

create function public.rename_group(p_group uuid, p_name text) returns void
language plpgsql volatile security definer set search_path = public as $$
begin
  perform public.require_owner(p_group);
  update public.groups set name = public.clean_text(p_name, 60) where id = p_group;
end $$;

create function public.set_display_name(p_group uuid, p_display_name text) returns void
language plpgsql volatile security definer set search_path = public as $$
begin
  perform public.require_member(p_group);
  update public.group_members set display_name = public.clean_text(p_display_name, 32)
  where group_id = p_group and user_id = auth.uid();
end $$;

-- ---------------------------------------------------------------------------------------------------------------
-- Worlds, versions and locks
-- ---------------------------------------------------------------------------------------------------------------

-- Everything the app shows for one game of a group, in one call.
create function public.get_group_snapshot(p_group uuid, p_game text) returns jsonb
language plpgsql stable security definer set search_path = public as $$
begin
  perform public.require_member(p_group);
  perform public.require_game(p_game);

  return jsonb_build_object(
    'server_time', now(),
    'members', coalesce((
      select jsonb_agg(jsonb_build_object('user_id', m.user_id, 'display_name', m.display_name, 'role', m.role)
                       order by m.joined_at)
      from public.group_members m
      where m.group_id = p_group), '[]'::jsonb),
    'worlds', coalesce((
      select jsonb_agg(to_jsonb(w) || jsonb_build_object(
               'latest', (select to_jsonb(v) from public.world_versions v
                          where v.world_uuid = w.id and v.version_number = w.latest_version_number),
               'lock', (select to_jsonb(l) from public.session_locks l where l.world_uuid = w.id))
             order by w.world_name, w.world_id)
      from public.worlds w
      where w.group_id = p_group and w.game = p_game), '[]'::jsonb));
end $$;

-- Records an archive the caller has already uploaded to storage as the next version of a world (creating the world on
-- its first version). Compare-and-set: fails with version_conflict unless the world's latest version is still the one
-- the caller based the upload on, so two uploads can never both become the same version.
create function public.publish_world_version(
  p_group uuid,
  p_game text,
  p_world_id text,
  p_world_name text,
  p_expected_latest integer,
  p_version jsonb) returns public.world_versions
language plpgsql volatile security definer set search_path = public as $$
declare
  v_world uuid;
  v_latest integer;
  v_new integer;
  v_path text := p_version ->> 'archive_path';
  v_uploader text;
  v_row public.world_versions;
begin
  perform public.require_member(p_group);
  perform public.require_game(p_game);
  if p_world_id is null or p_world_id !~ '^[A-Za-z0-9][A-Za-z0-9 _.()-]{0,127}$'
     or p_expected_latest is null or p_expected_latest < 0 or p_version is null then
    perform public.app_error('invalid_input');
  end if;
  if v_path is null or position(p_group::text || '/' || p_game || '/' || p_world_id || '/' in v_path) <> 1 then
    perform public.app_error('invalid_input');
  end if;
  if not exists (select 1 from storage.objects o where o.bucket_id = 'saves' and o.name = v_path) then
    perform public.app_error('archive_missing');
  end if;

  insert into public.worlds (group_id, game, world_id, world_name)
  values (p_group, p_game, p_world_id, public.clean_text(p_world_name, 120))
  on conflict (group_id, game, world_id) do update set world_name = excluded.world_name
  returning id into v_world;

  select latest_version_number into v_latest from public.worlds where id = v_world for update;
  if v_latest <> p_expected_latest then
    perform public.app_error('version_conflict');
  end if;
  v_new := v_latest + 1;

  select display_name into v_uploader from public.group_members where group_id = p_group and user_id = auth.uid();

  insert into public.world_versions (
    world_uuid, group_id, version_number, version_id, uploaded_by, uploader_name, uploader_device,
    archive_path, archive_sha256, archive_size_bytes, source_world_modified_at,
    based_on_version_number, based_on_version_id)
  values (
    v_world, p_group, v_new, p_version ->> 'version_id', auth.uid(), v_uploader,
    left(coalesce(btrim(p_version ->> 'uploader_device'), ''), 32),
    v_path, lower(p_version ->> 'archive_sha256'), (p_version ->> 'archive_size_bytes')::bigint,
    nullif(p_version ->> 'source_world_modified_at', '')::timestamptz,
    coalesce((p_version ->> 'based_on_version_number')::integer, 0),
    coalesce(p_version ->> 'based_on_version_id', ''))
  returning * into v_row;

  perform set_config('saveharbor.rpc', 'on', true);
  update public.worlds set latest_version_number = v_new where id = v_world;
  perform set_config('saveharbor.rpc', 'off', true);

  return v_row;
end $$;

-- Takes the world's lock when it is free, expired, or already held by the same user on the same device (renewal).
-- Always returns the lock row that holds the world afterwards: the caller won only if it carries the caller's lock_id.
create function public.acquire_session_lock(
  p_world uuid,
  p_lock_id uuid,
  p_device_id uuid,
  p_device_label text,
  p_based_on_number integer,
  p_based_on_id text,
  p_ttl_seconds integer) returns public.session_locks
language plpgsql volatile security definer set search_path = public as $$
declare
  v_group uuid;
  v_latest integer;
  v_player text;
  v_ttl integer := least(greatest(coalesce(p_ttl_seconds, 1200), 60), 3600);
  v_row public.session_locks;
begin
  select group_id, latest_version_number into v_group, v_latest from public.worlds where id = p_world;
  if v_group is null then
    perform public.app_error('world_missing');
  end if;
  perform public.require_member(v_group);
  if p_lock_id is null or p_device_id is null then
    perform public.app_error('invalid_input');
  end if;
  if coalesce(p_based_on_number, -1) <> v_latest then
    perform public.app_error('version_conflict');
  end if;

  select display_name into v_player from public.group_members where group_id = v_group and user_id = auth.uid();

  insert into public.session_locks (
    world_uuid, group_id, lock_id, user_id, player_name, device_id, device_label,
    started_at, last_heartbeat_at, expires_at, based_on_version_number, based_on_version_id)
  values (
    p_world, v_group, p_lock_id, auth.uid(), v_player, p_device_id,
    left(coalesce(btrim(regexp_replace(p_device_label, '[[:cntrl:]]', '', 'g')), ''), 32),
    now(), now(), now() + make_interval(secs => v_ttl), v_latest, coalesce(p_based_on_id, ''))
  on conflict (world_uuid) do update set
    lock_id = excluded.lock_id,
    user_id = excluded.user_id,
    player_name = excluded.player_name,
    device_id = excluded.device_id,
    device_label = excluded.device_label,
    started_at = excluded.started_at,
    last_heartbeat_at = excluded.last_heartbeat_at,
    expires_at = excluded.expires_at,
    based_on_version_number = excluded.based_on_version_number,
    based_on_version_id = excluded.based_on_version_id
  where public.session_locks.expires_at < now()
     or (public.session_locks.user_id = excluded.user_id and public.session_locks.device_id = excluded.device_id)
  returning * into v_row;

  if not found then
    select * into v_row from public.session_locks where world_uuid = p_world;
  end if;
  return v_row;
end $$;

create function public.heartbeat_session_lock(p_world uuid, p_lock_id uuid, p_ttl_seconds integer)
returns public.session_locks
language plpgsql volatile security definer set search_path = public as $$
declare
  v_ttl integer := least(greatest(coalesce(p_ttl_seconds, 1200), 60), 3600);
  v_row public.session_locks;
begin
  if auth.uid() is null then
    perform public.app_error('not_signed_in');
  end if;
  update public.session_locks
  set last_heartbeat_at = now(), expires_at = now() + make_interval(secs => v_ttl)
  where world_uuid = p_world and lock_id = p_lock_id and user_id = auth.uid()
  returning * into v_row;
  if not found then
    perform public.app_error('lock_lost');
  end if;
  return v_row;
end $$;

-- True when the caller's lock was removed, false when it was already gone. Never removes another user's lock.
create function public.release_session_lock(p_world uuid, p_lock_id uuid) returns boolean
language plpgsql volatile security definer set search_path = public as $$
begin
  if auth.uid() is null then
    perform public.app_error('not_signed_in');
  end if;
  delete from public.session_locks where world_uuid = p_world and lock_id = p_lock_id and user_id = auth.uid();
  return found;
end $$;

-- Removes a world with its versions and lock. The app deletes the archives from storage first (SQL cannot).
create function public.remove_world(p_world uuid) returns void
language plpgsql volatile security definer set search_path = public as $$
declare
  v_group uuid;
begin
  select group_id into v_group from public.worlds where id = p_world;
  if v_group is null then
    perform public.app_error('world_missing');
  end if;
  perform public.require_member(v_group);
  if exists (select 1 from public.session_locks
             where world_uuid = p_world and expires_at >= now() and user_id <> auth.uid()) then
    perform public.app_error('lock_held');
  end if;
  delete from public.worlds where id = p_world;
end $$;

-- ---------------------------------------------------------------------------------------------------------------
-- Row-level security and privileges
-- ---------------------------------------------------------------------------------------------------------------

alter table public.groups enable row level security;
alter table public.group_members enable row level security;
alter table public.worlds enable row level security;
alter table public.world_versions enable row level security;
alter table public.session_locks enable row level security;
alter table public.app_meta enable row level security;

create policy groups_select_member on public.groups
  for select to authenticated using (public.is_group_member(id));

create policy group_members_select_member on public.group_members
  for select to authenticated using (public.is_group_member(group_id));

create policy worlds_select_member on public.worlds
  for select to authenticated using (public.is_group_member(group_id));
create policy worlds_update_member on public.worlds
  for update to authenticated using (public.is_group_member(group_id)) with check (public.is_group_member(group_id));

create policy world_versions_select_member on public.world_versions
  for select to authenticated using (public.is_group_member(group_id));
-- Retention may remove old versions, never the latest one.
create policy world_versions_delete_old on public.world_versions
  for delete to authenticated using (
    public.is_group_member(group_id)
    and version_number < (select w.latest_version_number from public.worlds w where w.id = world_uuid));

create policy session_locks_select_member on public.session_locks
  for select to authenticated using (public.is_group_member(group_id));

create policy app_meta_select_all on public.app_meta
  for select to anon, authenticated using (true);

-- Supabase grants everything on new objects to anon and authenticated by default; narrow it to what the app needs.
revoke all on table public.groups, public.group_members, public.worlds, public.world_versions,
  public.session_locks, public.app_meta, public.group_summaries from anon, authenticated;

grant select (id, name, created_by, created_at) on table public.groups to authenticated;
grant select on table public.group_members to authenticated;
grant select on table public.worlds to authenticated;
grant update (world_name, keep_latest_versions) on table public.worlds to authenticated;
grant select, delete on table public.world_versions to authenticated;
grant select on table public.session_locks to authenticated;
grant select on table public.app_meta to anon, authenticated;
grant select on table public.group_summaries to authenticated;

revoke all on function
  public.touch_updated_at(),
  public.guard_latest_version(),
  public.app_error(text),
  public.clean_text(text, integer),
  public.require_game(text),
  public.require_member(uuid),
  public.require_owner(uuid),
  public.generate_invite_code(),
  public.normalize_invite_code(text),
  public.is_group_member(uuid),
  public.try_uuid(text),
  public.create_group(text, text),
  public.join_group(text, text),
  public.leave_group(uuid),
  public.remove_member(uuid, uuid),
  public.get_invite_code(uuid),
  public.rotate_invite_code(uuid),
  public.rename_group(uuid, text),
  public.set_display_name(uuid, text),
  public.get_group_snapshot(uuid, text),
  public.publish_world_version(uuid, text, text, text, integer, jsonb),
  public.acquire_session_lock(uuid, uuid, uuid, text, integer, text, integer),
  public.heartbeat_session_lock(uuid, uuid, integer),
  public.release_session_lock(uuid, uuid),
  public.remove_world(uuid)
from public, anon, authenticated;

grant execute on function
  public.is_group_member(uuid),
  public.try_uuid(text),
  public.create_group(text, text),
  public.join_group(text, text),
  public.leave_group(uuid),
  public.remove_member(uuid, uuid),
  public.get_invite_code(uuid),
  public.rotate_invite_code(uuid),
  public.rename_group(uuid, text),
  public.set_display_name(uuid, text),
  public.get_group_snapshot(uuid, text),
  public.publish_world_version(uuid, text, text, text, integer, jsonb),
  public.acquire_session_lock(uuid, uuid, uuid, text, integer, text, integer),
  public.heartbeat_session_lock(uuid, uuid, integer),
  public.release_session_lock(uuid, uuid),
  public.remove_world(uuid)
to authenticated;

-- ---------------------------------------------------------------------------------------------------------------
-- Storage: private bucket "saves", objects at <group_id>/<game>/<world_id>/<version_id>.zip
-- ---------------------------------------------------------------------------------------------------------------

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('saves', 'saves', false, 47185920, array['application/zip'])
on conflict (id) do update set
  public = excluded.public,
  file_size_limit = excluded.file_size_limit,
  allowed_mime_types = excluded.allowed_mime_types;

create policy saves_select_member on storage.objects
  for select to authenticated
  using (bucket_id = 'saves' and public.is_group_member(public.try_uuid((storage.foldername(name))[1])));

create policy saves_insert_member on storage.objects
  for insert to authenticated
  with check (
    bucket_id = 'saves'
    and public.is_group_member(public.try_uuid((storage.foldername(name))[1]))
    and (storage.foldername(name))[2] in ('windrose', 'dragonwilds')
    and array_length(storage.foldername(name), 1) = 3
    and name ~ '\.zip$');

create policy saves_delete_member on storage.objects
  for delete to authenticated
  using (bucket_id = 'saves' and public.is_group_member(public.try_uuid((storage.foldername(name))[1])));

-- ---------------------------------------------------------------------------------------------------------------
-- Realtime
-- ---------------------------------------------------------------------------------------------------------------

do $$
begin
  if exists (select 1 from pg_publication where pubname = 'supabase_realtime') then
    alter publication supabase_realtime add table public.session_locks, public.worlds, public.world_versions;
  end if;
end $$;
