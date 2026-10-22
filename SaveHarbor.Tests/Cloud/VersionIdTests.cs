using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;

namespace SaveHarbor.Tests.Cloud;

// The player part of a version id must be a valid name on every provider, including SaveHarbor Cloud.
public sealed class VersionIdTests
{
    [Theory]
    [InlineData("TEST_USER", "TEST_USER")]
    [InlineData("Test User", "Test_User")]
    [InlineData("Tést Üser", "T_st__ser")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("...", "player")]
    [InlineData("", "player")]
    public void PlayerPart_UsesOnlyCharactersEveryProviderAccepts(string playerName, string expected)
    {
        var part = CloudSyncService.VersionIdPart(playerName);

        Assert.Equal(expected, part);
        Assert.Matches(SupabaseMapping.VersionIdPattern(), $"20261020_120000_{part}_v1");
    }
}
