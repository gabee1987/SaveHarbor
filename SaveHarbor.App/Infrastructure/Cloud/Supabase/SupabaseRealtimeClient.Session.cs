using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// One connection from opening the socket to losing it: join, heartbeats, token refreshes and the incoming messages.
// Any problem ends the connection with an exception, and RunAsync starts the next one after the backoff.
public sealed partial class SupabaseRealtimeClient
{
    // Far above any message of a friend group; a larger one ends the connection instead of growing the buffer.
    private const int MaxMessageBytes = 1024 * 1024;

    private const int ReceiveBufferBytes = 8192;

    private async Task RunSessionAsync(string group, string user, Action joined, CancellationToken cancellationToken)
    {
        using var socket = await connect(SupabaseRealtimeProtocol.SocketUri(options), cancellationToken);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = await accessToken(cancellationToken);
        var join = NextRef();
        await SendAsync(socket, SupabaseRealtimeProtocol.Join(group, user, token, join), cancellationToken);

        var heartbeat = new HeartbeatState(token);
        var heartbeatTask = HeartbeatAsync(socket, group, join, heartbeat, session.Token);
        try
        {
            await ReceiveAsync(socket, group, join, heartbeat, joined, session.Token);
        }
        finally
        {
            lock (gate)
            {
                if (joinedSocket == socket)
                {
                    joinedSocket = null;
                }
            }

            await session.CancelAsync();
            try
            {
                await heartbeatTask;
            }
            catch (Exception)
            {
                // The heartbeat only ever ends this connection; why it ended is reported by the receive loop.
            }

            await CloseQuietlyAsync(socket);
        }
    }

    // Every interval: give up on a join or heartbeat that got no reply, send the next heartbeat, and pass on a token the
    // sign-in refreshed meanwhile, so the server does not drop the channel when the old one expires.
    private async Task HeartbeatAsync(WebSocket socket, string group, string join, HeartbeatState heartbeat, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!heartbeat.Joined || heartbeat.PendingRef is not null)
                {
                    logger.Debug(AppLogKeyword.CloudSync, "SaveHarbor Cloud live updates stopped answering; reconnecting");
                    socket.Abort();
                    return;
                }

                var reference = NextRef();
                heartbeat.PendingRef = reference;
                await SendAsync(socket, SupabaseRealtimeProtocol.Heartbeat(reference), cancellationToken);

                var fresh = await accessToken(cancellationToken);
                if (fresh != heartbeat.Token)
                {
                    heartbeat.Token = fresh;
                    await SendAsync(socket, SupabaseRealtimeProtocol.AccessToken(group, fresh, NextRef(), join), cancellationToken);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            socket.Abort();
            throw;
        }
    }

    private async Task ReceiveAsync(WebSocket socket, string group, string join, HeartbeatState heartbeat, Action joined, CancellationToken cancellationToken)
    {
        var topic = SupabaseRealtimeProtocol.Topic(group);
        var metasByKey = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        while (true)
        {
            var text = await ReceiveTextAsync(socket, cancellationToken)
                ?? throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
            if (SupabaseRealtimeProtocol.Parse(text) is not { } message)
            {
                continue;
            }

            if (message.Topic == "phoenix")
            {
                if (message.Event == "phx_reply" && message.Ref is { } reference && reference == heartbeat.PendingRef)
                {
                    heartbeat.PendingRef = null;
                }

                continue;
            }

            if (message.Topic != topic)
            {
                continue;
            }

            switch (message.Event)
            {
                case "phx_reply" when message.Ref == join:
                    if (SupabaseRealtimeProtocol.ReplyStatus(message) != "ok")
                    {
                        throw new RealtimeJoinRefusedException();
                    }

                    heartbeat.Joined = true;
                    lock (gate)
                    {
                        joinedSocket = socket;
                        joinRef = join;
                    }

                    joined();
                    SetState(Services.CloudLiveState.Live);
                    await TrackAsync(untrackWhenHidden: false, cancellationToken);
                    break;
                case "postgres_changes":
                    OnChange(message);
                    break;
                case "presence_state" or "presence_diff":
                    OnPresence(metasByKey, message);
                    break;
                case "phx_error" or "phx_close":
                    throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
            }
        }
    }

    // Sends this app's presence on the joined channel; nothing to do between connections, the next join sends it. A
    // hidden presence is untracked only when it may have been shown on this channel; a fresh join never showed it.
    private async Task TrackAsync(bool untrackWhenHidden, CancellationToken cancellationToken)
    {
        WebSocket? socket;
        string join;
        string? group;
        Services.CloudPresence? current;
        lock (gate)
        {
            socket = joinedSocket;
            join = joinRef;
            group = groupId;
            current = presence;
        }

        if (socket is null || group is null || (current is null && !untrackWhenHidden))
        {
            return;
        }

        try
        {
            var frame = current is null
                ? SupabaseRealtimeProtocol.Untrack(group, NextRef(), join)
                : SupabaseRealtimeProtocol.Track(group, current, NextRef(), join);
            await SendAsync(socket, frame, cancellationToken);
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
            logger.Debug(AppLogKeyword.CloudSync, "SaveHarbor Cloud presence not sent ({Error}); the next connection sends it", exception.GetType().Name);
        }
    }

    private async Task SendAsync(WebSocket socket, string text, CancellationToken cancellationToken)
    {
        await sendGate.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            sendGate.Release();
        }
    }

    // One whole message; null when the server closed the socket. Binary frames (broadcast only) come back empty.
    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferBytes);
        try
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > MaxMessageBytes)
                {
                    throw new WebSocketException(WebSocketError.Faulted);
                }

                if (result.EndOfMessage)
                {
                    return result.MessageType == WebSocketMessageType.Text
                        ? Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)
                        : string.Empty;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task CloseQuietlyAsync(WebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private sealed class HeartbeatState(string token)
    {
        private volatile string? pendingRef;
        private volatile bool joined;

        public string Token { get; set; } = token;

        public string? PendingRef
        {
            get => pendingRef;
            set => pendingRef = value;
        }

        public bool Joined
        {
            get => joined;
            set => joined = value;
        }
    }
}

// The database refused this account on the group's channel: not a member (any more), or the token expired.
public sealed class RealtimeJoinRefusedException() : Exception("SaveHarbor Cloud refused the live updates channel.");
