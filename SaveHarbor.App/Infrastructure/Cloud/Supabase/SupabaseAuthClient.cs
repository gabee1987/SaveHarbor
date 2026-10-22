using System.Net;
using System.Net.Http;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// E-mail code sign-in and the access token for every other SaveHarbor Cloud request. The token is refreshed shortly
// before it expires, by one caller at a time; a refresh the server rejects signs this PC out.
public sealed class SupabaseAuthClient
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly SupabaseHttp http;
    private readonly SupabaseSessionStore store;
    private readonly IAppLogger logger;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly Lock sessionGate = new();

    private SupabaseSession? session;
    private bool loaded;

    public SupabaseAuthClient(SupabaseHttp http, SupabaseSessionStore store, IAppLogger logger, TimeProvider time)
    {
        this.http = http;
        this.store = store;
        this.logger = logger;
        this.time = time;
    }

    public bool IsSignedIn => Current is not null;

    public string? Email => Current?.Email;

    public string? UserId => Current?.UserId;

    private SupabaseSession? Current
    {
        get
        {
            lock (sessionGate)
            {
                if (!loaded)
                {
                    session = store.Load();
                    loaded = true;
                }

                return session;
            }
        }
    }

    // Creates the account on first use; whether the address is new is not revealed to the caller.
    public async Task RequestCodeAsync(string email, CancellationToken cancellationToken = default)
    {
        await http.SendAsync(HttpMethod.Post, "auth/v1/otp", new { email, create_user = true }, null, cancellationToken);
        logger.Information(AppLogKeyword.CloudAuth, "Sign-in code requested");
    }

    public async Task VerifyCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        var dto = await http.SendJsonAsync<AuthSessionDto>(
            HttpMethod.Post,
            "auth/v1/verify",
            new { type = "email", email, token = code },
            null,
            cancellationToken);

        Replace(ToSession(dto, email));
        logger.Information(AppLogKeyword.CloudAuth, "Signed in to SaveHarbor Cloud");
    }

    // The last step of a browser sign-in (Google): the one-time code from the redirect, with the verifier only this PC knows.
    public async Task ExchangeCodeAsync(string authCode, string codeVerifier, CancellationToken cancellationToken = default)
    {
        var dto = await http.SendJsonAsync<AuthSessionDto>(
            HttpMethod.Post,
            "auth/v1/token?grant_type=pkce",
            new { auth_code = authCode, code_verifier = codeVerifier },
            null,
            cancellationToken);

        Replace(ToSession(dto, string.Empty));
        logger.Information(AppLogKeyword.CloudAuth, "Signed in to SaveHarbor Cloud with a browser sign-in");
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var current = Current ?? throw new SupabaseException(SupabaseErrorCodes.NotSignedIn);
        return current.ExpiresAtUtc - time.GetUtcNow() > RefreshMargin
            ? current.AccessToken
            : (await RefreshAsync(current.AccessToken, cancellationToken)).AccessToken;
    }

    // Runs call with the access token; when the server rejects the token anyway, refreshes once and retries.
    public async Task<T> WithAccessTokenAsync<T>(Func<string, CancellationToken, Task<T>> call, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        try
        {
            return await call(token, cancellationToken);
        }
        catch (SupabaseException exception) when (exception.Code == SupabaseErrorCodes.Unauthorized)
        {
            var refreshed = await RefreshAsync(token, cancellationToken);
            try
            {
                return await call(refreshed.AccessToken, cancellationToken);
            }
            catch (SupabaseException again) when (again.Code == SupabaseErrorCodes.Unauthorized)
            {
                logger.Warning(AppLogKeyword.CloudAuth, "SaveHarbor Cloud rejected a fresh sign-in; signing out");
                Replace(null);
                throw new SupabaseException(SupabaseErrorCodes.NotSignedIn, again.Status);
            }
        }
    }

    public async Task WithAccessTokenAsync(Func<string, CancellationToken, Task> call, CancellationToken cancellationToken = default)
    {
        await WithAccessTokenAsync<bool>(
            async (token, callToken) =>
            {
                await call(token, callToken);
                return true;
            },
            cancellationToken);
    }

    // Ends this PC's session only (scope=local): the same account stays signed in on the player's other PCs.
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var current = Current;
        if (current is null)
        {
            return;
        }

        try
        {
            await http.SendAsync(HttpMethod.Post, "auth/v1/logout?scope=local", null, current.AccessToken, cancellationToken);
        }
        catch (SupabaseException exception)
        {
            logger.Warning(AppLogKeyword.CloudAuth, "SaveHarbor Cloud sign-out could not reach the server ({Code}); signed out on this PC", exception.Code);
        }

        Replace(null);
        logger.Information(AppLogKeyword.CloudAuth, "Signed out of SaveHarbor Cloud");
    }

    // Refreshes unless another caller already replaced staleAccessToken with a token that is still fresh.
    private async Task<SupabaseSession> RefreshAsync(string staleAccessToken, CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            var current = Current ?? throw new SupabaseException(SupabaseErrorCodes.NotSignedIn);
            if (current.AccessToken != staleAccessToken && current.ExpiresAtUtc - time.GetUtcNow() > RefreshMargin)
            {
                return current;
            }

            AuthSessionDto dto;
            try
            {
                dto = await http.SendJsonAsync<AuthSessionDto>(
                    HttpMethod.Post,
                    "auth/v1/token?grant_type=refresh_token",
                    new { refresh_token = current.RefreshToken },
                    null,
                    cancellationToken);
            }
            catch (SupabaseException exception) when (exception.Status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                logger.Warning(AppLogKeyword.CloudAuth, "SaveHarbor Cloud sign-in expired ({Code}); signing out", exception.Code);
                Replace(null);
                throw new SupabaseException(SupabaseErrorCodes.NotSignedIn, exception.Status);
            }

            var refreshed = ToSession(dto, current.Email);
            Replace(refreshed);
            logger.Debug(AppLogKeyword.CloudAuth, "SaveHarbor Cloud session refreshed until {ExpiresAt}", refreshed.ExpiresAtUtc);
            return refreshed;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private SupabaseSession ToSession(AuthSessionDto dto, string fallbackEmail)
    {
        if (string.IsNullOrEmpty(dto.AccessToken) || string.IsNullOrEmpty(dto.RefreshToken) || dto.User is null || string.IsNullOrEmpty(dto.User.Id))
        {
            throw new SupabaseException(SupabaseErrorCodes.InvalidData);
        }

        var expiresAt = dto.ExpiresAt is { } unixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : time.GetUtcNow().AddSeconds(dto.ExpiresIn);
        return new SupabaseSession(dto.AccessToken, dto.RefreshToken, expiresAt, dto.User.Email ?? fallbackEmail, dto.User.Id);
    }

    private void Replace(SupabaseSession? replacement)
    {
        lock (sessionGate)
        {
            session = replacement;
            loaded = true;
            if (replacement is null)
            {
                store.Clear();
            }
            else
            {
                store.Save(replacement);
            }
        }
    }
}
