# Findings (Enshrouded)

One line per unrelated defect or surprise met during implementation. Do not fix here; the owner triages.

- 2026-10-28 (CP0): migration `20261028000000_enshrouded_game.sql` applied to the local stack with `supabase migration up` (no reset); `supabase test db` passed 90/90 (games.sql now 16 assertions). Web: `npm test` 56/56, `npm run build` ok. The CLI is the repo-root dev dependency (`node_modules\.bin\supabase.ps1`), not on PATH. Process name `enshrouded` confirmed by the owner.

- 2026-10-28 (CP1): the shell is chosen by `GameShellTemplateConverter` on `ActiveGameId` instead of the plan's `DataTemplateSelector`: the window's content (the view model) does not change on a game switch, so a selector would never re-run. Adding a game still adds one `DataTemplate` to `MainWindow.xaml`, but no trigger, and a game without a template throws instead of silently showing Windrose. Until CP4 Enshrouded points `ThemeDictionary`/`SkinDictionary` and its shell at the Windrose skin.

## Build baseline

CP1 start: 0 warnings, 542 passed, 1 skipped (opt-in Supabase stack test). CP1 end: 0 warnings, 557 passed, 1 skipped.

## World name spike (T2.6)

(record outcome)
