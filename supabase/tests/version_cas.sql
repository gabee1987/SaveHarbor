-- publish_world_version: server-assigned version numbers with compare-and-set, archive checks, retention rules.
begin;
create extension if not exists pgtap with schema extensions;
select plan(13);

insert into auth.users (id, email) values
  ('a0000000-0000-4000-8000-00000000000a', 'cas-a@example.invalid'),
  ('a0000000-0000-4000-8000-00000000000b', 'cas-b@example.invalid'),
  ('a0000000-0000-4000-8000-00000000000c', 'cas-c@example.invalid');

-- User A creates the group, user B joins it.
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select set_config('test.g', (public.create_group('TEST_GROUP', 'TEST_USER_A')).id::text, true);
select set_config('test.code', public.get_invite_code(current_setting('test.g')::uuid), true);
reset role;

select set_config('request.jwt.claims', '{"sub":"a0000000-0000-4000-8000-00000000000b","role":"authenticated"}', true);
set local role authenticated;
select public.join_group(current_setting('test.code'), 'TEST_USER_B');
reset role;

-- The archives exist in storage before their versions are published (the app uploads first).
insert into storage.objects (bucket_id, name) values
  ('saves', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v1.zip'),
  ('saves', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v2.zip');

-- A publishes v1.
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select is(
  (public.publish_world_version(current_setting('test.g')::uuid, 'windrose', 'TEST_WORLD_ID', 'Test World', 0,
    jsonb_build_object('version_id', 'v1', 'archive_path', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v1.zip',
      'archive_sha256', repeat('a', 64), 'archive_size_bytes', 12345))).version_number,
  1, 'first publish becomes version 1');
reset role;

-- B still believes the latest is 0: rejected. B then bases on 1: accepted as 2.
select set_config('request.jwt.claims', '{"sub":"a0000000-0000-4000-8000-00000000000b","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
  $$select public.publish_world_version(current_setting('test.g')::uuid, 'windrose', 'TEST_WORLD_ID', 'Test World', 0,
    jsonb_build_object('version_id', 'v2', 'archive_path', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v2.zip',
      'archive_sha256', repeat('b', 64), 'archive_size_bytes', 12345))$$,
  'P0001', 'version_conflict', 'publish based on an old version is rejected');
select is(
  (public.publish_world_version(current_setting('test.g')::uuid, 'windrose', 'TEST_WORLD_ID', 'Test World', 1,
    jsonb_build_object('version_id', 'v2', 'archive_path', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v2.zip',
      'archive_sha256', repeat('b', 64), 'archive_size_bytes', 12345, 'uploader_name', 'SPOOFED',
      'based_on_version_number', 1, 'based_on_version_id', 'v1'))).version_number,
  2, 'publish based on the latest version becomes the next number');
select is(
  (select uploader_name from public.world_versions where version_number = 2),
  'TEST_USER_B', 'uploader name comes from the group membership, not from the client');
reset role;

select set_config('request.jwt.claims', '{"sub":"a0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select is((select latest_version_number from public.worlds), 2, 'world latest version follows publishes');
select throws_ok($$update public.worlds set latest_version_number = 9$$, '42501', null,
  'clients cannot change the latest version directly');
select lives_ok($$update public.worlds set world_name = 'Renamed World'$$, 'members may rename a world');
select throws_ok(
  $$select public.publish_world_version(current_setting('test.g')::uuid, 'windrose', 'TEST_WORLD_ID', 'Test World', 2,
    jsonb_build_object('version_id', 'v3', 'archive_path', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v3.zip',
      'archive_sha256', repeat('c', 64), 'archive_size_bytes', 12345))$$,
  'P0001', 'archive_missing', 'a version needs its archive in storage');
select throws_ok(
  $$select public.publish_world_version(current_setting('test.g')::uuid, 'windrose', 'TEST_WORLD_ID', 'Test World', 2,
    jsonb_build_object('version_id', 'v3', 'archive_path', 'another-group/windrose/TEST_WORLD_ID/v3.zip',
      'archive_sha256', repeat('c', 64), 'archive_size_bytes', 12345))$$,
  'P0001', 'invalid_input', 'archive path must be inside the group and world folder');

-- Retention: the latest version can never be deleted, older ones can.
delete from public.world_versions where version_number = 2;
select is((select count(*)::integer from public.world_versions), 2, 'the latest version survives a delete');
delete from public.world_versions where version_number = 1;
select is((select count(*)::integer from public.world_versions), 1, 'an older version can be removed');
reset role;

select throws_ok($$update public.worlds set latest_version_number = 9$$, 'P0001', 'direct_update_forbidden',
  'even the table owner cannot move the latest version outside publish');

select set_config('request.jwt.claims', '{"sub":"a0000000-0000-4000-8000-00000000000c","role":"authenticated"}', true);
set local role authenticated;
select throws_ok(
  $$select public.publish_world_version(current_setting('test.g')::uuid, 'windrose', 'TEST_WORLD_ID', 'Test World', 2,
    jsonb_build_object('version_id', 'v3', 'archive_path', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v2.zip',
      'archive_sha256', repeat('c', 64), 'archive_size_bytes', 12345))$$,
  'P0001', 'not_member', 'non-members cannot publish');
reset role;

select * from finish();
rollback;
