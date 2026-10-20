using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// The only HttpClient of SaveHarbor Cloud. Adds the public API key and the caller's bearer token, applies timeouts, and
// turns every failure into a SupabaseException. Logs method, path, status and time; never bodies, queries or tokens.
public sealed partial class SupabaseHttp : IDisposable
{
    private const int MaxJsonBytes = 4 * 1024 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 16
    };

    private readonly HttpClient client;
    private readonly SupabaseOptions options;
    private readonly IAppLogger logger;

    // handler: tests pass a scripted handler; the app uses a pooled socket handler.
    public SupabaseHttp(SupabaseOptions options, IAppLogger logger, HttpMessageHandler? handler = null)
    {
        this.options = options;
        this.logger = logger;
        client = new HttpClient(handler ?? new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        if (options.IsConfigured)
        {
            client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("apikey", options.PublishableKey);
        }
    }

    public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(options.HttpTimeoutSeconds);

    public TimeSpan TransferTimeout => TimeSpan.FromSeconds(options.TransferTimeoutSeconds);

    [GeneratedRegex("^[a-z][a-z_]{1,63}$")]
    private static partial Regex ErrorCodePattern();

    // Sends a JSON request and returns the response body (empty for 204).
    public Task<string> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? bearer,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        return SendRawAsync(
            () =>
            {
                var request = new HttpRequestMessage(method, path);
                if (body is not null)
                {
                    request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
                }

                foreach (var (name, value) in headers ?? new Dictionary<string, string>())
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }

                return request;
            },
            bearer,
            DefaultTimeout,
            HttpCompletionOption.ResponseContentRead,
            ReadBodyAsync,
            cancellationToken);
    }

    public async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? bearer,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var text = await SendAsync(method, path, body, bearer, cancellationToken, headers);
        return Deserialize<T>(text);
    }

    public Task<T> RpcAsync<T>(string function, object args, string bearer, CancellationToken cancellationToken) =>
        SendJsonAsync<T>(HttpMethod.Post, $"rest/v1/rpc/{function}", args, bearer, cancellationToken);

    public Task RpcAsync(string function, object args, string bearer, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"rest/v1/rpc/{function}", args, bearer, cancellationToken);

    // Sends createRequest's request (built again for every call, so the caller can retry with a new token) and hands a
    // successful response to read within the same timeout.
    public async Task<T> SendRawAsync<T>(
        Func<HttpRequestMessage> createRequest,
        string? bearer,
        TimeSpan timeout,
        HttpCompletionOption completion,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
        {
            throw new SupabaseException(SupabaseErrorCodes.NotConfigured);
        }

        using var request = createRequest();
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var path = request.RequestUri?.OriginalString.Split('?')[0] ?? string.Empty;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await client.SendAsync(request, completion, timeoutSource.Token);
            logger.Debug(AppLogKeyword.CloudProvider, "SaveHarbor Cloud {Method} {Path} -> {Status} in {ElapsedMs} ms", request.Method, path, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadBodyAsync(response, timeoutSource.Token);
                throw CreateError(response.StatusCode, errorBody, response.Headers.RetryAfter);
            }

            return await read(response, timeoutSource.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.Debug(AppLogKeyword.CloudProvider, "SaveHarbor Cloud {Method} {Path} timed out after {ElapsedMs} ms", request.Method, path, stopwatch.ElapsedMilliseconds);
            throw new SupabaseException(SupabaseErrorCodes.Unreachable, hostedProject: options.IsHostedProject, innerException: exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or HttpIOException)
        {
            logger.Debug(AppLogKeyword.CloudProvider, "SaveHarbor Cloud {Method} {Path} failed: {Error}", request.Method, path, exception.GetType().Name);
            throw new SupabaseException(SupabaseErrorCodes.Unreachable, hostedProject: options.IsHostedProject, innerException: exception);
        }
    }

    public static T Deserialize<T>(string text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text)
                ? throw new SupabaseException(SupabaseErrorCodes.InvalidData)
                : JsonSerializer.Deserialize<T>(text, JsonOptions) ?? throw new SupabaseException(SupabaseErrorCodes.InvalidData);
        }
        catch (JsonException exception)
        {
            throw new SupabaseException(SupabaseErrorCodes.InvalidData, innerException: exception);
        }
    }

    // Reads at most 4 MB; SaveHarbor Cloud never sends more JSON than that.
    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxJsonBytes)
            {
                throw new SupabaseException(SupabaseErrorCodes.InvalidData);
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    // PostgREST reports a function's error code in "message", Auth in "error_code". Storage puts its own status in
    // "statusCode" and answers some errors with HTTP 400, so that status wins when present.
    public SupabaseException CreateError(HttpStatusCode status, string body, RetryConditionHeaderValue? retryAfter)
    {
        string? bodyCode = null;
        var storageError = string.Empty;
        var storageMessage = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = document.RootElement;
                bodyCode = new[] { ReadString(root, "error_code"), ReadString(root, "message"), ReadString(root, "error") }
                    .FirstOrDefault(value => value is not null && ErrorCodePattern().IsMatch(value));
                if (int.TryParse(ReadString(root, "statusCode"), out var storageStatus) && storageStatus is >= 400 and < 600)
                {
                    status = (HttpStatusCode)storageStatus;
                }

                storageError = ReadString(root, "error") ?? string.Empty;
                storageMessage = ReadString(root, "message") ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Not JSON (for example a gateway error page): the status decides.
        }

        var expiredToken = storageError.Equals("InvalidJWT", StringComparison.OrdinalIgnoreCase)
            || storageMessage.Contains("exp", StringComparison.OrdinalIgnoreCase) && storageMessage.Contains("claim", StringComparison.OrdinalIgnoreCase);

        var code = (int)status switch
        {
            401 => SupabaseErrorCodes.Unauthorized,
            403 when expiredToken => SupabaseErrorCodes.Unauthorized,
            413 => SupabaseErrorCodes.PayloadTooLarge,
            429 => bodyCode ?? SupabaseErrorCodes.RateLimited,
            >= 500 => SupabaseErrorCodes.ServerError,
            _ when bodyCode is not null => bodyCode,
            403 => SupabaseErrorCodes.Forbidden,
            404 => SupabaseErrorCodes.NotFound,
            409 => SupabaseErrorCodes.Conflict,
            _ => SupabaseErrorCodes.BadRequest
        };

        var wait = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return new SupabaseException(code, status, wait, (int)status >= 500 && options.IsHostedProject);
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    public void Dispose() => client.Dispose();
}
