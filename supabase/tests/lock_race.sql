-- Session locks: one winner, renewal by the same device, take-over after expiry, heartbeat and release ownership.
-- True concurrency (two requests at the same instant) is covered by supabase/scripts/smoke-test.ps1.
begin;
create extension if not exists pgtap with schema extensions;
select plan(19);

insert into auth.users (id, email) values
  ('b0000000-0000-4000-8000-00000000000a', 'lock-a@example.invalid'),
  ('b0000000-0000-4000-8000-00000000000b', 'lock-b@example.invalid'),
  ('b0000000-0000-4000-8000-00000000000c', 'lock-c@example.invalid');

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select set_config('test.g', (public.create_group('TEST_GROUP', 'TEST_USER_A')).id::text, true);
select set_config('test.code', public.get_invite_code(current_setting('test.g')::uuid), true);
reset role;

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000b","role":"authenticated"}', true);
set local role authenticated;
select public.join_group(current_setting('test.code'), 'TEST_USER_B');
reset role;

insert into storage.objects (bucket_id, name) values ('saves', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v1.zip');

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select set_config('test.w', (public.publish_world_version(current_setting('test.g')::uuid, 'windrose', 'TEST_WORLD_ID',
  'Test World', 0, jsonb_build_object('version_id', 'v1',
    'archive_path', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v1.zip',
    'archive_sha256', repeat('a', 64), 'archive_size_bytes', 12345))).world_uuid::text, true);

select is(
  (public.acquire_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000001',
    'da000000-0000-4000-8000-000000000001', 'TEST_DEVICE_A', 1, 'v1', 1200)).lock_id,
  '10000000-0000-4000-8000-000000000001'::uuid, 'A takes the free lock');
reset role;

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000b","role":"authenticated"}', true);
set local role authenticated;
select is(
  (public.acquire_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000002',
    'db000000-0000-4000-8000-000000000001', 'TEST_DEVICE_B', 1, 'v1', 1200)).lock_id,
  '10000000-0000-4000-8000-000000000001'::uuid, 'B gets the holder''s lock back while it is live');
select is(
  (public.acquire_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000002',
    'da000000-0000-4000-8000-000000000001', 'TEST_DEVICE_B', 1, 'v1', 1200)).lock_id,
  '10000000-0000-4000-8000-000000000001'::uuid, 'B cannot win by copying A''s device id');
select throws_ok(
  $$select public.heartbeat_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000001', 1200)$$,
  'P0001', 'lock_lost', 'B cannot extend A''s lock');
reset role;

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select is(
  (public.acquire_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000003',
    'da000000-0000-4000-8000-000000000001', 'TEST_DEVICE_A', 1, 'v1', 1200)).lock_id,
  '10000000-0000-4000-8000-000000000003'::uuid, 'the same user on the same device renews its own lock');
reset role;

-- A's PC crashed: the lock expires.
update public.session_locks set expires_at = now() - interval '1 minute';

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000b","role":"authenticated"}', true);
set local role authenticated;
select is(
  (public.acquire_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000004',
    'db000000-0000-4000-8000-000000000001', 'TEST_DEVICE_B', 1, 'v1', 1200)).lock_id,
  '10000000-0000-4000-8000-000000000004'::uuid, 'B takes over an expired lock');
select is(
  (select player_name from public.session_locks),
  'TEST_USER_B', 'the lock shows the holder''s group display name');
reset role;

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
  $$select public.heartbeat_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000003', 1200)$$,
  'P0001', 'lock_lost', 'A''s heartbeat after the take-over reports a lost lock');
select is(
  public.release_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000003'),
  false, 'A cannot release B''s lock with its old lock id');
select throws_ok($$select public.remove_world(current_setting('test.w')::uuid)$$, 'P0001', 'lock_held',
  'a world cannot be removed while another player holds its lock');
reset role;

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000b","role":"authenticated"}', true);
set local role authenticated;
select ok(
  (public.heartbeat_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000004', 1200)).expires_at > now(),
  'the holder''s heartbeat extends the lock');
select is(
  public.release_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000004'),
  true, 'the holder releases its lock');
select is(
  public.release_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000004'),
  false, 'releasing twice reports that the lock was already gone');
reset role;

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
  $$select public.acquire_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000005',
    'da000000-0000-4000-8000-000000000001', 'TEST_DEVICE_A', 0, '', 1200)$$,
  'P0001', 'version_conflict', 'a player who is not on the latest version cannot lock');
select ok(
  (public.acquire_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000005',
    'da000000-0000-4000-8000-000000000001', 'TEST_DEVICE_A', 1, 'v1', 999999)).expires_at <= now() + interval '3600 seconds',
  'the lock lifetime is capped at one hour');
select is(
  public.get_group_snapshot(current_setting('test.g')::uuid, 'windrose') -> 'worlds' -> 0 -> 'lock' ->> 'lock_id',
  '10000000-0000-4000-8000-000000000005', 'the snapshot shows the current lock');
reset role;

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000c","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
  $$select public.acquire_session_lock(current_setting('test.w')::uuid, '10000000-0000-4000-8000-000000000006',
    'dc000000-0000-4000-8000-000000000001', 'TEST_DEVICE_C', 1, 'v1', 1200)$$,
  'P0001', 'not_member', 'non-members cannot lock');
reset role;

select set_config('request.jwt.claims', '{"sub":"b0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select lives_ok($$select public.remove_world(current_setting('test.w')::uuid)$$,
  'a player may remove a world while holding its lock');
select is((select count(*)::integer from public.session_locks), 0, 'removing the world removes its lock');
reset role;

select * from finish();
rollback;
