using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// Enshrouded lists every "world<N>.ksd" in a save folder; N is only a slot. A world is identified by the GUID in its
// header, so a world SaveHarbor places goes back into the slot that already holds it, or else the first free one.
// The game shows two worlds when one GUID sits in two slots, which this mapping prevents.
public static partial class EnshroudedSlotMap
{
    public const int MaxSlot = 9999;

    public static IEnumerable<string> EnumerateWorldFiles(string worldsPath)
    {
        if (!Directory.Exists(worldsPath))
        {
            return [];
        }

        // "*.ksd" is not used as a search pattern: a three-character extension pattern also matches longer extensions.
        return Directory.EnumerateFiles(worldsPath, "*", SearchOption.TopDirectoryOnly).Where(path => TryGetSlot(path, out _));
    }

    // The game's menu numbers slots from 1: "world0.ksd" is slot 1.
    public static string DisplaySlot(int slot) => (slot + 1).ToString(CultureInfo.InvariantCulture);

    public static bool TryGetSlot(string path, out int slot)
    {
        slot = -1;
        var match = WorldFileName().Match(Path.GetFileName(path));
        return match.Success
            && int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot <= MaxSlot;
    }

    // A world file or one of the game's rolling copies of it ("world<N>.ksd.backup-<K>"), for picking a file to import.
    public static bool TryGetSlotOfWorldOrCopy(string path, out int slot)
    {
        var name = Path.GetFileName(path);
        var copy = GameCopySuffix().Match(name);
        return TryGetSlot(copy.Success ? name[..copy.Index] : name, out slot);
    }

    // The game's rolling copies of a world in the "backup" folder next to it, newest (lowest K) first.
    public static IReadOnlyList<string> GameCopies(string worldPath)
    {
        var folder = Path.Combine(Path.GetDirectoryName(worldPath) ?? string.Empty, "backup");
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var worldName = Path.GetFileName(worldPath);
        return Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Select(copy => (File: copy, Name: Path.GetFileName(copy), Suffix: GameCopySuffix().Match(Path.GetFileName(copy))))
            .Where(copy => copy.Suffix.Success && string.Equals(copy.Name[..copy.Suffix.Index], worldName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(copy => int.Parse(copy.Suffix.Groups[1].ValueSpan, CultureInfo.InvariantCulture))
            .Select(copy => copy.File)
            .ToArray();
    }

    public static string FileName(int slot) => $"world{slot.ToString(CultureInfo.InvariantCulture)}.ksd";

    // The file that already holds the world, else the first free slot. A file whose header cannot be read is never
    // chosen, and its slot is never treated as free, so an unreadable save is not overwritten.
    public static string ResolveTargetPath(string worldsPath, string worldId)
    {
        var usedSlots = new HashSet<int>();
        foreach (var file in EnumerateWorldFiles(worldsPath).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (TryGetSlot(file, out var slot))
            {
                usedSlots.Add(slot);
            }

            if (string.Equals(EnshroudedSaveHeader.TryRead(file)?.WorldId, worldId, StringComparison.Ordinal))
            {
                return file;
            }
        }

        var freeSlot = Enumerable.Range(0, MaxSlot + 1).FirstOrDefault(slot => !usedSlots.Contains(slot), -1);
        return freeSlot < 0
            ? throw new IOException("The Enshrouded save folder has no free world slot.")
            : Path.Combine(worldsPath, FileName(freeSlot));
    }

    [GeneratedRegex(@"^world([0-9]{1,9})\.ksd$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WorldFileName();

    [GeneratedRegex(@"\.backup-([0-9]{1,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GameCopySuffix();
}
