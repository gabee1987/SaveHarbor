using System.Net;
using System.Net.Http;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// E-mail code sign-in, token refresh shortly before expiry, and sign-out.
public sealed class SupabaseAuthClientTests : IDisposable
{
    private readonly SupabaseHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RequestCode_AsksAuthToSendACode()
    {
        harness.Handler.On(HttpMethod.Post, "auth/v1/otp", HttpStatusCode.OK, "{}");

        await harness.Auth.RequestCodeAsync("test@example.invalid", Token);

        var request = harness.Handler.To("auth/v1/otp").Single();
        Assert.Contains("\"email\":\"test@example.invalid\"", request.Body);
        Assert.Contains("\"create_user\":true", request.Body);
        Assert.Null(request.Bearer);
    }

    [Fact]
    public async Task VerifyCode_SavesTheSession()
    {
        harness.Handler.OnFixture(HttpMethod.Post, "auth/v1/verify", "session.json");

        await harness.Auth.VerifyCodeAsync("test@example.invalid", "123456", Token);

        Assert.True(harness.Auth.IsSignedIn);
        Assert.Equal(SupabaseHarness.UserId, harness.Auth.UserId);
        Assert.Contains("\"type\":\"email\"", harness.Handler.To("auth/v1/verify").Single().Body);
        var saved = harness.SessionStore.Load()!;
        Assert.Equal("TEST_ACCESS_TOKEN_2", saved.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792500000), saved.ExpiresAtUtc);
    }

    [Fact]
    public async Task AccessToken_StillFresh_IsUsedAsIs()
    {
        harness.SignIn(TimeSpan.FromMinutes(5));

        Assert.Equal(SupabaseHarness.AccessToken, await harness.Auth.GetAccessTokenAsync(Token));
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task AccessToken_WithinAMinuteOfExpiry_IsRefreshed()
    {
        harness.SignIn(TimeSpan.FromMinutes(5));
        harness.Handler.OnFixture(HttpMethod.Post, "auth/v1/token", "session.json");
        harness.Clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(1));

        var token = await harness.Auth.GetAccessTokenAsync(Token);

        Assert.Equal("TEST_ACCESS_TOKEN_2", token);
        Assert.Equal("TEST_REFRESH_TOKEN_2", harness.SessionStore.Load()!.RefreshToken);
        Assert.Contains("grant_type=refresh_token", harness.Handler.To("auth/v1/token").Single().Query);
    }

    [Fact]
    public async Task ConcurrentCallers_RefreshOnlyOnce()
    {
        harness.SignIn(TimeSpan.FromSeconds(30));
        harness.Handler.OnFixture(HttpMethod.Post, "auth/v1/token", "session.json");
        harness.Handler.Delay = TimeSpan.FromMilliseconds(100);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => harness.Auth.GetAccessTokenAsync(Token)));

        Assert.All(tokens, token => Assert.Equal("TEST_ACCESS_TOKEN_2", token));
        Assert.Single(harness.Handler.To("auth/v1/token"));
    }

    [Fact]
    public async Task RejectedRefresh_SignsOut()
    {
        harness.SignIn(TimeSpan.FromSeconds(30));
        harness.Handler.On(HttpMethod.Post, "auth/v1/token", HttpStatusCode.BadRequest, """{"code":400,"error_code":"refresh_token_not_found","msg":"Invalid Refresh Token"}""");

        var exception = await Assert.ThrowsAsync<SupabaseException>(() => harness.Auth.GetAccessTokenAsync(Token));

        Assert.Equal(SupabaseErrorCodes.NotSignedIn, exception.Code);
        Assert.False(harness.Auth.IsSignedIn);
        Assert.False(File.Exists(harness.Paths.SupabaseSessionPath));
    }

    [Fact]
    public async Task RefreshWhileOffline_KeepsTheSession()
    {
        harness.SignIn(TimeSpan.FromSeconds(30));
        harness.Handler.On(HttpMethod.Post, "auth/v1/token", HttpStatusCode.ServiceUnavailable);

        var exception = await Assert.ThrowsAsync<SupabaseException>(() => harness.Auth.GetAccessTokenAsync(Token));

        Assert.Equal(SupabaseErrorCodes.ServerError, exception.Code);
        Assert.True(harness.Auth.IsSignedIn);
    }

    [Fact]
    public async Task NotSignedIn_NeedsNoRequest()
    {
        var exception = await Assert.ThrowsAsync<SupabaseException>(() => harness.Auth.GetAccessTokenAsync(Token));

        Assert.Equal(SupabaseErrorCodes.NotSignedIn, exception.Code);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task SignOut_EndsThisPcsSessionAndClearsTheStore()
    {
        harness.SignIn();
        harness.Handler.On(HttpMethod.Post, "auth/v1/logout", HttpStatusCode.NoContent);

        await harness.Auth.SignOutAsync(Token);

        var logout = harness.Handler.To("auth/v1/logout").Single();
        Assert.Equal("?scope=local", logout.Query);
        Assert.Equal(SupabaseHarness.AccessToken, logout.Bearer);
        Assert.False(harness.Auth.IsSignedIn);
        Assert.False(File.Exists(harness.Paths.SupabaseSessionPath));
    }

    [Fact]
    public async Task SignOut_Offline_StillSignsOutLocally()
    {
        harness.SignIn();
        harness.Handler.On(HttpMethod.Post, "auth/v1/logout", HttpStatusCode.BadGateway);

        await harness.Auth.SignOutAsync(Token);

        Assert.False(harness.Auth.IsSignedIn);
    }
}
