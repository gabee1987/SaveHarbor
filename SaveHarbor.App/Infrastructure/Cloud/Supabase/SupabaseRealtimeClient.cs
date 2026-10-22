using System.Net.WebSockets;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// One WebSocket to SaveHarbor Cloud while a group is active: friends' changes and who is online arrive as they happen.
// A dropped connection is retried after 1, 2, 4 … 60 s; meanwhile the state is Polling and the window checks the cloud
// itself. Tokens and row contents are never logged.
public sealed partial class SupabaseRealtimeClient : ICloudLiveUpdates, IDisposable
{
    // Realtime closes a socket that has not sent a heartbeat for 30 s.
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(25);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private readonly SupabaseOptions options;
    private readonly Func<CancellationToken, Task<string>> accessToken;
    private readonly Func<string?> userId;
    private readonly IAppLogger logger;
    private readonly Func<Uri, CancellationToken, Task<WebSocket>> connect;
    private readonly Lock gate = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);

    private CancellationTokenSource? runCancellation;
    private Task runTask = Task.CompletedTask;
    private string? groupId;
    private CloudPresence? presence;
    private CloudLiveState state;
    private IReadOnlyList<CloudPresenceEntry> online = [];

    // The joined socket and its join reference; null between connections.
    private WebSocket? joinedSocket;
    private string joinRef = string.Empty;
    private int nextRef;

    public SupabaseRealtimeClient(SupabaseOptions options, SupabaseAuthClient auth, IAppLogger logger)
        : this(options, auth.GetAccessTokenAsync, () => auth.UserId, logger, ConnectAsync, DefaultHeartbeatInterval)
    {
    }

    public SupabaseRealtimeClient(
        SupabaseOptions options,
        Func<CancellationToken, Task<string>> accessToken,
        Func<string?> userId,
        IAppLogger logger,
        Func<Uri, CancellationToken, Task<WebSocket>> connect,
        TimeSpan heartbeatInterval,
        Func<int, TimeSpan>? backoff = null)
    {
        this.options = options;
        this.accessToken = accessToken;
        this.userId = userId;
        this.logger = logger;
        this.connect = connect;
        HeartbeatInterval = heartbeatInterval;
        Backoff = backoff ?? DefaultBackoff;
    }

    public event EventHandler<CloudLiveChange>? ChangeReceived;

    public event EventHandler? PresenceChanged;

    public event EventHandler? StateChanged;

    public TimeSpan HeartbeatInterval { get; }

    public Func<int, TimeSpan> Backoff { get; }

    public CloudLiveState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public IReadOnlyList<CloudPresenceEntry> Online
    {
        get
        {
            lock (gate)
            {
                return online;
            }
        }
    }

    // 1, 2, 4 … 60 s.
    public static TimeSpan DefaultBackoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(attempt, 6))));

    public async Task StartAsync(string group, CloudPresence? newPresence)
    {
        bool sameGroup;
        lock (gate)
        {
            sameGroup = runCancellation is not null && groupId == group;
            if (sameGroup && presence == newPresence)
            {
                return;
            }

            presence = newPresence;
        }

        if (sameGroup)
        {
            await TrackAsync(untrackWhenHidden: true, CancellationToken.None);
            return;
        }

        await StopAsync();
        if (!options.IsConfigured || userId() is not { Length: > 0 } user)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        lock (gate)
        {
            groupId = group;
            runCancellation = cancellation;
        }

        SetState(CloudLiveState.Connecting);
        runTask = Task.Run(() => RunAsync(group, user, cancellation.Token));
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        lock (gate)
        {
            cancellation = runCancellation;
            runCancellation = null;
            groupId = null;
        }

        if (cancellation is not null)
        {
            await cancellation.CancelAsync();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
            }

            cancellation.Dispose();
        }

        SetOnline([]);
        SetState(CloudLiveState.Off);
    }

    // Leaving the app: the server notices the closed socket and tells the group this member left.
    public void Dispose()
    {
        lock (gate)
        {
            runCancellation?.Cancel();
        }
    }

    private async Task RunAsync(string group, string user, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(group, user, () => attempt = 0, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The type only: messages of socket errors can contain the address with the project key.
                logger.Warning(AppLogKeyword.CloudSync, "SaveHarbor Cloud live updates disconnected ({Error}); checking every minute until they are back", exception.GetType().Name);
            }

            SetOnline([]);
            SetState(CloudLiveState.Polling);
            try
            {
                await Task.Delay(Backoff(attempt++), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void SetState(CloudLiveState newState)
    {
        lock (gate)
        {
            if (state == newState)
            {
                return;
            }

            state = newState;
        }

        logger.Debug(AppLogKeyword.CloudSync, "SaveHarbor Cloud live updates: {State}", newState);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetOnline(IReadOnlyList<CloudPresenceEntry> entries)
    {
        lock (gate)
        {
            if (online.Count == 0 && entries.Count == 0)
            {
                return;
            }

            online = entries;
        }

        PresenceChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(uri, timeout.Token);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private string NextRef() => Interlocked.Increment(ref nextRef).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void OnChange(RealtimeMessage message)
    {
        if (SupabaseRealtimeProtocol.Change(message) is { } change)
        {
            ChangeReceived?.Invoke(this, change);
        }
    }

    private void OnPresence(Dictionary<string, List<JsonElement>> metasByKey, RealtimeMessage message)
    {
        SupabaseRealtimeProtocol.ApplyPresence(metasByKey, message);
        SetOnline(SupabaseRealtimeProtocol.Entries(metasByKey));
    }
}
