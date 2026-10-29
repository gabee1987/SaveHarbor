using System.Buffers.Binary;
using System.Globalization;
using System.IO;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

public sealed record EnshroudedWorldSetting(string Label, string Value);

// The world's game settings (the sliders and switches of the game's world settings screen) sit uncompressed in the
// second "S2BH" block, directly after the name block: block type 12, then 127 bytes of fields in the order of the
// dedicated server's documented "gameSettings". Observed on 1.0 worlds created or edited in 1.0; worlds carried over
// from Early Access do not have the block. The layout is not documented by the game, so the whole block must frame
// exactly (type, length, the next block's magic) and every value must be in a plausible range, or nothing is shown.
public static class EnshroudedWorldSettingsReader
{
    private const int NameBlockTailBytes = 18;
    private const int BlockHeaderBytes = 14;
    private const int FieldBytes = 127;
    private const int BlockBytes = BlockHeaderBytes + FieldBytes + 1;
    private const ushort SettingsBlockType = 12;
    private const ushort SettingsBlockLayout = 3;
    private const int ReadBytes = EnshroudedSaveHeader.Size + 75 + EnshroudedWorldNameReader.MaxNameBytes + NameBlockTailBytes + BlockBytes + 4;
    private const double MaxFactor = 100;
    private static readonly long MinDurationNs = TimeSpan.FromSeconds(10).Ticks * 100;
    private static readonly long MaxDurationNs = TimeSpan.FromDays(1).Ticks * 100;
    private static readonly byte[] BlockMagic = "S2BH"u8.ToArray();

    private enum Kind
    {
        Factor,
        Switch,
        Choice,
        Duration
    }

    private sealed record Field(string Label, Kind Kind, string[]? Choices = null);

    private static readonly string[] Amounts = ["Few", "Normal", "Many", "Extreme"];

    // The order of the fields in the block. Labels and choice names follow the server documentation's settings and values.
    private static readonly Field[] Fields =
    [
        new("Player health", Kind.Factor),
        new("Player mana", Kind.Factor),
        new("Player stamina", Kind.Factor),
        new("Player body heat", Kind.Factor),
        new("Player diving time", Kind.Factor),
        new("Durability", Kind.Switch),
        new("Starving debuff", Kind.Switch),
        new("Food buff duration", Kind.Factor),
        new("From hunger to starving", Kind.Duration),
        new("Shroud time", Kind.Factor),
        new("Tombstone", Kind.Choice, ["Backpack materials", "Everything", "No tombstone"]),
        new("Glider turbulence", Kind.Switch),
        new("Weather frequency", Kind.Choice, ["Disabled", "Rare", "Normal", "Often"]),
        new("Fishing difficulty", Kind.Choice, ["Very easy", "Easy", "Normal", "Hard", "Very hard"]),
        new("Mining damage", Kind.Factor),
        new("Plant growth speed", Kind.Factor),
        new("Resource drop amount", Kind.Factor),
        new("Production speed", Kind.Factor),
        new("Perk upgrade recycling", Kind.Factor),
        new("Perk cost", Kind.Factor),
        new("Combat XP", Kind.Factor),
        new("Mining XP", Kind.Factor),
        new("Exploration and quest XP", Kind.Factor),
        new("Random spawner amount", Kind.Choice, Amounts),
        new("Aggro pool amount", Kind.Choice, Amounts),
        new("Enemy damage", Kind.Factor),
        new("Enemy health", Kind.Factor),
        new("Enemy stamina", Kind.Factor),
        new("Enemy perception range", Kind.Factor),
        new("Boss damage", Kind.Factor),
        new("Boss health", Kind.Factor),
        new("Threat bonus", Kind.Factor),
        new("Pacify all enemies", Kind.Switch),
        new("Taming startle repercussion", Kind.Choice, ["Keep progress", "Lose some progress", "Lose all progress"]),
        new("Day length", Kind.Duration),
        new("Night length", Kind.Duration),
        new("Curse modifier", Kind.Choice, ["Easy", "Normal", "Hard"])
    ];

    // The few settings shown in the world details panel, like Dragonwilds' world rules.
    public static readonly IReadOnlyList<string> SummaryLabels =
        ["Enemy damage", "Enemy health", "Boss health", "Player health", "Mining damage", "Resource drop amount", "Combat XP", "Shroud time"];

    public static IReadOnlyList<EnshroudedWorldSetting>? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[ReadBytes];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return TryParse(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<EnshroudedWorldSetting>? TryParse(ReadOnlySpan<byte> start)
    {
        if (EnshroudedWorldNameReader.TryParse(start) is null)
        {
            return null;
        }

        var nameLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(start[(EnshroudedSaveHeader.Size + 71)..]);
        var block = EnshroudedSaveHeader.Size + 75 + nameLength + NameBlockTailBytes;
        if (start.Length < block + BlockBytes + BlockMagic.Length
            || !start.Slice(block, BlockMagic.Length).SequenceEqual(BlockMagic)
            || BinaryPrimitives.ReadUInt16LittleEndian(start[(block + 8)..]) != SettingsBlockType
            || BinaryPrimitives.ReadUInt16LittleEndian(start[(block + 10)..]) != SettingsBlockLayout
            || !start.Slice(block + BlockBytes, BlockMagic.Length).SequenceEqual(BlockMagic))
        {
            return null;
        }

        var offset = block + BlockHeaderBytes;
        var settings = new List<EnshroudedWorldSetting>(Fields.Length);
        foreach (var field in Fields)
        {
            var value = field.Kind switch
            {
                Kind.Factor => Factor(start, ref offset),
                Kind.Switch => Switch(start, ref offset),
                Kind.Choice => Choice(start, ref offset, field.Choices!),
                _ => Duration(start, ref offset)
            };
            if (value is null)
            {
                return null;
            }

            settings.Add(new EnshroudedWorldSetting(field.Label, value));
        }

        return offset == block + BlockHeaderBytes + FieldBytes ? settings : null;
    }

    private static string? Factor(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
        offset += 4;
        return float.IsFinite(value) && value is >= 0 and <= (float)MaxFactor
            ? "×" + value.ToString("0.##", CultureInfo.InvariantCulture)
            : null;
    }

    private static string? Switch(ReadOnlySpan<byte> data, ref int offset) => data[offset++] switch
    {
        0 => "Off",
        1 => "On",
        _ => null
    };

    // A value past the known names is shown by number rather than refused: a later game version may add choices.
    private static string? Choice(ReadOnlySpan<byte> data, ref int offset, string[] choices)
    {
        var value = data[offset++];
        return value < choices.Length ? choices[value] : value <= 15 ? $"Setting {value}" : null;
    }

    private static string? Duration(ReadOnlySpan<byte> data, ref int offset)
    {
        var nanoseconds = BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);
        offset += 8;
        if (nanoseconds < MinDurationNs || nanoseconds > MaxDurationNs)
        {
            return null;
        }

        var time = TimeSpan.FromTicks(nanoseconds / 100);
        if (time.TotalMinutes < 1)
        {
            return $"{time.Seconds} s";
        }

        var hours = (int)time.TotalHours;
        return hours == 0 ? $"{time.Minutes} min" : time.Minutes == 0 ? $"{hours} h" : $"{hours} h {time.Minutes} min";
    }
}
