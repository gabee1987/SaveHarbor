using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.App.Services;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// The live connection against a scripted server on a loopback socket: join, presence, changes, heartbeats, token
// refresh, reconnect after a drop and a refused join, and stopping.
public sealed class SupabaseRealtimeClientTests : IAsyncDisposable
{
    private const string GroupId = "00000000-0000-4000-8000-000000012345";
    private const string UserId = "00000000-0000-4000-8000-0000000000aa";
    private static readonly CloudPresence Presence = new("TEST_USER", "windrose", null);

    private readonly RealtimeTestServer server = new(TestContext.Current.CancellationToken);
    private readonly RecordingLogger logger = new();
    // Written by the test, read by the client's heartbeat on another thread.
    private volatile string token = "TEST_TOKEN";

    public async ValueTask DisposeAsync() => await server.DisposeAsync();

    [Fact]
    public async Task Start_JoinsTheGroupChannelGoesLiveAndTracksPresence()
    {
        await using var client = new ClientScope(CreateClient(TimeSpan.FromSeconds(30)));

        await client.Value.StartAsync(GroupId, Presence);
        var socket = await server.NextAsync();
        var join = await ReceiveAsync(socket);

        Assert.Equal("phx_join", join.Event);
        Assert.Equal($"realtime:saveharbor:{GroupId}", join.Topic);
        Assert.Equal("TEST_TOKEN", join.Payload.GetProperty("access_token").GetString());
        Assert.Equal(CloudLiveState.Connecting, client.Value.State);

        await ReplyOkAsync(socket, join);
        var track = await ReceiveAsync(socket);

        Assert.Equal("presence", track.Event);
        Assert.Equal("TEST_USER", track.Payload.GetProperty("payload").GetProperty("display_name").GetString());
        await WaitUntilAsync(() => client.Value.State == CloudLiveState.Live);
    }

    [Fact]
    public async Task ChangesAndPresence_AreRaisedAsTheyArrive()
    {
        await using var client = new ClientScope(CreateClient(TimeSpan.FromSeconds(30)));
        var changes = new List<CloudLiveChange>();
        client.Value.ChangeReceived += (_, change) => { lock (changes) { changes.Add(change); } };
        var socket = await JoinAsync(client.Value);

        await SendAsync(socket, $"{{\"topic\":\"realtime:saveharbor:{GroupId}\",\"event\":\"postgres_changes\",\"payload\":{{\"data\":{{\"table\":\"worlds\",\"type\":\"UPDATE\"}}}},\"ref\":null}}");
        await SendAsync(socket, $"{{\"topic\":\"realtime:saveharbor:{GroupId}\",\"event\":\"presence_state\",\"payload\":{{\"{UserId}\":{{\"metas\":[{{\"phx_ref\":\"A\",\"display_name\":\"TEST_USER\",\"game\":\"windrose\"}}]}}}},\"ref\":null}}");
        // Another topic on the same socket is none of this channel's business.
        await SendAsync(socket, "{\"topic\":\"realtime:other\",\"event\":\"postgres_changes\",\"payload\":{\"data\":{\"table\":\"worlds\",\"type\":\"INSERT\"}},\"ref\":null}");

        await WaitUntilAsync(() => client.Value.Online.Count == 1);
        lock (changes)
        {
            Assert.Equal([new CloudLiveChange("worlds", "UPDATE")], changes);
        }

        Assert.Equal(UserId, client.Value.Online[0].UserId);
    }

    [Fact]
    public async Task Heartbeats_AreSentAndARefreshedTokenIsPassedOn()
    {
        // Every reply must arrive before the next tick, or the client rightly drops the connection; a second leaves
        // room for a busy CI runner.
        await using var client = new ClientScope(CreateClient(TimeSpan.FromSeconds(1)));
        var socket = await JoinAsync(client.Value);

        var heartbeat = await ReceiveUntilAsync(socket, message => message.Event == "heartbeat");
        Assert.Equal("phoenix", heartbeat.Topic);
        await SendAsync(socket, $"{{\"topic\":\"phoenix\",\"event\":\"phx_reply\",\"payload\":{{\"status\":\"ok\",\"response\":{{}}}},\"ref\":\"{heartbeat.Ref}\"}}");

        token = "TEST_TOKEN_2";
        var refresh = await ReceiveUntilAsync(socket, message => message.Event == "access_token", answerHeartbeats: true);

        Assert.Equal("TEST_TOKEN_2", refresh.Payload.GetProperty("access_token").GetString());
        Assert.Equal(CloudLiveState.Live, client.Value.State);
        Assert.DoesNotContain(logger.Lines, line => line.Contains("TEST_TOKEN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AHeartbeatWithoutReply_EndsTheConnectionAndReconnects()
    {
        await using var client = new ClientScope(CreateClient(TimeSpan.FromMilliseconds(150)));
        await JoinAsync(client.Value);

        var second = await server.NextAsync();

        Assert.Equal("phx_join", (await ReceiveAsync(second)).Event);
    }

    [Fact]
    public async Task ADroppedConnection_ShowsPollingThenReconnects()
    {
        await using var client = new ClientScope(CreateClient(TimeSpan.FromSeconds(30)));
        var states = new List<CloudLiveState>();
        client.Value.StateChanged += (_, _) => { lock (states) { states.Add(client.Value.State); } };
        var socket = await JoinAsync(client.Value);

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, Token);
        var second = await server.NextAsync();
        await ReplyOkAsync(second, await ReceiveAsync(second));

        await WaitUntilAsync(() => client.Value.State == CloudLiveState.Live);
        lock (states)
        {
            Assert.Contains(CloudLiveState.Polling, states);
        }
    }

    [Fact]
    public async Task ARefusedJoin_KeepsPollingAndTriesAgain()
    {
        await using var client = new ClientScope(CreateClient(TimeSpan.FromSeconds(30)));
        await client.Value.StartAsync(GroupId, Presence);
        var socket = await server.NextAsync();
        var join = await ReceiveAsync(socket);

        await SendAsync(socket, $"{{\"topic\":\"{join.Topic}\",\"event\":\"phx_reply\",\"payload\":{{\"status\":\"error\",\"response\":{{\"reason\":\"Unauthorized: not a member\"}}}},\"ref\":\"{join.Ref}\"}}");

        await WaitUntilAsync(() => client.Value.State == CloudLiveState.Polling);
        var second = await server.NextAsync();
        Assert.Equal("phx_join", (await ReceiveAsync(second)).Event);
    }

    [Fact]
    public async Task Stop_DisconnectsAndDoesNotReconnect()
    {
        var client = CreateClient(TimeSpan.FromSeconds(30));
        await JoinAsync(client);

        await client.StopAsync();

        Assert.Equal(CloudLiveState.Off, client.State);
        Assert.Empty(client.Online);
        await Task.Delay(200, Token);
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    public async Task Start_WithoutASignedInUserDoesNotConnect()
    {
        var client = new SupabaseRealtimeClient(Options(), _ => Task.FromResult(token), () => null, logger, server.ConnectAsync, TimeSpan.FromSeconds(30));

        await client.StartAsync(GroupId, Presence);

        Assert.Equal(CloudLiveState.Off, client.State);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    public async Task Start_ForTheSameGroupOnlyUpdatesThePresence()
    {
        await using var client = new ClientScope(CreateClient(TimeSpan.FromSeconds(30)));
        var socket = await JoinAsync(client.Value);

        await client.Value.StartAsync(GroupId, Presence with { PlayingWorldId = "TEST_WORLD_ID" });
        var track = await ReceiveAsync(socket);

        Assert.Equal("TEST_WORLD_ID", track.Payload.GetProperty("payload").GetProperty("playing_world_id").GetString());
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    public async Task HidingTheOnlineStatus_UntracksAndAHiddenJoinTracksNothing()
    {
        await using var client = new ClientScope(CreateClient(TimeSpan.FromSeconds(30)));
        var socket = await JoinAsync(client.Value);

        await client.Value.StartAsync(GroupId, null);
        var untrack = await ReceiveAsync(socket);

        Assert.Equal("untrack", untrack.Payload.GetProperty("event").GetString());

        // After a reconnect the hidden player joins without sending any presence: the next frame is the track below.
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, Token);
        var second = await server.NextAsync();
        await ReplyOkAsync(second, await ReceiveAsync(second));
        await WaitUntilAsync(() => client.Value.State == CloudLiveState.Live);

        await client.Value.StartAsync(GroupId, Presence);
        Assert.Equal("track", (await ReceiveAsync(second)).Payload.GetProperty("event").GetString());
    }

    [Fact]
    public void DefaultBackoff_DoublesUpToAMinute()
    {
        Assert.Equal(
            [1, 2, 4, 8, 16, 32, 60, 60],
            Enumerable.Range(0, 8).Select(attempt => SupabaseRealtimeClient.DefaultBackoff(attempt).TotalSeconds));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SupabaseOptions Options() => new() { Url = "http://127.0.0.1:54321/", PublishableKey = "TEST_KEY" };

    private SupabaseRealtimeClient CreateClient(TimeSpan heartbeat) =>
        new(Options(), _ => Task.FromResult(token), () => UserId, logger, server.ConnectAsync, heartbeat, _ => TimeSpan.FromMilliseconds(20));

    // Starts the client and answers its join; returns the server side once the presence was tracked.
    private async Task<WebSocket> JoinAsync(SupabaseRealtimeClient client)
    {
        await client.StartAsync(GroupId, Presence);
        var socket = await server.NextAsync();
        await ReplyOkAsync(socket, await ReceiveAsync(socket));
        Assert.Equal("presence", (await ReceiveAsync(socket)).Event);
        await WaitUntilAsync(() => client.State == CloudLiveState.Live);
        return socket;
    }

    private static Task ReplyOkAsync(WebSocket socket, RealtimeMessage join) =>
        SendAsync(socket, $"{{\"topic\":\"{join.Topic}\",\"event\":\"phx_reply\",\"payload\":{{\"status\":\"ok\",\"response\":{{\"postgres_changes\":[]}}}},\"ref\":\"{join.Ref}\",\"join_ref\":\"{join.Ref}\"}}");

    private static Task SendAsync(WebSocket socket, string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, Token);

    private static async Task<RealtimeMessage> ReceiveAsync(WebSocket socket)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var buffer = new byte[16384];
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.True(result.EndOfMessage);
        return SupabaseRealtimeProtocol.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))
            ?? throw new InvalidDataException("The client sent a frame that is not a message.");
    }

    private static async Task<RealtimeMessage> ReceiveUntilAsync(WebSocket socket, Func<RealtimeMessage, bool> match, bool answerHeartbeats = false)
    {
        while (true)
        {
            var message = await ReceiveAsync(socket);
            if (match(message))
            {
                return message;
            }

            if (answerHeartbeats && message.Event == "heartbeat")
            {
                await SendAsync(socket, $"{{\"topic\":\"phoenix\",\"event\":\"phx_reply\",\"payload\":{{\"status\":\"ok\",\"response\":{{}}}},\"ref\":\"{message.Ref}\"}}");
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met within 5 seconds.");
            await Task.Delay(10, Token);
        }
    }

    // Stops the client at the end of a test, so no reconnect outlives it.
    private sealed class ClientScope(SupabaseRealtimeClient value) : IAsyncDisposable
    {
        public SupabaseRealtimeClient Value { get; } = value;

        public async ValueTask DisposeAsync() => await Value.StopAsync();
    }
}
