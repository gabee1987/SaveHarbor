using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Backup;

internal interface IPayloadStrategy
{
    IReadOnlyList<BackupFileEntry> Stage(GameWorld world, IGameSaveAdapter adapter, string payloadRoot, CancellationToken cancellationToken);

    void Restore(string payloadRoot, GameWorld target, BackupManifest manifest, CancellationToken cancellationToken);

    string Import(string payloadRoot, BackupManifest manifest, GameSaveRoot root, IGameSaveAdapter adapter, bool overwriteExisting, CancellationToken cancellationToken);
}
