using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// Google sign-in: PKCE, the loopback callback on this PC, and the code exchange. The browser is played by the test.
public sealed class SupabaseOAuthSignInTests : IDisposable
{
    private readonly SupabaseHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GoogleEnabled_IsReadFromThePublicSettingsOnce()
    {
        harness.Handler.On(HttpMethod.Get, "auth/v1/settings", HttpStatusCode.OK, "{\"external\":{\"email\":true,\"google\":true}}");

        Assert.True(await harness.OAuth.IsGoogleEnabledAsync(Token));
        Assert.True(await harness.OAuth.IsGoogleEnabledAsync(Token));

        var request = Assert.Single(harness.Handler.To("auth/v1/settings"));
        Assert.Null(request.Bearer);
    }

    [Fact]
    public async Task GoogleEnabled_WhenTheCloudCannotBeReached_IsFalseAndAskedAgain()
    {
        harness.Handler.On(HttpMethod.Get, "auth/v1/settings", HttpStatusCode.ServiceUnavailable, "{}");

        Assert.False(await harness.OAuth.IsGoogleEnabledAsync(Token));
        Assert.False(await harness.OAuth.IsGoogleEnabledAsync(Token));

        Assert.Equal(2, harness.Handler.To("auth/v1/settings").Count());
    }

    [Fact]
    public async Task SignIn_ExchangesTheCodeWithTheVerifierOfTheChallenge()
    {
        harness.Browser.ReplyQuery = "code=TEST_CODE";
        harness.Handler.OnFixture(HttpMethod.Post, "auth/v1/token", "session.json");

        await harness.OAuth.SignInWithGoogleAsync(Token);

        Assert.True(harness.Auth.IsSignedIn);
        Assert.Equal("TEST_ACCESS_TOKEN_2", harness.SessionStore.Load()!.AccessToken);
        var authorize = Assert.Single(harness.Browser.Opened);
        Assert.StartsWith("https://TEST_REF.supabase.co/auth/v1/authorize?provider=google&", authorize.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("code_challenge_method=s256", authorize.Query);
        Assert.Matches("^http://127\\.0\\.0\\.1:5328[789]/auth/callback$", RecordingBrowserLauncher.RedirectTarget(authorize));

        var exchange = Assert.Single(harness.Handler.To("auth/v1/token"));
        Assert.Contains("grant_type=pkce", exchange.Query);
        Assert.Contains("\"auth_code\":\"TEST_CODE\"", exchange.Body);
        var verifier = System.Text.Json.JsonDocument.Parse(exchange.Body).RootElement.GetProperty("code_verifier").GetString()!;
        Assert.Equal(43, verifier.Length);
        Assert.Contains($"code_challenge={SupabaseOAuthSignIn.Challenge(verifier)}&", authorize.Query);
        await harness.Browser.Reply!;
        Assert.Contains("You can close this tab", harness.Browser.ReplyPage);
    }

    [Fact]
    public async Task SignIn_CancelledAtGoogle_SignsNobodyIn()
    {
        harness.Browser.ReplyQuery = "error=access_denied&error_description=The+user+denied";

        var failure = await Assert.ThrowsAsync<SupabaseException>(() => harness.OAuth.SignInWithGoogleAsync(Token));

        Assert.Equal(SupabaseErrorCodes.BrowserSignInCancelled, failure.Code);
        Assert.False(harness.Auth.IsSignedIn);
        Assert.Empty(harness.Handler.To("auth/v1/token"));
    }

    [Fact]
    public async Task SignIn_CancelledInTheApp_StopsListening()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var signIn = harness.OAuth.SignInWithGoogleAsync(cancel.Token);
        var port = new Uri(RecordingBrowserLauncher.RedirectTarget(Assert.Single(harness.Browser.Opened))).Port;

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signIn);
        using var again = new TcpListener(IPAddress.Loopback, port);
        again.Start();
    }

    [Fact]
    public async Task SignIn_StaleCode_IsNotASignIn()
    {
        harness.Browser.ReplyQuery = "code=TEST_CODE";
        harness.Handler.On(HttpMethod.Post, "auth/v1/token", HttpStatusCode.NotFound, "{\"code\":404,\"error_code\":\"flow_state_not_found\"}");

        var result = await harness.Groups.SignInWithGoogleAsync(Token);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("Google sign-in did not finish", result.Message);
        Assert.False(harness.Auth.IsSignedIn);
    }

    [Fact]
    public async Task GroupService_TurnsAFailedSignInIntoItsReason()
    {
        harness.Browser.ReplyQuery = "error=server_error";

        var result = await harness.Groups.SignInWithGoogleAsync(Token);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("Google sign-in did not finish", result.Message);
    }

    [Fact]
    public void Listener_TakesTheNextPortWhenOneIsBusy()
    {
        using var busy = new TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        var busyPort = ((IPEndPoint)busy.LocalEndpoint).Port;
        int freePort;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        using var listener = OAuthLoopbackListener.Start([busyPort, freePort]);

        Assert.Equal(new Uri($"http://127.0.0.1:{freePort}/auth/callback"), listener.RedirectUri);
        var failure = Assert.Throws<SupabaseException>(() => OAuthLoopbackListener.Start([busyPort]));
        Assert.Equal(SupabaseErrorCodes.CallbackPortsBusy, failure.Code);
    }

    [Theory]
    [InlineData("GET /favicon.ico HTTP/1.1")]
    [InlineData("GET /auth/callback HTTP/1.1")]
    [InlineData("GET /auth/callback?state=TEST HTTP/1.1")]
    [InlineData("POST /auth/callback?code=TEST_CODE HTTP/1.1")]
    [InlineData("GET /auth/callback/other?code=TEST_CODE HTTP/1.1")]
    [InlineData("GET /auth/callback?code=TEST_CODE")]
    public void Callback_OtherRequests_AreIgnored(string requestLine)
    {
        Assert.Null(OAuthLoopbackListener.ParseCallback(requestLine));
    }

    [Fact]
    public void Callback_QueryIsDecoded()
    {
        var query = OAuthLoopbackListener.ParseCallback("GET /auth/callback?error=access_denied&error_description=The+user%20denied HTTP/1.1")!;

        Assert.Equal("access_denied", query["error"]);
        Assert.Equal("The user denied", query["error_description"]);
    }
}
