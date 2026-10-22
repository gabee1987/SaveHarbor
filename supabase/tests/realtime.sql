-- Live updates: lock changes stamp the world (so friends hear of them as a filtered worlds update), heartbeats do
-- not, the stamp leaves updated_at alone, and only members may use their group's live channel.
begin;
create extension if not exists pgtap with schema extensions;
select plan(14);

insert into auth.users (id, email) values
  ('f0000000-0000-4000-8000-00000000000a', 'live-a@example.invalid'),
  ('f0000000-0000-4000-8000-00000000000c', 'live-c@example.invalid');

select set_config('request.jwt.claims', '{"sub":"f0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select set_config('test.g', (public.create_group('TEST_GROUP', 'TEST_USER')).id::text, true);
reset role;

insert into storage.objects (bucket_id, name) values ('saves', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v1.zip');

select set_config('request.jwt.claims', '{"sub":"f0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select set_config('test.w', (public.publish_world_version(current_setting('test.g')::uuid, 'windrose', 'TEST_WORLD_ID',
  'Test World', 0, jsonb_build_object('version_id', 'v1',
    'archive_path', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v1.zip',
    'archive_sha256', repeat('a', 64), 'archive_size_bytes', 12345))).world_uuid::text, true);
reset role;

select is((select lock_changed_at from public.worlds where id = current_setting('test.w')::uuid), null,
  'a new world has no lock stamp');

-- An old updated_at, to see whether the stamp moves it (now() is the same for the whole transaction).
update public.worlds set updated_at = '2000-01-01T00:00:00Z' where id = current_setting('test.w')::uuid;
select is((select updated_at from public.worlds where id = current_setting('test.w')::uuid), '2000-01-01T00:00:00Z'::timestamptz,
  'setting updated_at alone keeps it');

select set_config('request.jwt.claims', '{"sub":"f0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select public.acquire_session_lock(current_setting('test.w')::uuid, '20000000-0000-4000-8000-000000000001',
  'dc000000-0000-4000-8000-000000000001', 'TEST_DEVICE', 1, 'v1', 1200);
reset role;

select isnt((select lock_changed_at from public.worlds where id = current_setting('test.w')::uuid), null,
  'taking the lock stamps the world');
select is((select updated_at from public.worlds where id = current_setting('test.w')::uuid), '2000-01-01T00:00:00Z'::timestamptz,
  'the lock stamp leaves updated_at as it was');

update public.worlds set lock_changed_at = null where id = current_setting('test.w')::uuid;
select set_config('request.jwt.claims', '{"sub":"f0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select public.heartbeat_session_lock(current_setting('test.w')::uuid, '20000000-0000-4000-8000-000000000001', 1200);
reset role;
select is((select lock_changed_at from public.worlds where id = current_setting('test.w')::uuid), null,
  'a heartbeat does not stamp the world');

select set_config('request.jwt.claims', '{"sub":"f0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select public.release_session_lock(current_setting('test.w')::uuid, '20000000-0000-4000-8000-000000000001');
reset role;
select isnt((select lock_changed_at from public.worlds where id = current_setting('test.w')::uuid), null,
  'releasing the lock stamps the world');

update public.worlds set world_name = 'Test World Renamed' where id = current_setting('test.w')::uuid;
select isnt((select updated_at from public.worlds where id = current_setting('test.w')::uuid), '2000-01-01T00:00:00Z'::timestamptz,
  'other changes still move updated_at');

-- Removing a world with a lock cascades to the lock; the stamp then finds no world and must not fail.
select set_config('request.jwt.claims', '{"sub":"f0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select public.acquire_session_lock(current_setting('test.w')::uuid, '20000000-0000-4000-8000-000000000002',
  'dc000000-0000-4000-8000-000000000001', 'TEST_DEVICE', 1, 'v1', 1200);
select lives_ok($$select public.remove_world(current_setting('test.w')::uuid)$$, 'a locked world can still be removed');

select ok(public.can_use_realtime_topic('saveharbor:' || current_setting('test.g')), 'a member may use the group''s channel');
select ok(not public.can_use_realtime_topic('saveharbor:' || gen_random_uuid()::text), 'not the channel of another group');
select ok(not public.can_use_realtime_topic('saveharbor:TEST_GROUP'), 'not a channel without a group id');
select ok(not public.can_use_realtime_topic('other:' || current_setting('test.g')), 'not a channel of another kind');
reset role;

select set_config('request.jwt.claims', '{"sub":"f0000000-0000-4000-8000-00000000000c","role":"authenticated"}', true);
set local role authenticated;
select ok(not public.can_use_realtime_topic('saveharbor:' || current_setting('test.g')), 'a non-member may not');
reset role;

set local role anon;
select throws_ok($$select public.can_use_realtime_topic('saveharbor:' || current_setting('test.g'))$$, '42501', null,
  'signed-out clients cannot call the check');
reset role;

select * from finish();
rollback;
