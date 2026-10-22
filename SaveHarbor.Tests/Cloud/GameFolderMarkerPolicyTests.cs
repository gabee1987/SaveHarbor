using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.Tests.Cloud;

public sealed class GameFolderMarkerPolicyTests
{
    private static GameFolderMarker MarkerFor(GameId game) => new() { Game = game.ToStorageKey() };

    [Fact]
    public void Evaluate_MarkerForRequestedGame_Accepts()
    {
        var decision = GameFolderMarkerPolicy.Evaluate(GameId.Dragonwilds, MarkerFor(GameId.Dragonwilds), false, false);

        Assert.Equal(MarkerDecision.Accept, decision);
    }

    [Fact]
    public void Evaluate_MarkerForOtherGame_Rejects()
    {
        var decision = GameFolderMarkerPolicy.Evaluate(GameId.Dragonwilds, MarkerFor(GameId.Windrose), true, false);

        Assert.Equal(MarkerDecision.Reject, decision);
    }

    [Fact]
    public void Evaluate_NoMarkerEmptyFolder_AcceptsAndWritesMarker()
    {
        var decision = GameFolderMarkerPolicy.Evaluate(GameId.Dragonwilds, null, false, true);

        Assert.Equal(MarkerDecision.AcceptAndWriteMarker, decision);
    }

    [Fact]
    public void Evaluate_NoMarkerLegacyWorldsFolderForWindrose_AcceptsAndWritesMarker()
    {
        var decision = GameFolderMarkerPolicy.Evaluate(GameId.Windrose, null, true, false);

        Assert.Equal(MarkerDecision.AcceptAndWriteMarker, decision);
    }

    [Fact]
    public void Evaluate_NoMarkerLegacyWorldsFolderForDragonwilds_Rejects()
    {
        var decision = GameFolderMarkerPolicy.Evaluate(GameId.Dragonwilds, null, true, false);

        Assert.Equal(MarkerDecision.Reject, decision);
    }

    [Fact]
    public void Evaluate_NoMarkerFolderWithOtherFiles_Rejects()
    {
        var decision = GameFolderMarkerPolicy.Evaluate(GameId.Windrose, null, false, false);

        Assert.Equal(MarkerDecision.Reject, decision);
    }

    [Fact]
    public void BuildRejectMessage_OtherGameMarker_NamesBothGames()
    {
        var message = GameFolderMarkerPolicy.BuildRejectMessage(GameId.Dragonwilds, MarkerFor(GameId.Windrose), true);

        Assert.Equal("This folder is used for Windrose. Choose a separate folder for Dragonwilds.", message);
    }
}
