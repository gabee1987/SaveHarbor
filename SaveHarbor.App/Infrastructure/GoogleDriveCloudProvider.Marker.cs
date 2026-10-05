
using Google.Apis.Drive.v3;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class GoogleDriveCloudProvider
{
    private readonly HashSet<GameId> verifiedGames = [];

    private Task EnsureGameFolderAsync(DriveService service, GameId game, bool writeMarker, CancellationToken cancellationToken)
    {
        return EnsureGameFolderAsync(service, game, options.GetSharedFolderId(game), writeMarker, cancellationToken);
    }

    private async Task EnsureGameFolderAsync(
        DriveService service,
        GameId game,
        string rootId,
        bool writeMarker,
        CancellationToken cancellationToken)
    {
        if (writeMarker && verifiedGames.Contains(game))
        {
            return;
        }

        var folder = await GetAndValidateSharedRootFolderAsync(service, rootId, cancellationToken);
        logger.Information(AppLogKeyword.CloudProvider, "Using Google Drive shared folder {FolderName} for {Game}", folder.Name, game);

        var marker = await DownloadJsonByNameAsync<GameFolderMarker>(service, rootId, GameFolderMarker.FileName, cancellationToken);
        var hasWorlds = await FindFolderIdAsync(service, rootId, "worlds", cancellationToken) is not null;
        var isEmpty = marker is null && !hasWorlds && !await HasAnyChildAsync(service, rootId, cancellationToken);

        switch (GameFolderMarkerPolicy.Evaluate(game, marker, hasWorlds, isEmpty))
        {
            case MarkerDecision.Reject:
                throw new InvalidOperationException(GameFolderMarkerPolicy.BuildRejectMessage(game, marker, hasWorlds));
            case MarkerDecision.AcceptAndWriteMarker when writeMarker:
                var newMarker = new GameFolderMarker
                {
                    Game = game.ToStorageKey(),
                    CreatedBy = Environment.UserName,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                await UploadJsonByNameAsync(service, rootId, GameFolderMarker.FileName, newMarker, cancellationToken);
                break;
        }

        if (writeMarker)
        {
            verifiedGames.Add(game);
        }
    }

    private static bool BelongsToGame(CloudWorldManifest manifest, GameId game)
    {
        return string.IsNullOrWhiteSpace(manifest.Game)
            || string.Equals(manifest.Game, game.ToStorageKey(), StringComparison.OrdinalIgnoreCase);
    }
}
