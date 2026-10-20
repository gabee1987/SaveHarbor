using System.Net;
using System.Net.Http.Headers;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// HTTP failures become error codes, and error codes become what the player reads.
public sealed class SupabaseErrorMapperTests
{
    private static SupabaseHttp Http(string url = "https://TEST_REF.supabase.co") =>
        new(new SupabaseOptions { Url = url, PublishableKey = "sb_publishable_TEST" }, new NullAppLogger(), new RecordingHttpMessageHandler());

    [Theory]
    [InlineData(400, """{"code":"P0001","message":"version_conflict"}""", "version_conflict")]
    [InlineData(400, """{"code":"P0001","message":"invite_invalid"}""", "invite_invalid")]
    [InlineData(403, """{"code":403,"error_code":"otp_expired","msg":"Token has expired or is invalid"}""", "otp_expired")]
    [InlineData(429, """{"code":429,"error_code":"over_email_send_rate_limit","msg":"rate limited"}""", "over_email_send_rate_limit")]
    [InlineData(401, """{"code":"PGRST303","message":"JWT expired"}""", "unauthorized")]
    [InlineData(400, """{"statusCode":"413","error":"Payload too large","message":"The object exceeded the maximum allowed size"}""", "payload_too_large")]
    [InlineData(400, """{"statusCode":"403","error":"Unauthorized","message":"new row violates row-level security policy"}""", "forbidden")]
    [InlineData(400, """{"statusCode":"403","error":"InvalidJWT","message":"\"exp\" claim timestamp check failed"}""", "unauthorized")]
    [InlineData(409, """{"statusCode":"409","error":"Duplicate","message":"The resource already exists"}""", "conflict")]
    [InlineData(404, "", "not_found")]
    [InlineData(502, "<html>Bad gateway</html>", "server_error")]
    [InlineData(540, "", "server_error")]
    [InlineData(400, """{"message":"Something Went Wrong"}""", "bad_request")]
    public void CreateError_ReadsTheCodeFromBodyOrStatus(int status, string body, string expectedCode)
    {
        using var http = Http();

        var exception = http.CreateError((HttpStatusCode)status, body, null);

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void CreateError_KeepsRetryAfter()
    {
        using var http = Http();

        var exception = http.CreateError(HttpStatusCode.TooManyRequests, string.Empty, new RetryConditionHeaderValue(TimeSpan.FromSeconds(42)));

        Assert.Equal(SupabaseErrorCodes.RateLimited, exception.Code);
        Assert.Equal(TimeSpan.FromSeconds(42), exception.RetryAfter);
    }

    [Theory]
    [InlineData("https://TEST_REF.supabase.co", true)]
    [InlineData("http://127.0.0.1:54321", false)]
    public void ServerError_HintsAtAPausedProjectOnlyWhenHosted(string url, bool hinted)
    {
        using var http = Http(url);

        var message = SupabaseErrorMapper.Describe(http.CreateError(HttpStatusCode.ServiceUnavailable, string.Empty, null)).Message;

        Assert.StartsWith("SaveHarbor cannot reach the group's cloud.", message);
        Assert.Equal(hinted, message.Contains("paused", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SupabaseErrorCodes.InviteInvalid, AppErrorCode.InvalidOperation, "No group has this invite code. Check it with the group owner.")]
    [InlineData(SupabaseErrorCodes.VersionConflict, AppErrorCode.CloudConflict, "Someone shared a newer version while you were uploading. Download it first.")]
    [InlineData(SupabaseErrorCodes.LockLost, AppErrorCode.CloudConflict, "Your session lock expired and another player took over.")]
    [InlineData(SupabaseErrorCodes.NotOwner, AppErrorCode.InvalidOperation, "Only the group owner can do this.")]
    [InlineData(SupabaseErrorCodes.OwnerMustStay, AppErrorCode.InvalidOperation, "The owner cannot leave while others are in the group.")]
    [InlineData(SupabaseErrorCodes.NotSignedIn, AppErrorCode.CloudAuth, "Your sign-in expired. Sign in again to continue.")]
    [InlineData(SupabaseErrorCodes.Unauthorized, AppErrorCode.CloudAuth, "Your sign-in expired. Sign in again to continue.")]
    [InlineData(SupabaseErrorCodes.Unreachable, AppErrorCode.CloudProviderFailure, "SaveHarbor cannot reach the group's cloud. Check your connection.")]
    [InlineData("some_future_code", AppErrorCode.CloudProviderFailure, "The group's cloud refused the request. Try again; if it keeps failing, check the log.")]
    public void Describe_GivesThePlayerText(string code, AppErrorCode expectedCode, string expectedMessage)
    {
        var (appCode, message) = SupabaseErrorMapper.Describe(new SupabaseException(code));

        Assert.Equal(expectedCode, appCode);
        Assert.Equal(expectedMessage, message);
    }

    [Fact]
    public void Describe_RateLimited_SaysHowLongToWait()
    {
        var message = SupabaseErrorMapper.Describe(new SupabaseException(SupabaseErrorCodes.RateLimited, retryAfter: TimeSpan.FromSeconds(29.2))).Message;

        Assert.Equal("Too many attempts. Try again in 30 seconds.", message);
    }

    [Fact]
    public void ErrorHandler_UsesTheCloudText()
    {
        var error = new AppErrorHandler(new NullAppLogger()).Handle(new SupabaseException(SupabaseErrorCodes.ServerError), "Upload", AppLogKeyword.CloudUpload);

        Assert.Equal(AppErrorCode.CloudProviderFailure, error.Code);
        Assert.StartsWith("SaveHarbor cannot reach the group's cloud.", error.UserMessage);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public void ExceptionMessage_NamesOnlyCodeAndStatus()
    {
        var exception = new SupabaseException(SupabaseErrorCodes.Forbidden, HttpStatusCode.Forbidden);

        Assert.Equal("SaveHarbor Cloud request failed: forbidden (HTTP 403)", exception.Message);
    }
}
