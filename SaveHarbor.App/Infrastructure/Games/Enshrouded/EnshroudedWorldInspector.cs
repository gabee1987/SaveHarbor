using System.Globalization;
using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// The facts and the "All world info" view of one world file. Everything comes from the file header, the uncompressed
// blocks after it (world name, world settings) and the file system; the compressed world data is never read.
public static class EnshroudedWorldInspector
{
    public static IReadOnlyList<WorldFact> Facts(GameWorld world, string location)
    {
        var header = EnshroudedSaveHeader.TryRead(world.SavePath);
        EnshroudedSlotMap.TryGetSlot(world.SavePath, out var slot);
        List<WorldFact> facts =
        [
            new(WorldFactGroup.Details, "Location", location),
            new(WorldFactGroup.Details, "Slot", EnshroudedSlotMap.DisplaySlot(slot))
        ];
        if (header is not null)
        {
            facts.Add(new WorldFact(WorldFactGroup.Details, "Save counter", header.SaveCounter.ToString(CultureInfo.InvariantCulture)));
            facts.Add(new WorldFact(WorldFactGroup.Details, "Last saved", header.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        }

        facts.Add(new WorldFact(WorldFactGroup.Details, "Game copies kept", EnshroudedSlotMap.GameCopies(world.SavePath).Count.ToString(CultureInfo.InvariantCulture)));

        var settings = EnshroudedWorldSettingsReader.TryRead(world.SavePath);
        if (settings is not null)
        {
            facts.AddRange(EnshroudedWorldSettingsReader.SummaryLabels
                .Select(label => settings.First(setting => setting.Label == label))
                .Select(setting => new WorldFact(WorldFactGroup.Rules, setting.Label, setting.Value)));
        }

        return facts;
    }

    public static async Task<IReadOnlyList<InspectionSection>> InspectAsync(GameWorld world, string location, CancellationToken cancellationToken = default)
    {
        var header = EnshroudedSaveHeader.TryRead(world.SavePath);
        List<InspectionItem> worldItems =
        [
            new("World name", EnshroudedWorldNameReader.TryRead(world.SavePath) ?? "Not stored in a readable place"),
            new("World ID", header?.WorldId ?? "Not readable")
        ];
        worldItems.AddRange(Facts(world, location).Where(fact => fact.Group == WorldFactGroup.Details).Select(fact => new InspectionItem(fact.Label, fact.Value)));

        // Worlds carried over from Early Access keep their settings in the compressed data, which is never read.
        var settings = EnshroudedWorldSettingsReader.TryRead(world.SavePath);
        if (settings is null)
        {
            worldItems.Add(new InspectionItem("World settings", "Not stored in a readable place"));
        }

        List<InspectionSection> sections = [new("World", worldItems)];
        if (settings is not null)
        {
            sections.Add(new InspectionSection("World settings", settings.Select(setting => new InspectionItem(setting.Label, setting.Value)).ToArray()));
        }

        sections.Add(new InspectionSection("Save file", await DescribeFileAsync(world.SavePath, header, cancellationToken)));

        var copies = EnshroudedSlotMap.GameCopies(world.SavePath);
        if (copies.Count > 0)
        {
            sections.Add(new InspectionSection("Game's own rolling copies (newest first)", copies.Select(copy =>
            {
                var copyHeader = EnshroudedSaveHeader.TryRead(copy);
                var value = copyHeader is null
                    ? "Not readable as a world save"
                    : string.Create(CultureInfo.InvariantCulture, $"Save {copyHeader.SaveCounter} · {copyHeader.SavedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}")
                        + (copyHeader.WorldId == header?.WorldId ? string.Empty : " · a different world");
                return new InspectionItem(Path.GetFileName(copy), value);
            }).ToArray()));
        }

        return sections;
    }

    private static async Task<IReadOnlyList<InspectionItem>> DescribeFileAsync(string savePath, EnshroudedSaveHeader? header, CancellationToken cancellationToken)
    {
        var file = new FileInfo(savePath);
        if (!file.Exists)
        {
            return [new InspectionItem("File", "Not found")];
        }

        List<InspectionItem> items =
        [
            new("File", file.Name),
            new("Folder", file.DirectoryName ?? string.Empty),
            new("Size", $"{DisplayFormatter.FormatBytes(file.Length)} ({file.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes)"),
            new("Modified", file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            new("Created", file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            new("Format", header is null ? "Not recognised as an Enshrouded world save" : $"Enshrouded save, format {header.FormatVersion}")
        ];
        if (header is not null)
        {
            items.Add(new InspectionItem("Saved by the game", header.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        }

        try
        {
            items.Add(new InspectionItem("SHA-256", await FileHashCalculator.ComputeSha256Async(savePath, cancellationToken)));
        }
        catch (IOException)
        {
            items.Add(new InspectionItem("SHA-256", "Not available while the game is writing the file"));
        }

        return items;
    }
}
