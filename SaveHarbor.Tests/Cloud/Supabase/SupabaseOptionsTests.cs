using System.Net.Http;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// The project URL is the root only: the app adds auth/v1, rest/v1 and storage/v1 itself. A URL copied with a path would
// send every request somewhere that does not exist, so it counts as not configured instead.
public sealed class SupabaseOptionsTests
{
    [Theory]
    [InlineData("https://TEST_REF.supabase.co", true)]
    [InlineData("https://TEST_REF.supabase.co/", true)]
    [InlineData("http://127.0.0.1:54321", true)]
    [InlineData("https://TEST_REF.supabase.co/rest/v1", false)]
    [InlineData("https://TEST_REF.supabase.co/rest/v1/", false)]
    [InlineData("https://TEST_REF.supabase.co/auth/v1", false)]
    [InlineData("https://TEST_REF.supabase.co?apikey=TEST", false)]
    [InlineData("http://TEST_REF.supabase.co", false)]
    [InlineData("TEST_REF.supabase.co", false)]
    [InlineData("", false)]
    public void IsConfigured_NeedsTheBareProjectUrl(string url, bool expected)
    {
        var options = new SupabaseOptions { Url = url, PublishableKey = "sb_publishable_TEST" };

        Assert.Equal(expected, options.IsConfigured);
    }

    [Fact]
    public async Task UrlWithAPath_SendsNothingAndSaysNotConfigured()
    {
        var handler = new RecordingHttpMessageHandler();
        using var http = new SupabaseHttp(
            new SupabaseOptions { Url = "https://TEST_REF.supabase.co/rest/v1", PublishableKey = "sb_publishable_TEST" },
            new NullAppLogger(),
            handler);

        var exception = await Assert.ThrowsAsync<SupabaseException>(() =>
            http.SendAsync(HttpMethod.Post, "auth/v1/otp", new { email = "test@example.invalid" }, null, TestContext.Current.CancellationToken));

        Assert.Equal(SupabaseErrorCodes.NotConfigured, exception.Code);
        Assert.Empty(handler.Requests);
        Assert.Equal("SaveHarbor Cloud is not set up in this version of the app.", SupabaseErrorMapper.Describe(exception).Message);
    }
}
