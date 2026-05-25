using System.Globalization;
using System.IO;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class WindroseSaveDiscoveryService : IWindroseSaveDiscoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<WindroseWorld>> DiscoverWorldsAsync(CancellationToken cancellationToken = default)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profilesRoot = Path.Combine(localAppData, "R5", "Saved", "SaveProfiles");

        if (!Directory.Exists(profilesRoot))
        {
            return [];
        }

        var worldFolders = Directory.EnumerateDirectories(profilesRoot, "Worlds", SearchOption.AllDirectories)
            .SelectMany(path => Directory.EnumerateDirectories(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var worlds = new List<WindroseWorld>();
        foreach (var worldFolder in worldFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var world = await ReadWorldAsync(worldFolder, cancellationToken);
            if (world is not null)
            {
                worlds.Add(world);
            }
        }

        return worlds
            .OrderByDescending(world => world.LastModifiedAt)
            .ThenBy(world => world.WorldName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<WindroseWorld?> ReadWorldAsync(string worldPath, CancellationToken cancellationToken = default)
    {
        var descriptionPath = Path.Combine(worldPath, "WorldDescription.json");
        if (!Directory.Exists(worldPath) || !File.Exists(descriptionPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(descriptionPath);
        var document = await JsonSerializer.DeserializeAsync<WorldDescriptionDocument>(stream, JsonOptions, cancellationToken);
        var description = document?.WorldDescription;
        if (description is null)
        {
            return null;
        }

        var directory = new DirectoryInfo(worldPath);
        var files = directory.EnumerateFiles("*", SearchOption.AllDirectories).ToArray();
        var lastModified = files.Length > 0
            ? files.Max(file => file.LastWriteTimeUtc)
            : directory.LastWriteTimeUtc;

        var worldId = !string.IsNullOrWhiteSpace(description.IslandId)
            ? description.IslandId
            : directory.Name;

        return new WindroseWorld(
            worldId,
            string.IsNullOrWhiteSpace(description.WorldName) ? directory.Name : description.WorldName,
            string.IsNullOrWhiteSpace(description.WorldPresetType) ? "Unknown" : description.WorldPresetType,
            directory.FullName,
            ConvertUnrealTimestamp(description.CreationTime),
            new DateTimeOffset(lastModified, TimeSpan.Zero).ToLocalTime(),
            files.Sum(file => file.Length),
            files.Length);
    }

    private static DateTimeOffset ConvertUnrealTimestamp(double creationTime)
    {
        if (creationTime <= 0)
        {
            return DateTimeOffset.MinValue;
        }

        try
        {
            var ticks = Convert.ToInt64(creationTime, CultureInfo.InvariantCulture);
            return new DateTimeOffset(ticks, TimeSpan.Zero).ToLocalTime();
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }
}
