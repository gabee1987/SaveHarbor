using System.Net;
using System.Net.Http;
using SaveHarbor.App.Domain;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// The cloud names the oldest app version it still serves; an older app is told to update instead of syncing.
public sealed class MinimumVersionGateTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<SupabaseHarness> SignedInAsync(Version appVersion)
    {
        var harness = new SupabaseHarness(appVersion: appVersion);
        await harness.SignInWithGroupAsync();
        return harness;
    }

    [Fact]
    public async Task OlderApp_IsToldToUpdate()
    {
        using var harness = await SignedInAsync(new Version(2, 2, 2));

        var status = await harness.Provider.GetConnectionStatusAsync(GameId.Windrose, Token);

        Assert.False(status.IsConnected);
        Assert.Equal(CloudConnectionReason.UpdateRequired, status.Reason);
        Assert.Contains("2.3.0", status.Message);
    }

    [Theory]
    [InlineData(2, 3, 0, 0)]
    [InlineData(2, 4, 0, 0)]
    [InlineData(3, 0, 1, 7)]
    public async Task SameOrNewerApp_IsConnected(int major, int minor, int build, int revision)
    {
        using var harness = await SignedInAsync(new Version(major, minor, build, revision));

        Assert.True((await harness.Provider.GetConnectionStatusAsync(GameId.Windrose, Token)).IsConnected);
    }

    [Fact]
    public async Task MinimumVersion_IsReadOncePerHour()
    {
        using var harness = await SignedInAsync(new Version(2, 3, 0));

        await harness.Provider.GetConnectionStatusAsync(GameId.Windrose, Token);
        harness.Clock.Advance(TimeSpan.FromMinutes(59));
        await harness.Provider.GetConnectionStatusAsync(GameId.Windrose, Token);
        Assert.Single(harness.Handler.To("rest/v1/app_meta"));

        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        await harness.Provider.GetConnectionStatusAsync(GameId.Windrose, Token);
        Assert.Equal(2, harness.Handler.To("rest/v1/app_meta").Count);
    }

    [Fact]
    public async Task MinimumVersion_IsReadWithoutSignInToken()
    {
        using var harness = await SignedInAsync(new Version(2, 3, 0));

        await harness.Provider.GetConnectionStatusAsync(GameId.Windrose, Token);

        Assert.Null(harness.Handler.To("rest/v1/app_meta").Single().Bearer);
    }

    [Fact]
    public async Task MissingOrUnreadableMinimum_DoesNotBlock()
    {
        using var harness = new SupabaseHarness(appVersion: new Version(1, 0, 0));
        harness.SignIn();
        await harness.Settings.SetActiveGroupsAsync([GameId.Windrose], SupabaseHarness.GroupId, Token);
        harness.Handler.On(HttpMethod.Get, "rest/v1/app_meta", HttpStatusCode.OK, """[{"key":"min_supported_app_version","value":"not a version"}]""");

        Assert.True((await harness.Provider.GetConnectionStatusAsync(GameId.Windrose, Token)).IsConnected);
    }
}
