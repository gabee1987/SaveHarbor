using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.Tests.Options;

// Any web page can start a saveharbor:// link, so only a well-formed join link with a valid invite code is accepted.
public sealed class InviteLinkTests
{
    [Theory]
    [InlineData("saveharbor://join/TESTCODE22")]
    [InlineData("saveharbor://join/testcode22")]
    [InlineData("saveharbor://join/TESTCODE22/")]
    [InlineData("SAVEHARBOR://JOIN/TESTCODE22")]
    public void TryParse_JoinLink_ReturnsTheCode(string link)
    {
        Assert.True(InviteLink.TryParse(link, out var code));
        Assert.Equal("TESTCODE22", code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TESTCODE22")]
    [InlineData("https://join/TESTCODE22")]
    [InlineData("saveharbor://leave/TESTCODE22")]
    [InlineData("saveharbor://join/TESTCODE2")]
    [InlineData("saveharbor://join/TESTCODE222")]
    [InlineData("saveharbor://join/TEST-CODE22")]
    [InlineData("saveharbor://join/TESTCODE22?next=x")]
    [InlineData("saveharbor://join/TESTCODE22/extra")]
    public void TryParse_AnythingElse_IsRejected(string link)
    {
        Assert.False(InviteLink.TryParse(link, out var code));
        Assert.Empty(code);
    }

    [Fact]
    public void Format_RoundTrips()
    {
        Assert.True(InviteLink.TryParse(InviteLink.Format("test code-22"), out var code));
        Assert.Equal("TESTCODE22", code);
    }

    [Fact]
    public void Find_TakesTheLinkAmongOtherArguments()
    {
        Assert.Equal("TESTCODE22", InviteLink.Find(["--game", "windrose", "saveharbor://join/TESTCODE22"]));
        Assert.Null(InviteLink.Find(["--game", "windrose"]));
    }

    [Theory]
    [InlineData("supabase", "Supabase")]
    [InlineData("GoogleDrive", "GoogleDrive")]
    public void ReadProviderArgument_KnownNames(string value, string kind)
    {
        Assert.Equal(kind, AppOptionsLoader.ReadProviderArgument(["--provider", value]));
    }

    [Fact]
    public void ReadProviderArgument_UnknownOrMissing_IsNull()
    {
        Assert.Null(AppOptionsLoader.ReadProviderArgument(["--provider", "localtest"]));
        Assert.Null(AppOptionsLoader.ReadProviderArgument([]));
    }
}
