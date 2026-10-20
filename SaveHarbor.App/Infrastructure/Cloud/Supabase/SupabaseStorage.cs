using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// World archives in the private storage bucket. Files are streamed, never held in memory as a whole.
public sealed class SupabaseStorage(SupabaseHttp http, SupabaseAuthClient auth, SupabaseOptions options)
{
    // Storage removes at most this many objects per request.
    public const int DeleteBatchSize = 100;

    private const int ListPageSize = 1000;

    public Task UploadAsync(string objectPath, string filePath, CancellationToken cancellationToken = default) =>
        auth.WithAccessTokenAsync(
            async (token, callToken) =>
            {
                await using var file = File.OpenRead(filePath);
                await http.SendRawAsync(
                    () =>
                    {
                        var content = new StreamContent(file);
                        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                        var request = new HttpRequestMessage(HttpMethod.Post, ObjectUrl(objectPath)) { Content = content };
                        request.Headers.TryAddWithoutValidation("x-upsert", "false");
                        return request;
                    },
                    token,
                    http.TransferTimeout,
                    HttpCompletionOption.ResponseContentRead,
                    (_, _) => Task.FromResult(true),
                    callToken);
            },
            cancellationToken);

    // Refuses an object larger than the bucket allows, so a bad upload cannot fill this PC's disk.
    public Task DownloadAsync(string objectPath, string targetPath, CancellationToken cancellationToken = default) =>
        auth.WithAccessTokenAsync(
            (token, callToken) => http.SendRawAsync(
                () => new HttpRequestMessage(HttpMethod.Get, $"storage/v1/object/authenticated/{Escape(options.Bucket)}/{EscapePath(objectPath)}"),
                token,
                http.TransferTimeout,
                HttpCompletionOption.ResponseHeadersRead,
                async (response, readToken) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    await using var source = await response.Content.ReadAsStreamAsync(readToken);
                    await using var target = File.Create(targetPath);
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, readToken)) > 0)
                    {
                        total += read;
                        if (total > options.MaxArchiveBytes)
                        {
                            throw new SupabaseException(SupabaseErrorCodes.PayloadTooLarge);
                        }

                        await target.WriteAsync(buffer.AsMemory(0, read), readToken);
                    }

                    return true;
                },
                callToken),
            cancellationToken);

    // Everything directly inside a folder ("<group>/<game>/"), folders included, read in pages.
    public async Task<IReadOnlyList<StorageObjectDto>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var entries = new List<StorageObjectDto>();
        while (true)
        {
            var page = await auth.WithAccessTokenAsync(
                (token, callToken) => http.SendJsonAsync<List<StorageObjectDto>>(
                    HttpMethod.Post,
                    $"storage/v1/object/list/{Escape(options.Bucket)}",
                    new { prefix, limit = ListPageSize, offset = entries.Count, sortBy = new { column = "name", order = "asc" } },
                    token,
                    callToken),
                cancellationToken);
            entries.AddRange(page);
            if (page.Count < ListPageSize)
            {
                return entries;
            }
        }
    }

    public async Task DeleteAsync(IReadOnlyList<string> objectPaths, CancellationToken cancellationToken = default)
    {
        foreach (var batch in objectPaths.Chunk(DeleteBatchSize))
        {
            await auth.WithAccessTokenAsync(
                (token, callToken) => http.SendAsync(HttpMethod.Delete, $"storage/v1/object/{Escape(options.Bucket)}", new { prefixes = batch }, token, callToken),
                cancellationToken);
        }
    }

    private string ObjectUrl(string objectPath) => $"storage/v1/object/{Escape(options.Bucket)}/{EscapePath(objectPath)}";

    private static string EscapePath(string objectPath) => string.Join('/', objectPath.Split('/').Select(Escape));

    private static string Escape(string segment) => Uri.EscapeDataString(segment);
}
