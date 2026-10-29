# Plan 03 — Backend (SaveHarbor Cloud) and Companion Web Page

**Branch:** `feature/enshrouded-support`
**Depends on:** nothing. Do this first: the app's uploads for the new game are refused with `invalid_input` until the migration is live.
**Skills:** `multi-game-separation`, `coding-standards`.

## 1. Supabase

### 1.1 Migration
`supabase/migrations/20261028000000_enshrouded_game.sql`:
```sql
-- Enshrouded becomes the third game SaveHarbor groups can share. The key equals GameIdExtensions.ToStorageKey.
insert into public.games (key) values ('enshrouded');
```
Nothing else: `worlds.game`, `require_game()`, `is_known_game()` and the storage upload policy all read `public.games` (migration `20261021000000_games_table.sql`).

### 1.2 Tests
- `supabase/tests/games.sql`: the initial-list assertion becomes `array['dragonwilds', 'enshrouded', 'windrose']` and the plan count stays. Add one `lives_ok` upload of `<group>/enshrouded/TEST_WORLD_ID/v1.zip` and one `get_group_snapshot(..., 'enshrouded')` call.
- Run the pgTAP suite locally (`supabase test db`) before the owner pushes.

### 1.3 Limits
- `saves` bucket: 45 MiB per archive, `application/zip` only. An Enshrouded world is a few MB; no change.
- No per-game quota exists. If the group shares several games, storage grows per game; mention in the release notes only.

### 1.4 Ordering with the app release
1. Owner pushes the migration (`supabase db push`) to the hosted project.
2. Only then release the app version that knows `GameId.Enshrouded`.
An older app never sends `enshrouded`, so there is no compatibility issue the other way round.

## 2. Companion web page (`web/`)

| File | Change |
|---|---|
| `src/lib/games.ts` | Add `{ key: 'enshrouded', name: 'Enshrouded' }` |
| `src/styles.css` | Add `:root[data-game='enshrouded']` dark and light blocks (values from the `enshrouded-theme` skill; keep the "update together with the desktop theme" comment) |
| `public/about.html` | Supported games sentence: "Windrose, RuneScape: Dragonwilds and Enshrouded on Windows (Steam)" |
| `src/lib/router.test.ts`, `src/lib/presence.test.ts` | Add the game where the tests enumerate games |
| `src/App.tsx`, `src/lib/router.ts`, `src/pages/GroupsPage.tsx`, `index.html` | No change (Windrose stays the default) |

Group pages, invites, presence and Google sign-in are game-agnostic; verify by opening a group for the new game key after the migration (`?game=enshrouded`, or however the router carries it).

## 3. Google Drive provider

No code change: the per-game shared folder model (`SharedFolders: { windrose, dragonwilds, enshrouded }`) and the marker file `saveharbor-game.json` take the key from `IGameDefinition.StorageKey`. Verify once that `CloudProviderSettingsService` does not enumerate games by hand (the Dragonwilds handoff noted it only has Windrose legacy defaults, which is fine).

## 4. Acceptance criteria

- [ ] pgTAP suite passes with the new row.
- [ ] A world of game `enshrouded` can be published and listed through `publish_world_version` / `get_group_snapshot`; the same world id under `windrose` is a separate world.
- [ ] The companion page shows the game name and its colours; the Windrose default is unchanged.
