using SaveHarbor.App.Domain;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.Tests.Games;

public sealed class ImportComparisonTests
{
    private static ImportCandidate Candidate(string? fingerprint, long? revision) => new(
        new GameWorld(GameId.Dragonwilds, "TEST_WORLD", "TEST_WORLD", string.Empty, "TEST_WORLD.sav", DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 1),
        fingerprint,
        revision,
        null,
        []);

    [Fact]
    public void Describe_NoLocalWorld_AddsWithoutWarning()
    {
        var verdict = ImportComparison.Describe(Candidate("TEST_ID", 5), null, localExists: false);

        Assert.False(verdict.IsWarning);
    }

    [Fact]
    public void Describe_DifferentWorldWithSameName_Warns()
    {
        var verdict = ImportComparison.Describe(Candidate("TEST_ID_A", 5), Candidate("TEST_ID_B", 5), localExists: true);

        Assert.True(verdict.IsWarning);
        Assert.Contains("DIFFERENT world", verdict.Message);
    }

    [Fact]
    public void Describe_OlderCopyOfSameWorld_Warns()
    {
        var verdict = ImportComparison.Describe(Candidate("TEST_ID", 4), Candidate("TEST_ID", 9), localExists: true);

        Assert.True(verdict.IsWarning);
        Assert.Contains("OLDER", verdict.Message);
    }

    [Fact]
    public void Describe_NewerCopyOfSameWorld_DoesNotWarn()
    {
        var verdict = ImportComparison.Describe(Candidate("TEST_ID", 12), Candidate("TEST_ID", 9), localExists: true);

        Assert.False(verdict.IsWarning);
        Assert.Contains("NEWER", verdict.Message);
    }
}
