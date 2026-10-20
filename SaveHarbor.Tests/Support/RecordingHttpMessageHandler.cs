using System.Net;
using System.Net.Http;
using System.Text;

namespace SaveHarbor.Tests.Support;

// Answers requests from scripted routes ("METHOD path", query ignored) and records every request. A route's responses
// are used in order; the last one repeats. An unrouted request gets 501, so a test sees it fail.
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> routes = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    public List<RecordedRequest> Requests { get; } = [];

    // Delays every response, so concurrent callers overlap.
    public TimeSpan Delay { get; set; }

    public RecordingHttpMessageHandler On(
        HttpMethod method,
        string path,
        HttpStatusCode status,
        string body = "",
        Action<HttpResponseMessage>? configure = null)
    {
        var key = $"{method.Method} {path.TrimStart('/')}";
        lock (gate)
        {
            if (!routes.TryGetValue(key, out var queue))
            {
                routes[key] = queue = new Queue<Func<HttpResponseMessage>>();
            }

            queue.Enqueue(() =>
            {
                var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                configure?.Invoke(response);
                return response;
            });
        }

        return this;
    }

    public RecordingHttpMessageHandler OnFixture(HttpMethod method, string path, string fixtureName, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, path, status, Fixture(fixtureName));

    public static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cloud", "Supabase", "Fixtures", name));

    public IReadOnlyList<RecordedRequest> To(string path) =>
        Requests.Where(request => request.Path == path.TrimStart('/')).ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
        Func<HttpResponseMessage>? respond = null;
        lock (gate)
        {
            Requests.Add(new RecordedRequest(request.Method, path, request.RequestUri.Query, body, request.Headers.Authorization?.Parameter, headers));
            if (routes.TryGetValue($"{request.Method.Method} {path}", out var queue) && queue.Count > 0)
            {
                respond = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            }
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return respond?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotImplemented) { Content = new StringContent("unrouted") };
    }

    public sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Query,
        string Body,
        string? Bearer,
        IReadOnlyDictionary<string, string> Headers);
}
