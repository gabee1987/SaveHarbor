using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class LocalJsonSyncStateService : ILocalSyncStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string syncStateRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SaveHarbor",
        "sync-state");

    public async Task<LocalSyncState> LoadAsync(WindroseWorld world, CancellationToken cancellationToken = default)
    {
        var path = GetStatePath(world.WorldId);
        if (!File.Exists(path))
        {
            return LocalSyncState.CreateNew(world);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var state = await JsonSerializer.DeserializeAsync<LocalSyncState>(stream, JsonOptions, cancellationToken);
            if (state is null || !string.Equals(state.WorldId, world.WorldId, StringComparison.OrdinalIgnoreCase))
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

    public async Task SaveAsync(LocalSyncState state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(syncStateRoot);

        var path = GetStatePath(state.WorldId);
        var tempPath = $"{path}.tmp";

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

    private string GetStatePath(string worldId)
    {
        var safeWorldId = string.Join("_", worldId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(syncStateRoot, $"{safeWorldId}.json");
    }
}
