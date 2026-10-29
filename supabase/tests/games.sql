-- The games table: only listed games can hold worlds or archives, clients cannot read or change the list, and a game
-- added later works everywhere without touching functions or policies.
begin;
create extension if not exists pgtap with schema extensions;
select plan(16);

insert into auth.users (id, email) values
  ('e0000000-0000-4000-8000-00000000000a', 'games-a@example.invalid');

select results_eq($$select key from public.games order by key$$, array['dragonwilds', 'enshrouded', 'windrose'],
  'windrose, dragonwilds and enshrouded are the listed games');
select throws_ok($$insert into public.games (key) values ('Not A Key')$$, '23514', null,
  'game keys are lowercase without spaces');
select throws_ok($$insert into public.worlds (group_id, game, world_id, world_name)
  values (gen_random_uuid(), 'unknown-game', 'TEST_WORLD_ID', 'Test World')$$, '23503', null,
  'worlds need a listed game');

select set_config('request.jwt.claims', '{"sub":"e0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select set_config('test.g', (public.create_group('TEST_GROUP', 'TEST_USER')).id::text, true);

select throws_ok($$select key from public.games$$, '42501', null, 'clients cannot read the games table');
select throws_ok($$insert into public.games (key) values ('test-game')$$, '42501', null,
  'clients cannot add games');
select throws_ok($$select public.get_group_snapshot(current_setting('test.g')::uuid, 'test-game')$$,
  'P0001', 'invalid_input', 'an unlisted game is rejected by the functions');
select throws_ok($$insert into storage.objects (bucket_id, name)
  values ('saves', current_setting('test.g') || '/test-game/TEST_WORLD_ID/v1.zip')$$, '42501', null,
  'archives of an unlisted game cannot be uploaded');
select lives_ok($$insert into storage.objects (bucket_id, name)
  values ('saves', current_setting('test.g') || '/windrose/TEST_WORLD_ID/v1.zip')$$,
  'archives of a listed game can be uploaded');
select lives_ok($$insert into storage.objects (bucket_id, name)
  values ('saves', current_setting('test.g') || '/enshrouded/TEST_WORLD_ID/v1.zip')$$,
  'enshrouded archives can be uploaded');
select is(jsonb_array_length(public.get_group_snapshot(current_setting('test.g')::uuid, 'enshrouded') -> 'worlds'), 0,
  'enshrouded works in the functions');
reset role;

-- A game added by a later migration.
insert into public.games (key) values ('test-game');

select set_config('request.jwt.claims', '{"sub":"e0000000-0000-4000-8000-00000000000a","role":"authenticated"}', true);
set local role authenticated;
select lives_ok($$select public.get_group_snapshot(current_setting('test.g')::uuid, 'test-game')$$,
  'a new game works in the functions');
select lives_ok($$insert into storage.objects (bucket_id, name)
  values ('saves', current_setting('test.g') || '/test-game/TEST_WORLD_ID/v1.zip')$$,
  'a new game accepts archives');
select is((public.publish_world_version(current_setting('test.g')::uuid, 'test-game', 'TEST_WORLD_ID',
  'Test World', 0, jsonb_build_object('version_id', 'v1',
    'archive_path', current_setting('test.g') || '/test-game/TEST_WORLD_ID/v1.zip',
    'archive_sha256', repeat('a', 64), 'archive_size_bytes', 12345))).version_number, 1,
  'a new game can publish a version');
select is(jsonb_array_length(public.get_group_snapshot(current_setting('test.g')::uuid, 'test-game') -> 'worlds'), 1,
  'the new game has its own world');
select is(jsonb_array_length(public.get_group_snapshot(current_setting('test.g')::uuid, 'windrose') -> 'worlds'), 0,
  'the same world id in another game is a separate world');
reset role;

select throws_ok($$delete from public.games where key = 'test-game'$$, '23503', null,
  'a game with worlds cannot be removed');

select * from finish();
rollback;
