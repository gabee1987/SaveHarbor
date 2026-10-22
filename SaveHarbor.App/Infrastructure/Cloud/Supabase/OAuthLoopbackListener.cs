using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// Receives the one browser redirect that ends a browser sign-in, on this PC only (127.0.0.1), on the first free port of a
// short fixed list: SaveHarbor Cloud redirects only to addresses on its allow-list, so the port cannot be random. A
// program that takes the port first gains nothing: the code it would receive is useless without this sign-in's verifier.
public sealed class OAuthLoopbackListener : IDisposable
{
    public const string CallbackPath = "/auth/callback";

    public static readonly IReadOnlyList<int> Ports = [53287, 53288, 53289];

    private const int MaxRequestBytes = 8 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private const string DonePage =
        "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>SaveHarbor</title>"
        + "<h1>Back to SaveHarbor</h1><p>SaveHarbor is finishing your sign-in. You can close this tab.</p></html>";

    private const string FailedPage =
        "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>SaveHarbor</title>"
        + "<h1>Sign-in did not finish</h1><p>Go back to SaveHarbor and try again. You can close this tab.</p></html>";

    private readonly TcpListener listener;

    private OAuthLoopbackListener(TcpListener listener, int port)
    {
        this.listener = listener;
        RedirectUri = new Uri($"http://127.0.0.1:{port}{CallbackPath}");
    }

    public Uri RedirectUri { get; }

    public static OAuthLoopbackListener Start(IReadOnlyList<int>? ports = null)
    {
        foreach (var port in ports ?? Ports)
        {
            // Exclusive, so no other program can bind the same port alongside and read the redirect.
            var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
            try
            {
                listener.Start(4);
                return new OAuthLoopbackListener(listener, port);
            }
            catch (SocketException)
            {
                listener.Dispose();
            }
        }

        throw new SupabaseException(SupabaseErrorCodes.CallbackPortsBusy);
    }

    // Waits for the browser at the callback address and answers it with a short page. Anything else on the port (a
    // favicon request, a stray program) gets 404 and the wait goes on.
    public async Task<IReadOnlyDictionary<string, string>> WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            if (await TryReadCallbackAsync(client, cancellationToken) is { } query)
            {
                return query;
            }
        }
    }

    public void Dispose() => listener.Dispose();

    private static async Task<IReadOnlyDictionary<string, string>?> TryReadCallbackAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        var stream = client.GetStream();
        try
        {
            var requestLine = await ReadRequestLineAsync(stream, timeout.Token);
            if (requestLine is null || ParseCallback(requestLine) is not { } query)
            {
                await RespondAsync(stream, "404 Not Found", string.Empty, timeout.Token);
                return null;
            }

            await RespondAsync(stream, "200 OK", query.ContainsKey("code") ? DonePage : FailedPage, timeout.Token);
            return query;
        }
        catch (Exception exception) when (exception is IOException or SocketException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // The first line of the request ("GET /auth/callback?code=… HTTP/1.1"), once all headers are in.
    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        while (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) < 0)
        {
            if (length == buffer.Length)
            {
                return null;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                return null;
            }

            length += read;
        }

        var end = buffer.AsSpan(0, length).IndexOf("\r\n"u8);
        return Encoding.ASCII.GetString(buffer, 0, end);
    }

    // The query of a GET to the callback path that carries a code or an error; null for anything else.
    public static IReadOnlyDictionary<string, string>? ParseCallback(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length != 3 || parts[0] != "GET" || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            return null;
        }

        var target = parts[1];
        var queryStart = target.IndexOf('?');
        if ((queryStart < 0 ? target : target[..queryStart]) != CallbackPath || queryStart < 0)
        {
            return null;
        }

        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in target[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = Unescape(separator < 0 ? pair : pair[..separator]);
            var value = separator < 0 ? string.Empty : Unescape(pair[(separator + 1)..]);
            query.TryAdd(name, value);
        }

        return query.ContainsKey("code") || query.ContainsKey("error") ? query : null;
    }

    private static string Unescape(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    // No scripts, styles or images may load, and the address with the code is never sent on as a referrer.
    private static async Task RespondAsync(NetworkStream stream, string status, string html, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n"
            + "Content-Security-Policy: default-src 'none'\r\nReferrer-Policy: no-referrer\r\nCache-Control: no-store\r\n"
            + "X-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
