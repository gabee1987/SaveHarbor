using System.Net;
using System.Net.Http;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// Invite codes, the group chosen per game, and what sign-in and sign-out leave behind.
public sealed class SupabaseGroupServiceTests : IDisposable
{
    private const string OtherGroupId = "12111111-1111-4111-8111-111111111111";

    private readonly SupabaseHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string GroupJson(string id, string name, string role) =>
        $$"""{"id":"{{id}}","name":"{{name}}","created_at":"2026-10-20T12:00:00+00:00","member_count":2,"my_role":"{{role}}"}""";

    [Theory]
    [InlineData("test code-22", "TESTCODE22")]
    [InlineData(" TESTCODE22 ", "TESTCODE22")]
    [InlineData("tes-tco-de22", "TESTCODE22")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void InviteCode_IsNormalised(string? input, string expected)
    {
        Assert.Equal(expected, SupabaseGroupService.NormalizeInviteCode(input));
    }

    [Fact]
    public async Task Join_WithAShortCode_SendsNothing()
    {
        harness.SignIn();

        var exception = await Assert.ThrowsAsync<SupabaseException>(() => harness.Groups.JoinGroupAsync("TEST", "TEST_USER", Token));

        Assert.Equal(SupabaseErrorCodes.InviteInvalid, exception.Code);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task Join_SendsTheNormalisedCodeAndRemembersTheGroup()
    {
        harness.SignIn();
        harness.Handler.On(HttpMethod.Post, "rest/v1/rpc/join_group", HttpStatusCode.OK, GroupJson(SupabaseHarness.GroupId, "TEST_GROUP", "member"));

        var group = await harness.Groups.JoinGroupAsync("test code-22", "TEST_USER", Token);

        Assert.Equal("TEST_GROUP", group.Name);
        Assert.Contains("\"p_invite_code\":\"TESTCODE22\"", harness.Handler.To("rest/v1/rpc/join_group").Single().Body);
        Assert.Equal(SupabaseHarness.GroupId, Assert.Single(harness.Settings.GetKnownGroups()).Id);
    }

    [Fact]
    public async Task SetActiveGroup_ForOneGame_LeavesTheOtherAlone()
    {
        await harness.Groups.SetActiveGroupAsync(GameId.Windrose, SupabaseHarness.GroupId, applyToAllGames: false, Token);

        Assert.Equal(SupabaseHarness.GroupId, harness.Groups.GetActiveGroupId(GameId.Windrose));
        Assert.False(harness.Groups.HasGroupConfigured(GameId.Dragonwilds));
    }

    [Fact]
    public async Task SetActiveGroup_ForAllGames_SetsEveryGame()
    {
        await harness.Groups.SetActiveGroupAsync(GameId.Windrose, SupabaseHarness.GroupId, applyToAllGames: true, Token);

        Assert.All(Enum.GetValues<GameId>(), game => Assert.Equal(SupabaseHarness.GroupId, harness.Groups.GetActiveGroupId(game)));
    }

    [Fact]
    public async Task SetActiveGroup_RejectsAnythingButAUuid()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Groups.SetActiveGroupAsync(GameId.Windrose, "TEST_GROUP", false, Token));
    }

    [Fact]
    public async Task ListGroups_DropsActiveGroupsTheAccountLeft()
    {
        harness.SignIn();
        await harness.Groups.SetActiveGroupAsync(GameId.Windrose, SupabaseHarness.GroupId, false, Token);
        await harness.Groups.SetActiveGroupAsync(GameId.Dragonwilds, OtherGroupId, false, Token);
        harness.Handler.On(HttpMethod.Get, "rest/v1/group_summaries", HttpStatusCode.OK, $"[{GroupJson(OtherGroupId, "TEST_GROUP_2", "owner")}]");

        var groups = await harness.Groups.ListGroupsAsync(Token);

        Assert.True(Assert.Single(groups).IsOwner);
        Assert.Null(harness.Groups.GetActiveGroupId(GameId.Windrose));
        Assert.Equal(OtherGroupId, harness.Groups.GetActiveGroupId(GameId.Dragonwilds));
    }

    [Fact]
    public async Task KnownGroups_SurviveARestart()
    {
        harness.SignIn();
        harness.Handler.On(HttpMethod.Get, "rest/v1/group_summaries", HttpStatusCode.OK, $"[{GroupJson(SupabaseHarness.GroupId, "TEST_GROUP", "member")}]");
        await harness.Groups.ListGroupsAsync(Token);

        var restarted = new SupabaseGroupService(harness.Auth, harness.Rest, harness.Settings, new NullAppLogger());

        Assert.Equal("TEST_GROUP", Assert.Single(restarted.KnownGroups).Name);
    }

    [Fact]
    public async Task SignOut_ForgetsGroups()
    {
        harness.SignIn();
        await harness.Groups.SetActiveGroupAsync(GameId.Windrose, SupabaseHarness.GroupId, true, Token);
        harness.Handler.On(HttpMethod.Post, "auth/v1/logout", HttpStatusCode.NoContent);

        await harness.Groups.SignOutAsync(Token);

        Assert.False(harness.Groups.IsSignedIn);
        Assert.False(harness.Groups.HasGroupConfigured(GameId.Windrose));
        Assert.Empty(harness.Groups.KnownGroups);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("two@@example.invalid")]
    [InlineData("")]
    public async Task RequestCode_InvalidAddress_SendsNothing(string email)
    {
        var result = await harness.Groups.RequestSignInCodeAsync(email, Token);

        Assert.False(result.IsSuccess);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task RequestCode_RateLimited_SaysSo()
    {
        harness.Handler.On(HttpMethod.Post, "auth/v1/otp", HttpStatusCode.TooManyRequests, """{"code":429,"error_code":"over_email_send_rate_limit","msg":"email rate limit exceeded"}""");

        var result = await harness.Groups.RequestSignInCodeAsync("test@example.invalid", Token);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("Too many attempts.", result.Message);
    }

    [Fact]
    public async Task VerifyCode_WrongCode_AsksForANewOne()
    {
        harness.Handler.On(HttpMethod.Post, "auth/v1/verify", HttpStatusCode.Forbidden, """{"code":403,"error_code":"otp_expired","msg":"Token has expired or is invalid"}""");

        var result = await harness.Groups.VerifySignInCodeAsync("test@example.invalid", "123 456", Token);

        Assert.False(result.IsSuccess);
        Assert.True(result.NeedsCode);
        Assert.Equal("This code is wrong or has expired. Request a new code.", result.Message);
        Assert.Contains("\"token\":\"123456\"", harness.Handler.To("auth/v1/verify").Single().Body);
    }

    [Fact]
    public async Task VerifyCode_NotSixDigits_SendsNothing()
    {
        var result = await harness.Groups.VerifySignInCodeAsync("test@example.invalid", "12345", Token);

        Assert.False(result.IsSuccess);
        Assert.Empty(harness.Handler.Requests);
    }
}
