using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// Discovery and the world<N>.ksd payload arrive in the next step of the Enshrouded plan (docs/plans/enshrouded/06-tasks.md
// T2). Until then the game is selectable but lists no worlds, and every write refuses.
public sealed class EnshroudedSaveAdapter : IGameSaveAdapter
{
    public WorldPayloadKind PayloadKind => WorldPayloadKind.FileSet;

    public string SaveRootPath => string.Empty;

    public Task<IReadOnlyList<GameSaveRoot>> DiscoverSaveRootsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameSaveRoot>>([]);

    public Task<IReadOnlyList<GameWorld>> DiscoverWorldsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameWorld>>([]);

    public Task<GameWorld?> ReadWorldAsync(string savePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<GameWorld?>(null);

    public Task<IReadOnlyList<WorldFact>> ReadWorldFactsAsync(GameWorld world, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorldFact>>([]);

    public Task<IReadOnlyList<InspectionSection>> InspectWorldAsync(GameWorld world, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InspectionSection>>([]);

    public string ImportFileFilter => "Enshrouded world (world*.ksd)|*.ksd";

    public Task<ImportCandidate?> ReadImportCandidateAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<ImportCandidate?>(null);

    public string? GetGameBackupCopyPath(GameWorld world) => null;

    public IReadOnlyList<string> GetPayloadFiles(GameWorld world) => [];

    public string GetExpectedWorldPath(GameSaveRoot root, string worldId) =>
        throw new InvalidOperationException("Enshrouded worlds cannot be placed yet.");

    public string? GetSaveFormatVersion(string worldPath) => null;

    public void ValidatePayload(string payloadRoot, string expectedWorldId) =>
        throw new InvalidDataException("Enshrouded worlds cannot be placed yet.");

    public void StageGameFiles(GameWorld world, string payloadRoot)
    {
    }

    public void PlaceGameFiles(string gameFilesRoot, string worldPath)
    {
    }

    public string? MoveGameFilesAside(GameWorld world) => null;

    public void RememberWorldState(string worldPath)
    {
    }

    public IReadOnlyList<SaveHealthNotice>? CheckHealth(GameWorld world) => null;

    public IReadOnlyList<string> MoveLeftoversAside(GameWorld world) => [];
}
