using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public enum MarkerDecision
{
    Accept,
    AcceptAndWriteMarker,
    Reject
}

public static class GameFolderMarkerPolicy
{
    public static MarkerDecision Evaluate(
        GameId requested,
        GameFolderMarker? marker,
        bool folderHasWorldsSubfolder,
        bool folderIsEmpty)
    {
        if (marker is not null)
        {
            return GameIdExtensions.TryParseStorageKey(marker.Game, out var markerGame) && markerGame == requested
                ? MarkerDecision.Accept
                : MarkerDecision.Reject;
        }

        if (folderIsEmpty)
        {
            return MarkerDecision.AcceptAndWriteMarker;
        }

        // Folders created before multi-game support hold Windrose worlds and carry no marker.
        return folderHasWorldsSubfolder && requested == GameId.Windrose
            ? MarkerDecision.AcceptAndWriteMarker
            : MarkerDecision.Reject;
    }

    public static string BuildRejectMessage(GameId requested, GameFolderMarker? marker, bool folderHasWorldsSubfolder)
    {
        if (marker is not null)
        {
            return $"This folder is used for {DescribeGame(marker.Game)}. Choose a separate folder for {requested}.";
        }

        return folderHasWorldsSubfolder
            ? $"This folder is used for {GameId.Windrose}. Choose a separate folder for {requested}."
            : "This folder already contains other files. Use an empty folder.";
    }

    private static string DescribeGame(string storageKey)
    {
        return GameIdExtensions.TryParseStorageKey(storageKey, out var game) ? game.ToString() : "another game";
    }
}
