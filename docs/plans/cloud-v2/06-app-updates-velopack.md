# 06 — App Updates with Velopack

Detect a new release, fetch it in the background, apply it when it is safe, and refuse to talk to a cloud the app is too old for. Independent of the Supabase work except for §5.

## 1. Choice and facts

- **Velopack** NuGet `1.2.161` (2026-09-29), MIT. Installer + updater + delta packages. Successor of Squirrel.Windows. Update source: `GithubSource` on the public repository (`https://github.com/gabee1987/SaveHarbor`, taken from `git remote`), no token, 60 requests/hour/IP unauthenticated limit (we make at most 5 per day per PC).
- Velopack installs per user to `%LOCALAPPDATA%\<packId>\current\`. **`packId = SaveHarborApp`** so it never touches the data root `%LOCALAPPDATA%\SaveHarbor\`.
- Velopack packs a **folder**, not a single-file bundle: publish without `PublishSingleFile` (delta packages work per file; a single 60 MB exe would make every update a full download). **[verify]** against the Velopack docs that `PublishSingleFile` is indeed discouraged; keep self-contained + `ReadyToRun` off (size).
- The `vpk` CLI (`dotnet tool install -g vpk`) produces: `SaveHarborApp-win-Setup.exe`, `SaveHarborApp-win-Portable.zip`, `SaveHarborApp-<version>-full.nupkg`, `-delta.nupkg`, `releases.win.json`. GitHub Releases host them (plan 07).

Rejected: ClickOnce (HTTPS hosting + clunky UX), MSIX (needs a certificate), winget (public manifests, review latency), a home-made "download zip and swap exe" (file-in-use and half-download handling is what Velopack already solves).

## 2. Integration points

**`SaveHarbor.App.csproj`**
```xml
<PackageReference Include="Velopack" Version="1.2.161" />
<StartupObject>SaveHarbor.App.Program</StartupObject>
<ApplicationIcon>Resources\saveharbor.ico</ApplicationIcon>   <!-- [verify] an .ico exists; if not, the owner adds one (task T13.1) -->
```

**`Program.cs`** (new, ≤ 60 lines)
```csharp
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnAfterInstallFastCallback(v => ProtocolRegistration.Register())    // plan 05 §7, optional
            .OnBeforeUninstallFastCallback(v => ProtocolRegistration.Unregister())
            .Run();
        using var singleInstance = SingleInstanceGuard.TryAcquire(args);         // plan 05 §5
        if (singleInstance is null) return;
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
```
`App.xaml` keeps no `StartupUri` (the window is shown from `OnStartup` today); nothing else in `App.xaml.cs` changes.

**Services/IAppUpdateService.cs**
```csharp
public interface IAppUpdateService
{
    bool IsInstalled { get; }                       // false under `dotnet run` → feature disabled
    Version CurrentVersion { get; }
    AppUpdateState State { get; }                   // Idle, Checking, Available, Downloading, Ready, Failed, Disabled
    AppUpdateInfo? Available { get; }               // record(Version, string NotesMarkdown, long SizeBytes, bool IsDelta)
    event EventHandler? StateChanged;
    Task CheckAsync(bool userInitiated, CancellationToken ct = default);
    Task DownloadAsync(IProgress<int>? progress, CancellationToken ct = default);
    void ApplyAndRestart();                         // only when State == Ready and the game is not running
    void ApplyOnExit();                             // WaitExitThenApplyUpdates
    void SkipVersion(Version version);
}
```
Implementation `Infrastructure/Updates/VelopackUpdateService.cs` wraps `UpdateManager` (`new UpdateManager(new GithubSource(repoUrl, null, prerelease: false))`). An `IUpdateSource` factory lets tests and the `--update-source <folder>` developer flag inject `SimpleFileSource`. Options in `appsettings.json`: `"Updates": { "GitHubRepository": "https://github.com/gabee1987/SaveHarbor", "CheckIntervalHours": 6, "AutoDownload": true }`.

## 3. Behaviour

| When | What |
|---|---|
| Startup, 10 s after the window is shown | `CheckAsync(userInitiated: false)` in the background. Never blocks UI. |
| Every `CheckIntervalHours` while running | same |
| Settings → "Check for updates" | `CheckAsync(true)`: toast "You have the newest version (2.3.1)" or the banner |
| Update found | `AutoDownload` → download immediately (deltas, usually < 5 MB); banner at the top of the shell: "SaveHarbor 2.4.0 is ready · What's new · Restart now · Later". While downloading: "Downloading update… 43 %" |
| "Restart now" | if the game is running or a session is active: "Finish playing first; the update installs when you close SaveHarbor." and switch to `ApplyOnExit()`; else `ApplyAndRestart()` |
| "Later" | banner collapses to a small dot on the Settings tab; `ApplyOnExit()` is armed so the update installs when the app closes |
| "Skip this version" (in What's new) | `AppSettings.SkippedUpdateVersion`; banner hidden for that version only |
| Check fails (offline, rate-limited) | silent at `Debug`; user-initiated checks show "Could not check for updates: <reason>" |
| Running from `dotnet run` or an unzipped non-Velopack folder (`IsInstalled == false`) | State `Disabled`; Settings shows "Updates are available in the installed version." |

"What's new" opens `AppDialogWindow` with the release notes rendered as plain paragraphs and bullets (a 60-line Markdown-to-`FlowDocument` helper for headings, bullets, bold; no package). Notes come from `UpdateInfo.TargetFullRelease.NotesMarkdown`, which `vpk pack --releaseNotes` embeds (plan 07 §3).

## 4. Version as a single source of truth

- The git tag `vX.Y.Z` is the version. CI passes `-p:Version=X.Y.Z` to `dotnet publish` and `--packVersion X.Y.Z` to `vpk pack`. The csproj `<Version>` stays as a fallback for local builds and is bumped by the owner when tagging (task T13.6 adds a check that the tag equals the csproj version, so they cannot drift).
- `CurrentVersion` = `Assembly.GetEntryAssembly()!.GetName().Version` (3 parts). Shown in the footer and in Settings (existing footer text gets "v2.3.1 · up to date" / "v2.3.1 · 2.4.0 ready").
- Pre-releases (`v2.4.0-beta.1`) are published as GitHub pre-releases and ignored by the app (`prerelease: false`); the owner tests them with `--update-source`.

## 5. Minimum supported version gate (needs plan 01)

- `app_meta.min_supported_app_version` is readable before sign-in. `SupabaseCloudProvider.GetConnectionStatusAsync` reads it (cached 1 h) and, when `CurrentVersion < min`, returns not connected with `CloudConnectionStatus.Reason = UpdateRequired`. `CloudSyncState.UpdateRequired` is added; `CloudSyncService.RefreshStatusAsync` maps it first (before `NotConnected`). All upload/download/session commands are blocked in that state; the Sharing view shows the row from plan 04 §4 with an **Update** button that triggers `CheckAsync(true)`.
- For Google Drive the same gate applies through the manifest: a `CloudWorldManifest.SchemaVersion` or `CloudSessionLock.SchemaVersion` higher than the app knows → `UpdateRequired` for that world (today unknown fields are silently ignored).
- When the owner raises `min_supported_app_version` (after a schema change), old apps stop syncing gracefully instead of corrupting shared state. Rule: raise it only for breaking schema changes, in the same migration file, and release the app **before** pushing the migration.

## 6. Security

- Velopack verifies package SHA-1/SHA-256 from `releases.win.json` and GitHub serves it over HTTPS; there is no code signing yet, so a compromised GitHub account could ship a bad build. Mitigation now: 2FA on the GitHub account, branch protection on `main`, releases only from tags created by the owner. Hook for later: `vpk pack --signParams` with Azure Trusted Signing.
- The update check sends no personal data: GitHub sees the IP and a user agent. Document in README.
- The updater runs under the user's account (per-user install); no elevation prompts, no services.

## 7. Migration for friends on the old zip

The 2.2.x zip has no updater. One-time manual step, documented in the release notes of the first Velopack release (`v2.3.0`): download `SaveHarborApp-win-Setup.exe`, run it (SmartScreen "More info → Run anyway" as before), delete the old folder. Data under `%LOCALAPPDATA%\SaveHarbor` is untouched. The installer creates a Start-menu entry and a desktop shortcut; `--game` shortcuts keep working with the new exe path (`%LOCALAPPDATA%\SaveHarborApp\current\SaveHarbor.App.exe`).
