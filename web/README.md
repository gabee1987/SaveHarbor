# SaveHarbor companion page

A read-only page for the phone: who is playing which world, the newest version of each, the version history and who
has SaveHarbor open. It cannot play, upload, download or release a lock; that stays in the desktop app. Plan:
`docs/plans/cloud-v2/08-companion-web.md`.

## Run it locally

Needs Node.js 24 and the local Supabase stack (`npm run db:start` in the repository root; see
`docs/runbooks/supabase.md`).

```powershell
cd web
npm ci
Copy-Item .env.example .env.local   # then set the local URL and the publishable key from `npx supabase status`
npm run dev                         # http://localhost:5287/SaveHarbor/
```

Sign in with a seed address (`test-a@example.invalid`); the code arrives in Mailpit at http://127.0.0.1:54324.

## Checks

```powershell
npm test         # Vitest: snapshot mapping, lock text, formats, routes, presence, CSP
npm run build    # type check, then dist/ (needs VITE_SUPABASE_URL)
```

## Rules

- Dependencies use exact versions; `.npmrc` turns install scripts off. Add a package only when the page cannot do
  without it, and check its size: the bundle stays under 150 kB gzipped.
- Only public values in `.env*` files: the project URL and the publishable key. Never a secret key.
- No e-mail addresses on screen; members appear by display name.
- Colours come from the desktop themes; see the comment at the top of `src/styles.css`.
