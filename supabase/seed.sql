-- Local development data only (`supabase db reset` applies it). Never runs against the hosted project.
-- Two test users who can sign in with an e-mail code (read the codes in Mailpit at http://127.0.0.1:54324)
-- and one group they both belong to. Invite code for joining with a third address: TESTCODE22.

insert into auth.users (
  instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
  raw_app_meta_data, raw_user_meta_data, created_at, updated_at,
  confirmation_token, recovery_token, email_change_token_new, email_change)
values
  ('00000000-0000-0000-0000-000000000000', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'authenticated', 'authenticated',
   'test-a@example.invalid', '', now(), '{"provider":"email","providers":["email"]}', '{}', now(), now(), '', '', '', ''),
  ('00000000-0000-0000-0000-000000000000', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'authenticated', 'authenticated',
   'test-b@example.invalid', '', now(), '{"provider":"email","providers":["email"]}', '{}', now(), now(), '', '', '', '');

insert into auth.identities (id, user_id, provider_id, identity_data, provider, last_sign_in_at, created_at, updated_at)
select gen_random_uuid(), u.id, u.id::text,
       jsonb_build_object('sub', u.id::text, 'email', u.email, 'email_verified', true),
       'email', now(), now(), now()
from auth.users u
where u.email in ('test-a@example.invalid', 'test-b@example.invalid');

insert into public.groups (id, name, invite_code, created_by)
values ('11111111-1111-4111-8111-111111111111', 'TEST_GROUP', 'TESTCODE22', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');

insert into public.group_members (group_id, user_id, display_name, role)
values
  ('11111111-1111-4111-8111-111111111111', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'TEST_USER_A', 'owner'),
  ('11111111-1111-4111-8111-111111111111', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', 'TEST_USER_B', 'member');
