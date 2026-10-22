-- SaveHarbor Cloud live updates (app 2.3.x, CP7).
--
-- 1. Lock changes reach friends as a worlds UPDATE. Realtime can filter inserts and updates by group_id, but not
--    deletes: a released lock (a delete on session_locks) would go to every connected app of every group. Instead a
--    lock taken, taken over or released stamps worlds.lock_changed_at, and the app listens to worlds only.
-- 2. The live channel saveharbor:<group id> is private: only members of that group may join it and share presence
--    (who has SaveHarbor open). The project's Realtime setting "Allow public access" must be off (owner step).

alter table public.worlds add column lock_changed_at timestamptz;

-- updated_at keeps meaning "the world or its versions changed": a lock stamp alone leaves it as it was.
create or replace function public.touch_updated_at() returns trigger
language plpgsql set search_path = public as $$
begin
  if (to_jsonb(new) - 'updated_at' - 'lock_changed_at') is distinct from (to_jsonb(old) - 'updated_at' - 'lock_changed_at') then
    new.updated_at = now();
  end if;
  return new;
end $$;

-- Heartbeats only move expires_at, so they do not stamp the world: no live message every few minutes per player.
create function public.stamp_world_lock_change() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  update public.worlds set lock_changed_at = now()
  where id = case when tg_op = 'DELETE' then old.world_uuid else new.world_uuid end;
  return null;
end $$;

revoke all on function public.stamp_world_lock_change() from public, anon, authenticated;

create trigger session_locks_stamp_world after insert or delete or update of lock_id on public.session_locks
  for each row execute function public.stamp_world_lock_change();

-- Whether the caller may use a live channel: saveharbor:<group id> of a group they belong to. The policies below
-- call it with (select realtime.topic()), so it runs once per check instead of once per row.
create function public.can_use_realtime_topic(p_topic text) returns boolean
language sql stable security definer set search_path = public as $$
  select coalesce(p_topic like 'saveharbor:%' and public.is_group_member(public.try_uuid(substr(p_topic, 12))), false);
$$;

revoke all on function public.can_use_realtime_topic(text) from public, anon, authenticated;
grant execute on function public.can_use_realtime_topic(text) to authenticated;

-- The realtime schema exists on Supabase and in the local stack, not in a plain Postgres.
do $$
begin
  if to_regclass('realtime.messages') is not null then
    execute $policy$
      create policy saveharbor_members_receive on realtime.messages
        for select to authenticated
        using (public.can_use_realtime_topic((select realtime.topic())))
    $policy$;
    -- Presence only: the app never broadcasts, so members cannot send other messages through the channel.
    execute $policy$
      create policy saveharbor_members_track_presence on realtime.messages
        for insert to authenticated
        with check (realtime.messages.extension = 'presence' and public.can_use_realtime_topic((select realtime.topic())))
    $policy$;
  end if;
end $$;
