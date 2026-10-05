using System.Globalization;
using System.IO;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure.Games.Dragonwilds;

// Builds the "All world info" view of one save file. Secrets stay unread (see DragonwildsSaveInfoReader).
public static class DragonwildsWorldInspector
{
    public const string GameBackupSuffix = ".backup";
    private const string Unconfirmed = "meaning not confirmed yet";

    public static async Task<IReadOnlyList<InspectionSection>> InspectAsync(string savePath, CancellationToken cancellationToken = default)
    {
        var info = await DragonwildsSaveInfoReader.TryReadAsync(savePath, cancellationToken);
        var sections = new List<InspectionSection>();

        if (info is not null)
        {
            var facts = DragonwildsWorldFacts.From(info);
            var world = new List<InspectionItem>();
            Add(world, "World name", info.WorldName);
            Add(world, "World ID", info.WorldGuid);
            Add(world, "Map", info.MapName);
            world.AddRange(facts.Where(fact => fact.Group == WorldFactGroup.Details).Select(fact => new InspectionItem(fact.Label, fact.Value)));
            Add(world, "Hardcore state", info.HardcoreState is { } hardcore ? $"{hardcore} ({Unconfirmed})" : null);
            Add(world, "Session privacy", info.SessionPrivacy is { } privacy ? $"{privacy} ({Unconfirmed})" : null);
            sections.Add(new InspectionSection("World", world));

            var rules = facts.Where(fact => fact.Group == WorldFactGroup.Rules).Select(fact => new InspectionItem(fact.Label, fact.Value)).ToArray();
            if (rules.Length > 0)
            {
                sections.Add(new InspectionSection("World rules", rules));
            }
        }

        sections.Add(new InspectionSection("Save file", await DescribeFileAsync(savePath, info, cancellationToken)));

        var gameBackup = savePath + GameBackupSuffix;
        if (File.Exists(gameBackup))
        {
            var backupInfo = await DragonwildsSaveInfoReader.TryReadAsync(gameBackup, cancellationToken);
            var file = new FileInfo(gameBackup);
            List<InspectionItem> items =
            [
                new("File", file.Name),
                new("Size", DisplayFormatter.FormatBytes(file.Length)),
                new("Modified", FormatLocal(file.LastWriteTimeUtc))
            ];
            Add(items, "Save revision", backupInfo?.SaveRevision?.ToString(CultureInfo.InvariantCulture));
            Add(items, "Last saved in game", backupInfo?.SavedAtUtc is { } saved ? FormatLocal(saved.UtcDateTime) : null);
            items.Add(new InspectionItem("Readable", backupInfo is null ? "No: it does not look like a valid world save" : "Yes"));
            sections.Add(new InspectionSection("Game's own backup copy", items));
        }

        if (info is { BuildHistory.Count: > 0 })
        {
            sections.Add(new InspectionSection(
                "Game builds that saved this world",
                info.BuildHistory.Select((build, index) => new InspectionItem(index == 0 ? "Newest" : $"Earlier {index}", build)).ToArray()));
        }

        if (info is { Fields.Count: > 0 })
        {
            sections.Add(new InspectionSection(
                "All metadata fields",
                info.Fields.Select(field => new InspectionItem(field.Key, field.Visibility switch
                {
                    DragonwildsFieldVisibility.Private => "Private: never read by SaveHarbor",
                    DragonwildsFieldVisibility.Shown => "Shown above",
                    _ => $"Not decoded ({field.SizeBytes} bytes)"
                })).ToArray()));
        }

        return sections;
    }

    private static async Task<IReadOnlyList<InspectionItem>> DescribeFileAsync(string savePath, DragonwildsSaveInfo? info, CancellationToken cancellationToken)
    {
        var file = new FileInfo(savePath);
        if (!file.Exists)
        {
            return [new InspectionItem("File", "Not found")];
        }

        var chunks = await DragonwildsSaveLayout.ReadChunksAsync(savePath, cancellationToken);
        List<InspectionItem> items =
        [
            new("File", file.Name),
            new("Folder", file.DirectoryName ?? string.Empty),
            new("Size", $"{DisplayFormatter.FormatBytes(file.Length)} ({file.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes)"),
            new("Modified", FormatLocal(file.LastWriteTimeUtc)),
            new("Created", FormatLocal(file.CreationTimeUtc))
        ];
        Add(items, "Format", info?.FormatVersion is { } version ? $"Dragonwilds save, format {version}" : "Not recognised as a Dragonwilds world save");
        Add(items, "Save revision", info?.SaveRevision?.ToString(CultureInfo.InvariantCulture));
        Add(items, "Sections", chunks.Count > 0
            ? string.Join(" · ", chunks.Select(chunk => $"{chunk.Tag} {DisplayFormatter.FormatBytes(chunk.SizeBytes)}"))
            : null);
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

    private static void Add(List<InspectionItem> items, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            items.Add(new InspectionItem(label, value));
        }
    }

    private static string FormatLocal(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
