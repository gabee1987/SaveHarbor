using System.Buffers.Binary;
using System.Text;
using SaveHarbor.App.Infrastructure.Games.Enshrouded;

namespace SaveHarbor.Tests.Support;

// Writes synthetic Enshrouded 1.0 saves: the 48-byte "SV2C" header, then an "S2BH" block that repeats the GUID and
// carries the world name at the observed offset, optionally the world settings block, then filler. The filler is not
// valid zstd; SaveHarbor never reads it. Placeholder data only.
public static class EnshroudedSaveBuilder
{
    public const string WorldIdOne = "00000000000000000000000000000001";
    public const string WorldIdTwo = "00000000000000000000000000000002";

    // The 127 field bytes of a settings block as observed on a 1.0 world: ×2 mining damage, ×1.25 plant growth,
    // ×1.5 resource drops and production speed, ×0.75 perk cost, everything else at its usual value.
    public static readonly byte[] ObservedSettingsFields = Convert.FromHexString(
        "0000803F0000803F0000803F0000803F0000803F01000000803F0070C9B28B0000000000803F00010202" +
        "000000400000A03F0000C03F0000C03F0000803F0000403F0000803F0000803F0000803F01010000803F" +
        "0000803F0000803F0000803F0000803F0000803F0000803F000100505C18A3010000002058A3A7000000" +
        "01");

    public static byte[] Build(
        string worldId, uint saveCounter = 1, DateTimeOffset? savedAtUtc = null, string? worldName = "TEST_WORLD", int fillerBytes = 64, byte[]? settingsFields = null)
    {
        var guid = Convert.FromHexString(worldId);
        var name = worldName is null ? [] : Encoding.UTF8.GetBytes(worldName);
        var nameBlock = 75 + name.Length;
        // With settings: the name block's 18-byte tail, the settings block (14-byte head, fields, one trailing byte)
        // and the magic of the block after it.
        var block = new byte[settingsFields is null ? nameBlock : nameBlock + 18 + 14 + settingsFields.Length + 1 + 4];
        "S2BH"u8.CopyTo(block);
        guid.CopyTo(block, 14);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(71), (uint)name.Length);
        name.CopyTo(block, 75);
        if (settingsFields is not null)
        {
            var settings = block.AsSpan(nameBlock + 18);
            "S2BH"u8.CopyTo(settings);
            BinaryPrimitives.WriteUInt32LittleEndian(settings[4..], 2);
            BinaryPrimitives.WriteUInt16LittleEndian(settings[8..], 12);
            BinaryPrimitives.WriteUInt16LittleEndian(settings[10..], 3);
            settingsFields.CopyTo(settings[14..]);
            "S2BH"u8.CopyTo(settings[(14 + settingsFields.Length + 1)..]);
        }

        var bytes = new byte[EnshroudedSaveHeader.Size + block.Length + fillerBytes];
        "SV2C"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), EnshroudedSaveHeader.Size);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        guid.CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), saveCounter);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(32), (savedAtUtc ?? new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)).ToUnixTimeSeconds());
        block.CopyTo(bytes, EnshroudedSaveHeader.Size);
        Random.Shared.NextBytes(bytes.AsSpan(EnshroudedSaveHeader.Size + block.Length));
        return bytes;
    }

    public static string Write(string folder, string fileName, string worldId, uint saveCounter = 1, string? worldName = "TEST_WORLD", byte[]? settingsFields = null)
    {
        var path = Path.Combine(folder, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Build(worldId, saveCounter, worldName: worldName, settingsFields: settingsFields));
        return path;
    }
}
