using System.Net.Http;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

// Records the opened addresses instead of starting a browser. With a reply, it plays the browser at the end of a sign-in:
// it follows the project's redirect to the app's callback address with the given query (for example "code=TEST_CODE").
public sealed class RecordingBrowserLauncher : IBrowserLauncher
{
    public List<Uri> Opened { get; } = [];

    public string? ReplyQuery { get; set; }

    public Task? Reply { get; private set; }

    public string? ReplyPage { get; private set; }

    public void Open(Uri address)
    {
        Opened.Add(address);
        if (ReplyQuery is { } query)
        {
            Reply = ReplyAsync(new Uri($"{RedirectTarget(address)}?{query}"));
        }
    }

    public static string RedirectTarget(Uri authorize) =>
        Uri.UnescapeDataString(authorize.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("redirect_to=", StringComparison.Ordinal))["redirect_to=".Length..]);

    private async Task ReplyAsync(Uri callback)
    {
        using var client = new HttpClient();
        ReplyPage = await client.GetStringAsync(callback);
    }
}
