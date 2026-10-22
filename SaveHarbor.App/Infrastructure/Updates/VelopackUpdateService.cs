using System.IO;
using System.Reflection;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace SaveHarbor.App.Infrastructure.Updates;

// Finds new releases on GitHub, downloads them in the background and installs them when the app restarts or closes,
// never while the active game runs. Disabled when the installer did not install this copy (dotnet run, an unzipped
// folder), because there is nothing to update in place.
public sealed class VelopackUpdateService : IAppUpdateService, IDisposable
{
    private readonly UpdateOptions options;
    private readonly IAppSettingsStore settings;
    private readonly IProcessDetectionService processes;
    private readonly IActiveGameContext activeGame;
    private readonly IAppLogger logger;
    private readonly TimeProvider timeProvider;
    private readonly UpdateManager? manager;
    private readonly ITimer? scheduledChecks;
    private readonly SemaphoreSlim gate = new(1, 1);
    private UpdateInfo? pending;

    // locator: tests pass a TestVelopackLocator; the app uses the one VelopackApp.Run() set up.
    public VelopackUpdateService(
        UpdateOptions options,
        IAppSettingsStore settings,
        IProcessDetectionService processes,
        IActiveGameContext activeGame,
        IAppLogger logger,
        Func<IUpdateSource> sourceFactory,
        IVelopackLocator? locator = null,
        TimeProvider? timeProvider = null)
    {
        this.options = options;
        this.settings = settings;
        this.processes = processes;
        this.activeGame = activeGame;
        this.logger = logger;
        this.timeProvider = timeProvider ?? TimeProvider.System;

        try
        {
            var candidate = new UpdateManager(sourceFactory(), null, locator);
            manager = candidate.IsInstalled ? candidate : null;
        }
        catch (Exception ex)
        {
            logger.Debug(AppLogKeyword.Updates, "Updates are off: {Message}", ex.Message);
        }

        CurrentVersion = manager?.CurrentVersion is { } installed
            ? new Version(installed.Major, installed.Minor, installed.Patch)
            : AssemblyVersion();
        State = manager is null ? AppUpdateState.Disabled : AppUpdateState.Idle;

        if (manager is not null)
        {
            var interval = TimeSpan.FromHours(options.CheckIntervalHours);
            scheduledChecks = this.timeProvider.CreateTimer(_ => _ = CheckInBackgroundAsync(), null, interval, interval);
        }
    }

    public bool IsInstalled => manager is not null;

    public Version CurrentVersion { get; }

    public AppUpdateState State { get; private set; }

    public AppUpdateInfo? Available { get; private set; }

    public int DownloadProgress { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? LastCheckedAtUtc { get; private set; }

    public bool IsApplyOnExitArmed { get; private set; }

    public event EventHandler? StateChanged;

    // GitHub releases normally; a folder of packed releases when started with --update-source.
    public static Func<IUpdateSource> CreateSourceFactory(UpdateOptions options) =>
        string.IsNullOrWhiteSpace(options.LocalSourceFolder)
            ? () => new GithubSource(options.GitHubRepository, null, false)
            : () => new SimpleFileSource(new DirectoryInfo(options.LocalSourceFolder));

    public async Task CheckAsync(bool userInitiated, CancellationToken cancellationToken = default)
    {
        if (manager is null || !await gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        var download = false;
        try
        {
            if (State is AppUpdateState.Ready or AppUpdateState.Downloading)
            {
                return;
            }

            SetState(AppUpdateState.Checking);
            var info = await manager.CheckForUpdatesAsync();
            LastCheckedAtUtc = timeProvider.GetUtcNow();
            LastError = null;

            var found = info is null ? null : Describe(info);
            if (found is null || (!userInitiated && found.Version.ToString() == settings.Current.SkippedUpdateVersion))
            {
                pending = null;
                Available = null;
                logger.Debug(AppLogKeyword.Updates, "No update to offer (current {Version})", CurrentVersion);
                SetState(AppUpdateState.Idle);
                return;
            }

            pending = info;
            Available = found;
            logger.Information(AppLogKeyword.Updates, "Update {Version} is available ({Bytes} bytes, delta {IsDelta})", found.Version, found.SizeBytes, found.IsDelta);
            SetState(AppUpdateState.Available);
            download = options.AutoDownload;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(loud: userInitiated, "check", ex);
        }
        finally
        {
            gate.Release();
        }

        if (download)
        {
            await DownloadAsync(null, cancellationToken);
        }
    }

    public async Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken = default)
    {
        if (manager is null || !await gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (pending is null || State is not (AppUpdateState.Available or AppUpdateState.Failed))
            {
                return;
            }

            DownloadProgress = 0;
            SetState(AppUpdateState.Downloading);
            await manager.DownloadUpdatesAsync(pending, percent =>
            {
                DownloadProgress = percent;
                progress?.Report(percent);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }, cancellationToken);

            DownloadProgress = 100;
            logger.Information(AppLogKeyword.Updates, "Update {Version} downloaded", Available?.Version);
            SetState(AppUpdateState.Ready);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(loud: true, "download", ex);
        }
        finally
        {
            gate.Release();
        }
    }

    public bool ApplyAndRestart()
    {
        if (manager is null || pending is null || State != AppUpdateState.Ready || processes.IsGameRunning(activeGame.Current))
        {
            return false;
        }

        // Update.exe waits for this process to end, so the app can close normally (logs flushed) before the swap.
        logger.Information(AppLogKeyword.Updates, "Installing update {Version} and restarting", Available?.Version);
        return StartUpdater(restart: true);
    }

    public void ApplyOnExit()
    {
        if (manager is null || pending is null || State != AppUpdateState.Ready || IsApplyOnExitArmed)
        {
            return;
        }

        logger.Information(AppLogKeyword.Updates, "Update {Version} installs when SaveHarbor closes", Available?.Version);
        if (StartUpdater(restart: false))
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SkipVersion(Version version)
    {
        settings.Update(current => current.SkippedUpdateVersion = version.ToString());
        logger.Information(AppLogKeyword.Updates, "Update {Version} skipped", version);
        if (Available?.Version == version && !IsApplyOnExitArmed)
        {
            pending = null;
            Available = null;
            SetState(AppUpdateState.Idle);
        }
    }

    public void Dispose()
    {
        scheduledChecks?.Dispose();
        gate.Dispose();
    }

    private bool StartUpdater(bool restart)
    {
        try
        {
            manager!.WaitExitThenApplyUpdates(pending!.TargetFullRelease, silent: true, restart: restart);
            IsApplyOnExitArmed = true;
            return true;
        }
        catch (Exception ex)
        {
            logger.Warning(AppLogKeyword.Updates, "The updater could not be started: {Message}", ex.Message);
            return false;
        }
    }

    private async Task CheckInBackgroundAsync()
    {
        try
        {
            await CheckAsync(userInitiated: false);
        }
        catch (Exception ex)
        {
            logger.Debug(AppLogKeyword.Updates, "Scheduled update check failed: {Message}", ex.Message);
        }
    }

    private static AppUpdateInfo? Describe(UpdateInfo info)
    {
        var target = info.TargetFullRelease;
        if (info.IsDowngrade || target.Version.IsPrerelease)
        {
            return null;
        }

        var deltas = info.BaseRelease is null ? [] : info.DeltasToTarget ?? [];
        return new AppUpdateInfo(
            new Version(target.Version.Major, target.Version.Minor, target.Version.Patch),
            target.NotesMarkdown ?? string.Empty,
            deltas.Length > 0 ? deltas.Sum(delta => delta.Size) : target.Size,
            deltas.Length > 0);
    }

    // Offline or rate limited: automatic checks stay quiet, the player only hears about checks they asked for.
    private void Fail(bool loud, string step, Exception ex)
    {
        LastError = ex is System.Net.Http.HttpRequestException or IOException
            ? "GitHub could not be reached. Check your internet connection."
            : ex.Message;
        if (loud)
        {
            logger.Warning(AppLogKeyword.Updates, "Update {Step} failed: {Message}", step, ex.Message);
        }
        else
        {
            logger.Debug(AppLogKeyword.Updates, "Update {Step} failed: {Message}", step, ex.Message);
        }

        SetState(AppUpdateState.Failed);
    }

    private void SetState(AppUpdateState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Version AssemblyVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }
}
