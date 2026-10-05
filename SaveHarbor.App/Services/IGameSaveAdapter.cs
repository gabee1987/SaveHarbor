using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IGameSaveAdapter
{
    WorldPayloadKind PayloadKind { get; }

    string SaveRootPath { get; }

    Task<IReadOnlyList<GameSaveRoot>> DiscoverSaveRootsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameWorld>> DiscoverWorldsAsync(CancellationToken cancellationToken = default);

    Task<GameWorld?> ReadWorldAsync(string savePath, CancellationToken cancellationToken = default);

    // Read-only facts about a world for display. Must never expose secrets stored in the save.
    Task<IReadOnlyList<WorldFact>> ReadWorldFactsAsync(GameWorld world, CancellationToken cancellationToken = default);

    IReadOnlyList<string> GetPayloadFiles(GameWorld world);

    string GetExpectedWorldPath(GameSaveRoot root, string worldId);
}
