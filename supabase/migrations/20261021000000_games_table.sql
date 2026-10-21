-- SaveHarbor Cloud: the games a group can share live in one table instead of three fixed lists.
-- Before this migration 'windrose' and 'dragonwilds' were spelled out in the worlds.game check, in require_game()
-- and in the storage upload policy. Adding a game is now one row in a new migration:
--   insert into public.games (key) values ('<key>');
-- The key must equal GameIdExtensions.ToStorageKey in the app. Never delete a row that worlds still reference.

create table public.games (
  key text primary key check (key ~ '^[a-z][a-z0-9-]{1,31}$'),
  created_at timestamptz not null default now()
);

insert into public.games (key) values ('windrose'), ('dragonwilds');

-- Clients never read the table; they reach it only through is_known_game().
alter table public.games enable row level security;
revoke all on table public.games from anon, authenticated;

-- Security definer, so the storage policy below can call it without giving clients access to public.games.
create function public.is_known_game(p_game text) returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.games g where g.key = p_game);
$$;

revoke all on function public.is_known_game(text) from public, anon, authenticated;
grant execute on function public.is_known_game(text) to authenticated;

alter table public.worlds drop constraint worlds_game_check;
alter table public.worlds add constraint worlds_game_fkey
  foreign key (game) references public.games(key) on update restrict on delete restrict;

-- Same contract as before (invalid_input for an unknown game); stable now, because it reads a table.
create or replace function public.require_game(p_game text) returns void
language plpgsql stable set search_path = public as $$
begin
  if p_game is null or not public.is_known_game(p_game) then
    raise exception using errcode = 'P0001', message = 'invalid_input';
  end if;
end $$;

drop policy saves_insert_member on storage.objects;
create policy saves_insert_member on storage.objects
  for insert to authenticated
  with check (
    bucket_id = 'saves'
    and public.is_group_member(public.try_uuid((storage.foldername(name))[1]))
    and public.is_known_game((storage.foldername(name))[2])
    and array_length(storage.foldername(name), 1) = 3
    and name ~ '\.zip$');
