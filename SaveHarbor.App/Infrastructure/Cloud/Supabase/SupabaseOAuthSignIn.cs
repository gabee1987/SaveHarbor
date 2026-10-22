using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// Google sign-in for SaveHarbor Cloud: OAuth 2.0 with PKCE (RFC 7636) and a loopback redirect (RFC 8252). The browser opens
// the project's Google sign-in, Google sends the player back to the project, the project redirects to this PC with a
// one-time code, and the app exchanges that code together with a verifier that never left this PC. The app never sees
// the player's Google password or Google's own tokens. An existing e-mail account with the same address is the same
// SaveHarbor account.
public sealed partial class SupabaseOAuthSignIn
{
    public const string GoogleProvider = "google";

    private static readonly TimeSpan BrowserTimeout = TimeSpan.FromMinutes(5);

    private readonly SupabaseHttp http;
    private readonly SupabaseOptions options;
    private readonly SupabaseAuthClient auth;
    private readonly IBrowserLauncher browser;
    private readonly IAppLogger logger;
    private bool? googleEnabled;

    public SupabaseOAuthSignIn(SupabaseHttp http, SupabaseOptions options, SupabaseAuthClient auth, IBrowserLauncher browser, IAppLogger logger)
    {
        this.http = http;
        this.options = options;
        this.auth = auth;
        this.browser = browser;
        this.logger = logger;
    }

    [GeneratedRegex("^[a-z_]{1,64}$")]
    private static partial Regex OAuthErrorPattern();

    // Whether the project has Google sign-in switched on, from its public auth settings. Remembered once known; when the
    // project cannot be reached the button stays hidden and the next sign-in screen asks again.
    public async Task<bool> IsGoogleEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (googleEnabled is { } known)
        {
            return known;
        }

        if (!options.IsConfigured)
        {
            return false;
        }

        try
        {
            var settings = await http.SendJsonAsync<AuthSettingsDto>(HttpMethod.Get, "auth/v1/settings", null, null, cancellationToken);
            googleEnabled = settings.External?.Google == true;
            return googleEnabled.Value;
        }
        catch (SupabaseException exception)
        {
            logger.Debug(AppLogKeyword.CloudAuth, "SaveHarbor Cloud sign-in options unavailable ({Code})", exception.Code);
            return false;
        }
    }

    public async Task SignInWithGoogleAsync(CancellationToken cancellationToken = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        using var callback = OAuthLoopbackListener.Start();
        browser.Open(AuthorizeUri(GoogleProvider, callback.RedirectUri, Challenge(verifier)));
        logger.Information(AppLogKeyword.CloudAuth, "Google sign-in opened in the browser");

        IReadOnlyDictionary<string, string> query;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(BrowserTimeout);
            try
            {
                query = await callback.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SupabaseException(SupabaseErrorCodes.BrowserSignInTimedOut);
            }
        }

        if (query.TryGetValue("error", out var error))
        {
            var safeError = OAuthErrorPattern().IsMatch(error) ? error : "other";
            logger.Warning(AppLogKeyword.CloudAuth, "Google sign-in did not finish ({Error})", safeError);
            throw new SupabaseException(safeError == "access_denied" ? SupabaseErrorCodes.BrowserSignInCancelled : SupabaseErrorCodes.BrowserSignInFailed);
        }

        if (!query.TryGetValue("code", out var code) || code.Length is 0 or > 512)
        {
            throw new SupabaseException(SupabaseErrorCodes.BrowserSignInFailed);
        }

        await auth.ExchangeCodeAsync(code, verifier, cancellationToken);
    }

    // select_account lets a player who is signed in to several Google accounts, or shares the PC, pick the right one.
    public Uri AuthorizeUri(string provider, Uri redirectUri, string codeChallenge) =>
        new($"{options.Url.TrimEnd('/')}/auth/v1/authorize?provider={provider}"
            + $"&redirect_to={Uri.EscapeDataString(redirectUri.AbsoluteUri)}"
            + $"&code_challenge={codeChallenge}&code_challenge_method=s256&prompt=select_account");

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
