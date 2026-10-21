# SaveHarbor

SaveHarbor is a Windows desktop app that backs up, restores and shares world saves for
**Windrose** and **RuneScape: Dragonwilds**, either through **SaveHarbor Cloud** (sign in with an e-mail code, share
in a group) or through a shared Google Drive folder. Installed copies update themselves from GitHub Releases.

## Requirements (development)

- Windows 10 or 11
- .NET SDK 10.0.401 or later (pinned in `global.json`)

## Everyday commands

Run these from the repository root (`C:\Coding\SaveHarbor`).

```powershell
# Build
dotnet build SaveHarbor.sln -c Debug

# Run all tests
dotnet test SaveHarbor.sln --no-build

# Start the app (optionally choose the game to open first)
dotnet run --project SaveHarbor.App -c Debug -- --game dragonwilds
dotnet run --project SaveHarbor.App -c Debug -- --game windrose
```

If the build fails because `SaveHarbor.App.exe` is in use, close the running app first. A copy started with
`dotnet run` does not update itself; Settings says "Updates are available in the installed version."

## Local SaveHarbor Cloud (Supabase in Docker)

The SaveHarbor Cloud backend runs completely on your PC for development and testing.
It needs Docker Desktop and Node.js; the Supabase CLI is pinned in `package.json`.

```powershell
npm install                          # once
npm run db:start                     # start the stack (first start downloads the images)
npm run db:test                      # database tests
pwsh supabase/scripts/smoke-test.ps1 # end-to-end check over the real APIs
npm run db:stop                      # stop the stack
```

Studio runs at `http://127.0.0.1:54323` and sign-in codes arrive in Mailpit at `http://127.0.0.1:54324`.
Details, test users and the hosted-project setup are in [docs/runbooks/supabase.md](docs/runbooks/supabase.md).

## Continuous integration

| Workflow | Runs on | Does |
| --- | --- | --- |
| `ci` | push to `develop`/`main`, pull requests | Release build, warning count must not grow (`WARNING_BASELINE`), all tests |
| `supabase` | changes under `supabase/` | fresh local stack, migrations and seed, pgTAP tests, lint |
| `release` | tag `vX.Y.Z` | tests, publish, `vpk pack`, GitHub Release with the installer and the update feed |
| `keepalive` | Mondays 06:00 UTC, or by hand | one read from the hosted project so the free plan does not pause it |

### One-time repository setup (owner)

- **Variables** (Settings → Secrets and variables → Actions → Variables): `SUPABASE_URL` and
  `SUPABASE_PUBLISHABLE_KEY` of the hosted project. Both are public values; the `service_role` key never goes to
  GitHub.
- **Secret** (optional): `GOOGLE_CLIENT_SECRET_ENC_B64`, the Base64 of `SaveHarbor.App/Configuration/google-client-secret.enc`
  (`[Convert]::ToBase64String([IO.File]::ReadAllBytes("SaveHarbor.App\Configuration\google-client-secret.enc"))`).
  Without it, release builds say "not configured" for Google Drive.
- **Branch protection on `main`:** require the `ci` check, no force-push. Only the owner pushes tags.

## Releasing a new version (owner)

1. Merge `develop` into `main` (pull request).
2. On `main`, in one commit: set `<Version>` in `SaveHarbor.App/SaveHarbor.App.csproj` (for example `2.3.0`) and
   add the `## [2.3.0]` section to `CHANGELOG.md`. That section becomes the release notes in the app and on GitHub,
   so write it for friends: headings, bullets and **bold** only.
3. Tag and push: `git tag v2.3.0` then `git push origin v2.3.0`.
4. Watch the `release` workflow. When it is green, open the GitHub Release and check the assets:
   `SaveHarborApp-win-Setup.exe`, `SaveHarborApp-win-Portable.zip`, the `.nupkg` package(s) and `releases.win.json`.
5. Start an installed copy of the previous version: the update banner must appear within a minute.
6. If a Supabase migration belongs to this release: `supabase db push` **after** the release is live. Raise
   `min_supported_app_version` only for a breaking schema change, in the same migration.

Releases come only from `main`: the workflow refuses a tag on a commit that is not on `main`, a tag that does not
match the csproj version, and a tag without a CHANGELOG section. A tag with a suffix
(`v2.4.0-beta.1`) becomes a GitHub pre-release, which installed copies ignore.

To try an update on your own PC before tagging, see "Trying an update locally" in
[.claude/skills/build-and-verify/SKILL.md](.claude/skills/build-and-verify/SKILL.md).

## What a friend does

1. Download `SaveHarborApp-win-Setup.exe` from the latest GitHub Release and run it.
2. If Windows shows "Windows protected your PC", choose **More info → Run anyway** (the app is not code-signed).
3. SaveHarbor installs for the current Windows user (no admin rights), adds a Start-menu entry and a desktop
   shortcut, and starts.
4. Choose how to share:
   - **SaveHarbor Cloud:** enter your e-mail, type the code you receive, then create a group or join one with the
     invite code from a friend.
   - **Google Drive:** choose **Connect**, sign in with Google and pick the shared folder. While the Google Cloud
     project's OAuth consent screen is in *Testing* mode, the owner must add each friend's Google account under
     *OAuth consent screen → Test users* and give them edit access to the folder.

Updates arrive by themselves: a banner says when a new version is ready, and **Restart now** installs it. SaveHarbor
never restarts while the game is running. Uninstall from Windows Settings → Apps; saves and backups stay.

The installer has no folder choice. To install elsewhere, start it from a terminal:
`SaveHarborApp-win-Setup.exe --installto "D:\Games\SaveHarbor"`.

**Friends on the old zip (2.2.x):** install the new build once as above and delete the old folder. The zip cannot
update itself; everything under `%LOCALAPPDATA%\SaveHarbor` stays as it is.

**Portable copy:** every release also has `SaveHarborApp-win-Portable.zip`, which runs without installing. It does
not update itself.

## Where data is stored

| What | Location |
| --- | --- |
| The installed app | `%LOCALAPPDATA%\SaveHarborApp` (`current\SaveHarbor.App.exe`; replaced by updates) |
| App settings, device id | `%LOCALAPPDATA%\SaveHarbor\app-settings.json` |
| Cloud provider choice | `%LOCALAPPDATA%\SaveHarbor\cloud-provider-settings.json` |
| SaveHarbor Cloud sign-in | `%LOCALAPPDATA%\SaveHarbor\supabase-session.bin` (encrypted for the Windows user) |
| Google Drive sign-in | `%LOCALAPPDATA%\SaveHarbor\google-drive-token` |
| Backups | `%LOCALAPPDATA%\SaveHarbor\backups\<game>` |
| Logs | `%LOCALAPPDATA%\SaveHarbor\logs` |
| Dragonwilds saves (game) | `%LOCALAPPDATA%\RSDragonwilds\Saved\SaveGames` |

The backup, log and save folders can also be opened from **Settings** in the app. Personal data is never part of
a build or a release.

## Privacy

- **Update check:** an installed copy asks GitHub every few hours whether a newer release exists, and downloads it.
  GitHub sees the PC's IP address and that SaveHarbor asked; nothing about the player, worlds or saves is sent.
- **SaveHarbor Cloud:** stores your e-mail address (for sign-in only), a display name, a device label, timestamps
  and the shared world archives of your group. Other members see the display name, never the e-mail address. Row-level
  security limits every request to the groups you belong to.
- **Google Drive:** world archives and session locks go to the shared folder you chose, under your Google account.
- Supabase, GitHub and Google are third-party services. They are fine for a private group of friends; any wider or
  organisational use needs a GDPR and compliance review first (hosting region, processor agreements, retention).

## Notes

- The executable is not code-signed. A build distributed beyond a small group of
  friends should be signed and pass a security and compliance review first.
- The Dragonwilds save format is not documented by the game; SaveHarbor reads only
  the metadata it needs and never reads the world password or owner ID.
