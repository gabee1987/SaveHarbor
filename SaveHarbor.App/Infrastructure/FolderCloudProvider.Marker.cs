using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class FolderCloudProvider
{
    private void EnsureGameFolder(GameId game)
    {
        var rootPath = GetRootPath(game);
        Directory.CreateDirectory(rootPath);

        var markerPath = Path.Combine(rootPath, GameFolderMarker.FileName);
        var marker = ReadMarker(markerPath);
        var hasWorlds = Directory.Exists(Path.Combine(rootPath, "worlds"));
        var isEmpty = !Directory.EnumerateFileSystemEntries(rootPath).Any();

        switch (GameFolderMarkerPolicy.Evaluate(game, marker, hasWorlds, isEmpty))
        {
            case MarkerDecision.Reject:
                throw new InvalidOperationException(GameFolderMarkerPolicy.BuildRejectMessage(game, marker, hasWorlds));
            case MarkerDecision.AcceptAndWriteMarker:
                var newMarker = new GameFolderMarker
                {
                    Game = game.ToStorageKey(),
                    CreatedBy = Environment.UserName,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                File.WriteAllText(markerPath, JsonSerializer.Serialize(newMarker, JsonOptions));
                break;
        }
    }

    private static GameFolderMarker? ReadMarker(string markerPath)
    {
        if (!File.Exists(markerPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GameFolderMarker>(File.ReadAllText(markerPath), JsonOptions);
        }
        catch (JsonException)
        {
            return new GameFolderMarker();
        }
    }
}
