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

    // Everything SaveHarbor can tell about a world, for the "All world info" window. Same secrecy rule as above.
    Task<IReadOnlyList<InspectionSection>> InspectWorldAsync(GameWorld world, CancellationToken cancellationToken = default);

    // Open-file dialog filter for world saves picked from outside the save folder, e.g. "World save (*.sav)|*.sav".
    string ImportFileFilter { get; }

    // Validates a picked file as a world of this game. Returns null when it is not one. The file is untrusted.
    Task<ImportCandidate?> ReadImportCandidateAsync(string path, CancellationToken cancellationToken = default);

    // A copy the game itself keeps of this world (Dragonwilds writes "<world>.sav.backup"), when one exists.
    string? GetGameBackupCopyPath(GameWorld world);

    IReadOnlyList<string> GetPayloadFiles(GameWorld world);

    string GetExpectedWorldPath(GameSaveRoot root, string worldId);
}
