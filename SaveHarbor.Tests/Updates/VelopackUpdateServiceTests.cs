using System.Net.Http;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Updates;
using SaveHarbor.App.Services;
using SaveHarbor.Tests.Support;
using Velopack.Locators;
using Velopack.Logging;

namespace SaveHarbor.Tests.Updates;

// The update service over Velopack with an in-memory feed: an installed 2.2.2 that finds, downloads, skips and refuses
// updates the way the banner expects.
public sealed class VelopackUpdateServiceTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly FakeUpdateSource source = new();
    private readonly AppSettingsStore settings;
    private readonly GameState game = new();
    private readonly List<VelopackUpdateService> created = [];

    public VelopackUpdateServiceTests()
    {
        settings = new AppSettingsStore(new TestPathProvider(temp));
    }

    public void Dispose()
    {
        created.ForEach(service => service.Dispose());
        temp.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private VelopackUpdateService Create(bool autoDownload = true)
    {
        var packages = Directory.CreateDirectory(temp.Combine("packages")).FullName;
        var service = new VelopackUpdateService(
            new UpdateOptions { AutoDownload = autoDownload },
            settings,
            game,
            game,
            new NullAppLogger(),
            () => source,
            new TestVelopackLocator("SaveHarborApp", "2.2.2", packages, new NullVelopackLogger()));
        created.Add(service);
        return service;
    }

    [Fact]
    public async Task NotInstalled_IsDisabledAndNeverAsksTheFeed()
    {
        var service = new VelopackUpdateService(new UpdateOptions(), settings, game, game, new NullAppLogger(), () => source);
        created.Add(service);

        await service.CheckAsync(userInitiated: true, Token);

        Assert.False(service.IsInstalled);
        Assert.Equal(AppUpdateState.Disabled, service.State);
        Assert.Equal(0, source.FeedRequests);
    }

    [Fact]
    public async Task NewerRelease_IsDownloadedAndReady()
    {
        source.AddFullRelease("2.3.0", "## Fixed\n- **Locks** expire");
        var service = Create();
        var states = new List<AppUpdateState>();
        service.StateChanged += (_, _) => states.Add(service.State);

        await service.CheckAsync(userInitiated: false, Token);

        Assert.Equal(new Version(2, 2, 2), service.CurrentVersion);
        Assert.Equal(AppUpdateState.Ready, service.State);
        Assert.Equal(new AppUpdateInfo(new Version(2, 3, 0), "## Fixed\n- **Locks** expire", 2048, false), service.Available);
        Assert.Equal(100, service.DownloadProgress);
        Assert.Equal(1, source.Downloads);
        Assert.Equal(
            [AppUpdateState.Checking, AppUpdateState.Available, AppUpdateState.Downloading, AppUpdateState.Ready],
            states.Distinct());
    }

    [Fact]
    public async Task WithoutAutoDownload_StopsAtAvailableUntilAsked()
    {
        source.AddFullRelease("2.3.0");
        var service = Create(autoDownload: false);
        var reported = new List<int>();

        await service.CheckAsync(userInitiated: false, Token);
        Assert.Equal(AppUpdateState.Available, service.State);
        Assert.Equal(0, source.Downloads);

        await service.DownloadAsync(new SynchronousProgress(reported.Add), Token);

        Assert.Equal(AppUpdateState.Ready, service.State);
        Assert.NotEmpty(reported);
    }

    [Fact]
    public async Task NoNewerRelease_IsUpToDate()
    {
        source.AddFullRelease("2.2.2");
        var service = Create();

        await service.CheckAsync(userInitiated: false, Token);

        Assert.Equal(AppUpdateState.Idle, service.State);
        Assert.Null(service.Available);
        Assert.NotNull(service.LastCheckedAtUtc);
    }

    [Fact]
    public async Task PreRelease_IsNotOffered()
    {
        source.AddFullRelease("2.4.0-beta.1");
        var service = Create();

        await service.CheckAsync(userInitiated: true, Token);

        Assert.Equal(AppUpdateState.Idle, service.State);
        Assert.Equal(0, source.Downloads);
    }

    [Fact]
    public async Task SkippedVersion_IsOfferedOnlyWhenThePlayerAsks()
    {
        settings.Update(current => current.SkippedUpdateVersion = "2.3.0");
        source.AddFullRelease("2.3.0");
        var service = Create();

        await service.CheckAsync(userInitiated: false, Token);
        Assert.Equal(AppUpdateState.Idle, service.State);

        await service.CheckAsync(userInitiated: true, Token);
        Assert.Equal(AppUpdateState.Ready, service.State);
    }

    [Fact]
    public async Task SkipVersion_HidesTheOfferAndIsRemembered()
    {
        source.AddFullRelease("2.3.0");
        var service = Create(autoDownload: false);
        await service.CheckAsync(userInitiated: false, Token);

        service.SkipVersion(new Version(2, 3, 0));

        Assert.Equal(AppUpdateState.Idle, service.State);
        Assert.Null(service.Available);
        Assert.Equal("2.3.0", new AppSettingsStore(new TestPathProvider(temp)).Current.SkippedUpdateVersion);
    }

    [Fact]
    public async Task OfflineCheck_FailsWithPlainWordsAndTheNextCheckWorks()
    {
        source.AddFullRelease("2.3.0");
        source.FeedFailure = new HttpRequestException("No such host is known.");
        var service = Create(autoDownload: false);

        await service.CheckAsync(userInitiated: false, Token);
        Assert.Equal(AppUpdateState.Failed, service.State);
        Assert.Equal("GitHub could not be reached. Check your internet connection.", service.LastError);

        await service.CheckAsync(userInitiated: false, Token);
        Assert.Equal(AppUpdateState.Available, service.State);
        Assert.Null(service.LastError);
    }

    [Fact]
    public async Task ApplyAndRestart_IsRefusedWhileTheGameRuns()
    {
        source.AddFullRelease("2.3.0");
        var service = Create();
        await service.CheckAsync(userInitiated: false, Token);
        game.IsRunning = true;

        Assert.False(service.ApplyAndRestart());
        Assert.False(service.IsApplyOnExitArmed);
    }

    [Fact]
    public async Task Apply_NeedsADownloadedUpdate()
    {
        source.AddFullRelease("2.3.0");
        var service = Create(autoDownload: false);
        await service.CheckAsync(userInitiated: false, Token);

        Assert.False(service.ApplyAndRestart());
        service.ApplyOnExit();
        Assert.False(service.IsApplyOnExitArmed);
    }

    // IProgress<T> without the thread-pool hop of Progress<T>, so the test sees every report before it asserts.
    private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private sealed class GameState : IProcessDetectionService, IActiveGameContext
    {
        public bool IsRunning { get; set; }

        public IGameDefinition Current { get; } = new StubGameDefinition(GameId.Windrose);

        public event EventHandler<IGameDefinition>? ActiveGameChanged
        {
            add { }
            remove { }
        }

        public bool IsGameRunning(IGameDefinition definition) => IsRunning;

        public void SetActive(GameId id)
        {
        }
    }
}
