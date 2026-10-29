-- SaveHarbor Cloud: Enshrouded becomes the third game a group can share. The key equals GameIdExtensions.ToStorageKey
-- in the app. Functions, the worlds.game foreign key and the storage upload policy all read public.games
-- (migration 20261021000000_games_table.sql), so nothing else changes.
insert into public.games (key) values ('enshrouded');
