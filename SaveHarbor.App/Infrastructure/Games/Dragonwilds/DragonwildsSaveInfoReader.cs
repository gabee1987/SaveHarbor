using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace SaveHarbor.App.Infrastructure.Games.Dragonwilds;

public sealed record DragonwildsSaveInfo(
    string? WorldName,
    string? CreatedBy,
    int? Difficulty,
    bool? FriendlyFire,
    bool? Crossplay,
    bool? PasswordProtected,
    DateTimeOffset? SavedAtUtc,
    string? GameBuild,
    IReadOnlyDictionary<string, float> RuleScales);

// Reads the metadata table ("INFO" chunk, "CINF" block) at the start of a Dragonwilds world save.
// The layout was observed on a real save (owner's install, game build 247068); it is not documented by the game.
// Saves arrive from other players through the cloud, so every length and offset is bounds-checked, and only
// allow-listed keys are decoded. The world password and owner id are never read: for the password only the
// length prefix is inspected to tell whether one is set.
public static class DragonwildsSaveInfoReader
{
    public const int MaxHeaderBytes = 64 * 1024;
    private const int MaxStringLength = 512;
    private const int MaxKeys = 256;

    public static readonly IReadOnlyList<string> RuleKeys =
    [
        "Difficulty.Progression.XPGainScale",
        "Difficulty.Progression.CraftingCostScale",
        "Difficulty.Progression.BuildingMaterialCostScale",
        "Difficulty.Progression.ProcessingSpeedScale",
        "Difficulty.Player.InventoryCarryCapacityScale",
        "Difficulty.Player.TeleportationCostScale"
    ];

    public static async Task<DragonwildsSaveInfo?> TryReadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[(int)Math.Min(stream.Length, MaxHeaderBytes)];
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            return Parse(buffer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static DragonwildsSaveInfo? Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            return ParseCore(data);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static DragonwildsSaveInfo? ParseCore(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data[..4].SequenceEqual("SAVE"u8) || !data.Slice(8, 4).SequenceEqual("INFO"u8))
        {
            return null;
        }

        var info = Slice(data, 16, ReadInt(data, 12));
        var marker = info.IndexOf("CINF"u8);
        if (marker < 0)
        {
            return null;
        }

        var position = marker + 8;
        var keyCount = ReadInt(info, position);
        position += 4;
        if (keyCount > MaxKeys)
        {
            return null;
        }

        var keys = new string[keyCount];
        for (var index = 0; index < keyCount; index++)
        {
            keys[index] = ReadString(info, ref position) ?? string.Empty;
        }

        var valueCount = ReadInt(info, position);
        position += 4;
        if (valueCount != keyCount)
        {
            return null;
        }

        var offsets = new int[valueCount];
        for (var index = 0; index < valueCount; index++)
        {
            offsets[index] = ReadInt(info, position);
            position += 4;
        }

        var blob = Slice(info, position + 4, ReadInt(info, position)).ToArray();
        var values = new Dictionary<string, (int Start, int Length)>(StringComparer.Ordinal);
        for (var index = 0; index < keyCount; index++)
        {
            var end = index + 1 < valueCount ? offsets[index + 1] : blob.Length;
            if (offsets[index] < 0 || end < offsets[index] || end > blob.Length)
            {
                return null;
            }

            values[keys[index]] = (offsets[index], end - offsets[index]);
        }

        ReadOnlySpan<byte> Value(string key) =>
            values.TryGetValue(key, out var range) ? blob.AsSpan(range.Start, range.Length) : [];

        var rules = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var key in RuleKeys)
        {
            var raw = Value(key);
            if (raw.Length == 4)
            {
                var scale = BinaryPrimitives.ReadSingleLittleEndian(raw);
                if (float.IsFinite(scale) && scale is >= 0 and <= 100)
                {
                    rules[key] = scale;
                }
            }
        }

        return new DragonwildsSaveInfo(
            ReadStringValue(Value("WorldName")),
            ReadStringValue(Value("WorldNameOwner")),
            Value("SurvivalDifficulty").Length == 4 ? ReadInt(Value("SurvivalDifficulty"), 0) : null,
            ReadFlag(Value("FriendlyFire")),
            ReadFlag(Value("CrossplayEnabled")),
            Value("SessionPasswd").Length >= 4 ? ReadInt(Value("SessionPasswd"), 0) is not (0 or 1) : null,
            ReadTicks(Value("TimeOfSave")),
            ReadBuild(ReadStringValue(Value("LastSavedBy"))),
            rules);
    }

    private static string? ReadStringValue(ReadOnlySpan<byte> raw)
    {
        if (raw.IsEmpty)
        {
            return null;
        }

        var position = 0;
        return ReadString(raw, ref position);
    }

    // Length-prefixed string; the length includes the terminating null. Negative lengths mean UTF-16.
    private static string? ReadString(ReadOnlySpan<byte> data, ref int position)
    {
        var length = ReadInt(data, position);
        position += 4;
        if (length == 0)
        {
            return string.Empty;
        }

        var unicode = length < 0;
        var characters = Math.Abs(length);
        if (characters > MaxStringLength)
        {
            throw new FormatException("String too long.");
        }

        var byteCount = unicode ? characters * 2 : characters;
        var bytes = Slice(data, position, byteCount);
        position += byteCount;
        var text = unicode
            ? Encoding.Unicode.GetString(bytes[..^2])
            : Encoding.UTF8.GetString(bytes[..^1]);
        return new string(text.Where(character => !char.IsControl(character)).ToArray()).Trim();
    }

    private static bool? ReadFlag(ReadOnlySpan<byte> raw) => raw.Length is 1 or 4 ? raw.IndexOfAnyExcept((byte)0) >= 0 : null;

    private static DateTimeOffset? ReadTicks(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != 8)
        {
            return null;
        }

        var ticks = BinaryPrimitives.ReadInt64LittleEndian(raw);
        return ticks > 0 && ticks < DateTime.MaxValue.Ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    }

    // "++dominion+hotfix:247068, ++dominion+hotfix:245400, ..." -> "247068" (the newest build is listed first).
    private static string? ReadBuild(string? history)
    {
        var newest = history?.Split(',')[0];
        var separator = newest?.LastIndexOf(':') ?? -1;
        if (newest is null || separator < 0)
        {
            return null;
        }

        var build = new string(newest[(separator + 1)..].TakeWhile(char.IsAsciiDigit).ToArray());
        return build.Length > 0 ? build : null;
    }

    private static int ReadInt(ReadOnlySpan<byte> data, int position) =>
        BinaryPrimitives.ReadInt32LittleEndian(Slice(data, position, 4));

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int start, int length)
    {
        if (start < 0 || length < 0 || start > data.Length - length)
        {
            throw new FormatException("Out of range.");
        }

        return data.Slice(start, length);
    }
}
