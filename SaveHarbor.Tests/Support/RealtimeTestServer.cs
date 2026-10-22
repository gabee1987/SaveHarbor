using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Channels;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

// Each connection of the client becomes a WebSocket pair over a loopback TCP connection; tests script the server
// side. No HTTP upgrade is needed, so nothing has to be registered with Windows.
public sealed class RealtimeTestServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly Channel<WebSocket> accepted = Channel.CreateUnbounded<WebSocket>();
    private readonly List<IDisposable> resources = [];
    private int connectionCount;

    private readonly CancellationToken testToken;

    public RealtimeTestServer(CancellationToken testToken)
    {
        this.testToken = testToken;
        listener.Start();
    }

    public int ConnectionCount => Volatile.Read(ref connectionCount);

    public async Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        Assert.Equal("ws", uri.Scheme);
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellationToken);
        var serverSide = await listener.AcceptTcpClientAsync(cancellationToken);
        lock (resources)
        {
            resources.Add(client);
            resources.Add(serverSide);
        }

        Interlocked.Increment(ref connectionCount);
        accepted.Writer.TryWrite(WebSocket.CreateFromStream(serverSide.GetStream(), new WebSocketCreationOptions { IsServer = true }));
        return WebSocket.CreateFromStream(client.GetStream(), new WebSocketCreationOptions { IsServer = false });
    }

    public async Task<WebSocket> NextAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        return await accepted.Reader.ReadAsync(timeout.Token);
    }

    public ValueTask DisposeAsync()
    {
        listener.Stop();
        lock (resources)
        {
            resources.ForEach(resource => resource.Dispose());
        }

        return ValueTask.CompletedTask;
    }
}

// Keeps every log line with its values, so a test can check that no token was written.
public sealed class RecordingLogger : IAppLogger
{
    private readonly List<string> lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (lines)
            {
                return lines.ToArray();
            }
        }
    }

    public void Debug(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues) => Add(messageTemplate, propertyValues);

    public void Information(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues) => Add(messageTemplate, propertyValues);

    public void Warning(AppLogKeyword keyword, string messageTemplate, params object?[] propertyValues) => Add(messageTemplate, propertyValues);

    public void Error(AppLogKeyword keyword, Exception exception, string messageTemplate, params object?[] propertyValues) =>
        Add(messageTemplate + " " + exception.Message, propertyValues);

    private void Add(string template, object?[] values)
    {
        lock (lines)
        {
            lines.Add(template + " " + string.Join(' ', values));
        }
    }
}
