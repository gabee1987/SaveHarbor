# Findings (Enshrouded)

One line per unrelated defect or surprise met during implementation. Do not fix here; the owner triages.

- 2026-10-28 (CP0): migration `20261028000000_enshrouded_game.sql` applied to the local stack with `supabase migration up` (no reset); `supabase test db` passed 90/90 (games.sql now 16 assertions). Web: `npm test` 56/56, `npm run build` ok. The CLI is the repo-root dev dependency (`node_modules\.bin\supabase.ps1`), not on PATH. Process name `enshrouded` confirmed by the owner.

## Build baseline

(record warning count at CP1)

## World name spike (T2.6)

(record outcome)
