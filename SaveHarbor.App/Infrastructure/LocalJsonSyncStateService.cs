using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class LocalJsonSyncStateService(IAppDataPathProvider pathProvider) : ILocalSyncStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim fileLock = new(1, 1);

    public async Task<LocalSyncState> LoadAsync(GameWorld world, CancellationToken cancellationToken = default)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var path = GetStatePath(world.Game, world.WorldId);
            if (!File.Exists(path))
            {
                return LocalSyncState.CreateNew(world);
            }

            try
            {
                await using var stream = File.OpenRead(path);
                var state = await JsonSerializer.DeserializeAsync<LocalSyncState>(stream, JsonOptions, cancellationToken);
                if (state is null
                    || !string.Equals(state.WorldId, world.WorldId, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(state.Game) && !string.Equals(state.Game, world.Game.ToStorageKey(), StringComparison.OrdinalIgnoreCase)))
                {
                    return LocalSyncState.CreateNew(world);
                }

                state.WorldName = world.WorldName;
                state.LocalWorldPath = world.SavePath;
                return state;
            }
            catch (JsonException)
            {
                return LocalSyncState.CreateNew(world);
            }
            catch (IOException)
            {
                return LocalSyncState.CreateNew(world);
            }
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task SaveAsync(LocalSyncState state, CancellationToken cancellationToken = default)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var stateGame = GameIdExtensions.TryParseStorageKey(state.Game, out var parsedGame) ? parsedGame : GameId.Windrose;
            Directory.CreateDirectory(pathProvider.GetSyncStateRoot(stateGame));

            var path = GetStatePath(stateGame, state.WorldId);
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(tempPath, path);
        }
        finally
        {
            fileLock.Release();
        }
    }

    private string GetStatePath(GameId game, string worldId)
    {
        var safeWorldId = string.Join("_", worldId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(pathProvider.GetSyncStateRoot(game), $"{safeWorldId}.json");
    }
}
