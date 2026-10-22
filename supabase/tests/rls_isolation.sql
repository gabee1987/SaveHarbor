-- Row-level security and privileges: groups cannot see each other, invite codes stay with owners, anon sees only app_meta.
begin;
create extension if not exists pgtap with schema extensions;
select plan(26);

insert into auth.users (id, email) values
  ('c0000000-0000-4000-8000-00000000000a', 'rls-a@example.invalid'),
  ('c0000000-0000-4000-8000-00000000000b', 'rls-b@example.invalid'),
  ('c0000000-0000-4000-8000-00000000000c', 'rls-c@example.invalid');

-- Group 1: A (owner) and B. Group 2: C alone. A shares a world in group 1 and holds its lock.
select set_config('request.jwt.claims', '{"sub":"c0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select set_config('test.g1', (public.create_group('TEST_GROUP_1', 'TEST_USER_A')).id::text, true);
select set_config('test.code', public.get_invite_code(current_setting('test.g1')::uuid), true);
reset role;

select set_config('request.jwt.claims', '{"sub":"c0000000-0000-4000-8000-00000000000b","role":"authenticated"}', true);
set local role authenticated;
select public.join_group(current_setting('test.code'), 'TEST_USER_B');
reset role;

select set_config('request.jwt.claims', '{"sub":"c0000000-0000-4000-8000-00000000000c","role":"authenticated"}', true);
set local role authenticated;
select set_config('test.g2', (public.create_group('TEST_GROUP_2', 'TEST_USER_C')).id::text, true);
reset role;

insert into storage.objects (bucket_id, name) values ('saves', current_setting('test.g1') || '/windrose/TEST_WORLD_ID/v1.zip');

select set_config('request.jwt.claims', '{"sub":"c0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select set_config('test.w', (public.publish_world_version(current_setting('test.g1')::uuid, 'windrose', 'TEST_WORLD_ID',
  'Test World', 0, jsonb_build_object('version_id', 'v1',
    'archive_path', current_setting('test.g1') || '/windrose/TEST_WORLD_ID/v1.zip',
    'archive_sha256', repeat('a', 64), 'archive_size_bytes', 12345))).world_uuid::text, true);
select public.acquire_session_lock(current_setting('test.w')::uuid, '20000000-0000-4000-8000-000000000001',
  'da000000-0000-4000-8000-000000000001', 'TEST_DEVICE_A', 1, 'v1', 1200);
reset role;

-- C, a member of another group, sees nothing of group 1.
select set_config('request.jwt.claims', '{"sub":"c0000000-0000-4000-8000-00000000000c","role":"authenticated"}', true);
set local role authenticated;
select is((select count(*)::integer from public.group_summaries where id = current_setting('test.g1')::uuid), 0,
  'other groups are invisible');
select is((select count(*)::integer from public.worlds), 0, 'other groups'' worlds are invisible');
select is((select count(*)::integer from public.world_versions), 0, 'other groups'' versions are invisible');
select is((select count(*)::integer from public.session_locks), 0, 'other groups'' locks are invisible');
select is((select count(*)::integer from public.group_members where group_id = current_setting('test.g1')::uuid), 0,
  'other groups'' members are invisible');
select throws_ok($$select public.get_group_snapshot(current_setting('test.g1')::uuid, 'windrose')$$,
  'P0001', 'not_member', 'non-members cannot read a snapshot');
select throws_ok($$select invite_code from public.groups$$, '42501', null, 'invite codes cannot be selected');
select is((select count(*)::integer from storage.objects where bucket_id = 'saves'), 0,
  'other groups'' archives are invisible');
select throws_ok($$select public.join_group('WRONGCODE2', 'TEST_USER_C')$$, 'P0001', 'invite_invalid',
  'an unknown invite code is rejected');
select throws_ok($$select public.get_invite_code(current_setting('test.g1')::uuid)$$, 'P0001', 'not_member',
  'non-members cannot read the invite code');
reset role;

-- A, the owner of group 1.
select set_config('request.jwt.claims', '{"sub":"c0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select throws_ok($$select invite_code from public.groups$$, '42501', null,
  'even owners read the invite code only through get_invite_code');
select is(char_length(public.get_invite_code(current_setting('test.g1')::uuid)), 10, 'owners get the invite code');
select is((select member_count from public.group_summaries where id = current_setting('test.g1')::uuid), 2,
  'member count');
select is((select my_role from public.group_summaries where id = current_setting('test.g1')::uuid), 'owner',
  'own role');
select is((select count(*)::integer from storage.objects where bucket_id = 'saves'), 1, 'members see their archives');
select throws_ok($$select public.leave_group(current_setting('test.g1')::uuid)$$, 'P0001', 'owner_must_stay',
  'the owner cannot leave while others are in the group');
select throws_ok(
  $$insert into public.group_members (group_id, user_id, display_name) values
    (current_setting('test.g2')::uuid, 'c0000000-0000-4000-8000-00000000000a', 'INTRUDER')$$,
  '42501', null, 'nobody joins a group by inserting a membership row');
reset role;

-- B, a plain member of group 1.
select set_config('request.jwt.claims', '{"sub":"c0000000-0000-4000-8000-00000000000b","role":"authenticated"}', true);
set local role authenticated;
select throws_ok($$select public.get_invite_code(current_setting('test.g1')::uuid)$$, 'P0001', 'not_owner',
  'members cannot read the invite code');
select throws_ok($$select public.rotate_invite_code(current_setting('test.g1')::uuid)$$, 'P0001', 'not_owner',
  'members cannot rotate the invite code');
select lives_ok($$select public.leave_group(current_setting('test.g1')::uuid)$$, 'a member can leave');
select is((select count(*)::integer from public.group_summaries), 0, 'after leaving, the group is invisible');
reset role;

-- Anonymous callers (only the publishable key, no sign-in).
select set_config('request.jwt.claims', '{"role":"anon"}', true);
set local role anon;
select isnt_empty($$select key from public.app_meta$$, 'anon can read app_meta');
select throws_ok($$select id from public.worlds$$, '42501', null, 'anon cannot read worlds');
select throws_ok($$select public.get_group_snapshot(current_setting('test.g1')::uuid, 'windrose')$$, '42501', null,
  'anon cannot call group functions');
reset role;

-- C joins group 1 with the code typed loosely (lower case, dash).
select set_config('request.jwt.claims', '{"sub":"c0000000-0000-4000-8000-00000000000c","role":"authenticated"}', true);
set local role authenticated;
select is(
  (public.join_group(lower(substr(current_setting('test.code'), 1, 5)) || '-' || lower(substr(current_setting('test.code'), 6)),
    'TEST_USER_C')).member_count,
  2, 'invite codes are accepted in any case and with separators');
select is((select count(*)::integer from public.group_summaries), 2, 'C now sees both of its groups');
reset role;

select * from finish();
rollback;
