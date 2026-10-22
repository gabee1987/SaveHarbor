# 07 — Release Pipeline (GitHub Actions)

From "owner pushes a tag" to "friends get the update banner" with no manual packaging. Also CI for every push, Supabase migration checks, the keep-alive ping, and the Pages deploy for plan 08.

## 1. Workflows

| File | Trigger | Does |
|---|---|---|
| `.github/workflows/ci.yml` | push to `develop`/`main`, pull requests | restore, build Release, `dotnet test`, warning count must not grow (compare with a baseline number stored in the workflow env) |
| `.github/workflows/release.yml` | push of tag `v*.*.*` | build, publish, `vpk pack`, upload to a GitHub Release with the installer, portable zip, nupkgs and `releases.win.json`; publishes the release when the tag is not a pre-release |
| `.github/workflows/supabase.yml` | push/PR touching `supabase/**` | `supabase start` (Docker on `ubuntu-latest`), `supabase db reset` (applies migrations + seed), `supabase test db` (pgTAP), `supabase db lint` |
| `.github/workflows/keepalive.yml` | `schedule: "0 6 * * 1"` (weekly) + manual | `curl` `GET $SUPABASE_URL/rest/v1/app_meta?select=key` with `apikey` header; fails loudly (issue comment or job failure) when the status is not 200 |
| `.github/workflows/pages.yml` | push to `main` touching `web/**` | build the companion page and deploy to GitHub Pages (plan 08) |

Runners: `windows-latest` for build/release (WPF needs Windows), `ubuntu-latest` for the rest. Concurrency groups so two tags do not race.

## 2. Secrets and variables

| Name | Kind | Used by |
|---|---|---|
| `GITHUB_TOKEN` | built-in, `permissions: contents: write` on `release.yml` only | `vpk upload github` |
| `GOOGLE_CLIENT_SECRET_ENC_B64` | repository secret | restored to `SaveHarbor.App/Configuration/google-client-secret.enc` before publish (the file is git-ignored; without it the Drive provider says "not configured") |
| `SUPABASE_URL`, `SUPABASE_PUBLISHABLE_KEY` | repository **variables** (public values) | keep-alive, Pages build |
| `SUPABASE_ACCESS_TOKEN`, `SUPABASE_DB_PASSWORD` | **not stored**; migrations to the hosted project are pushed by the owner from their PC (`supabase db push`) | — |

The `service_role` key is never a secret in GitHub. Branch protection on `main`: require the `ci` check, no force-push, tags only by the owner.

## 3. `release.yml` outline

```yaml
name: release
on:
  push:
    tags: ['v*.*.*']
permissions:
  contents: write
concurrency: release-${{ github.ref }}
jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-dotnet@v4
        with: { global-json-file: global.json }
      - name: Derive version from tag
        shell: pwsh
        run: |
          $v = "${{ github.ref_name }}".TrimStart('v')
          "APP_VERSION=$v" >> $env:GITHUB_ENV
          $csproj = [xml](Get-Content SaveHarbor.App/SaveHarbor.App.csproj)
          if ($csproj.Project.PropertyGroup.Version -ne ($v -replace '-.*$','')) { throw "csproj Version must equal the tag" }
      - name: Restore Google client secret
        shell: pwsh
        run: |
          if ("${{ secrets.GOOGLE_CLIENT_SECRET_ENC_B64 }}") {
            [IO.File]::WriteAllBytes("SaveHarbor.App/Configuration/google-client-secret.enc", [Convert]::FromBase64String("${{ secrets.GOOGLE_CLIENT_SECRET_ENC_B64 }}"))
          }
      - run: dotnet test SaveHarbor.sln -c Release
      - run: dotnet publish SaveHarbor.App -c Release -r win-x64 --self-contained true -p:Version=${{ env.APP_VERSION }} -p:DebugType=none -o artifacts/publish
      - run: dotnet tool install -g vpk
      - name: Extract release notes
        shell: pwsh
        run: ./build/Get-ReleaseNotes.ps1 -Version ${{ env.APP_VERSION }} -Out artifacts/notes.md   # section from CHANGELOG.md
      - run: vpk download github --repoUrl https://github.com/${{ github.repository }} --outputDir artifacts/releases   # previous release, for delta packages; tolerate "no releases"
        continue-on-error: true
      - run: vpk pack --packId SaveHarborApp --packVersion ${{ env.APP_VERSION }} --packDir artifacts/publish --mainExe SaveHarbor.App.exe --packTitle SaveHarbor --packAuthors "SaveHarbor" --icon SaveHarbor.App/Resources/saveharbor.ico --releaseNotes artifacts/notes.md --outputDir artifacts/releases
      - run: vpk upload github --repoUrl https://github.com/${{ github.repository }} --token ${{ secrets.GITHUB_TOKEN }} --outputDir artifacts/releases --releaseName "SaveHarbor ${{ env.APP_VERSION }}" --tag ${{ github.ref_name }} --publish ${{ !contains(github.ref_name, '-') }} --pre ${{ contains(github.ref_name, '-') }}
```

Flags are from the `vpk` help as of 1.2.x; **[verify]** names with `vpk pack --help` / `vpk upload github --help` on first run and fix in the task. Pre-release tags (`v2.4.0-beta.1`) upload as GitHub pre-releases, which the app ignores.

## 4. CHANGELOG and notes

`CHANGELOG.md` at the repo root in Keep-a-Changelog form. The owner writes the `## [2.3.0] - 2026-11-02` section before tagging; `build/Get-ReleaseNotes.ps1` extracts the section for the tag and fails if it is missing (so a release never ships with empty notes). The first section (`2.3.0`) must contain the "install the new build once" instruction from plan 06 §7.

## 5. Owner's release checklist (goes into README)

1. Merge `develop` → `main` (PR by the owner).
2. Bump `<Version>` in the csproj and add the CHANGELOG section (one commit on `main`).
3. `git tag v2.3.0 && git push origin v2.3.0`.
4. Watch the `release` workflow; when green, open the GitHub Release and check the four assets.
5. Start an installed copy of the previous version: the banner must appear within a minute.
6. If a Supabase migration belongs to this release: `supabase db push` **after** the release is live, then (only for breaking changes) raise `min_supported_app_version` in the same migration.

## 6. E-mail delivery for sign-in codes (optional, when the built-in sender is too slow)

Supabase's default sender is rate-limited. If friends report codes arriving late, configure custom SMTP in Auth → SMTP with a free-tier transactional provider (Resend: 3,000 e-mails/month free; Brevo: 300/day). Both are third-party services and would need a compliance review outside this hobby use. Sender address on a domain the owner controls; SPF/DKIM records set. Codes still expire after 10 minutes (Supabase default `otp_expiry`, configurable down to 5 minutes).

## 7. Runbook (`docs/runbooks/supabase.md`, short)

- Resume a paused project; check keep-alive history.
- Rotate the publishable key (requires an app release because it ships in `appsettings.json`; old versions keep working until the old key is disabled).
- Remove a member (dashboard: delete `group_members` row; or the owner uses "Remove" in Settings once T12.5 adds it).
- Delete a user on request: reassign or delete their `world_versions` rows, then delete the auth user.
- Export the `saves` bucket (Supabase CLI `storage` commands or the dashboard) before risky migrations.
- Raise `min_supported_app_version`: only with a breaking schema migration, after the fixed app is released.
