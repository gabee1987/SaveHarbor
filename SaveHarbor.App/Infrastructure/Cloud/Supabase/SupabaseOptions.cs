namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// The SaveHarbor Cloud project the app talks to. Url and PublishableKey are public values; the service_role key never
// belongs here.
public sealed class SupabaseOptions
{
    public string Url { get; set; } = string.Empty;

    public string PublishableKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "saves";

    // 45 MB, below the bucket's own limit so the app refuses before uploading.
    public long MaxArchiveBytes { get; set; } = 47185920;

    public int HttpTimeoutSeconds { get; set; } = 30;

    public int TransferTimeoutSeconds { get; set; } = 300;

    // Tokens travel only over HTTPS; plain HTTP is accepted for the local development stack alone.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublishableKey)
        && Uri.TryCreate(Url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    public bool IsHostedProject =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase);
}
