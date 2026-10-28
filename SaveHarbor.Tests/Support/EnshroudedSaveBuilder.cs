using System.Buffers.Binary;
using System.Text;
using SaveHarbor.App.Infrastructure.Games.Enshrouded;

namespace SaveHarbor.Tests.Support;

// Writes synthetic Enshrouded 1.0 saves: the 48-byte "SV2C" header, then an "S2BH" block that repeats the GUID and
// carries the world name at the observed offset, then filler. The filler is not valid zstd; SaveHarbor never reads it.
// Placeholder data only.
public static class EnshroudedSaveBuilder
{
    public const string WorldIdOne = "00000000000000000000000000000001";
    public const string WorldIdTwo = "00000000000000000000000000000002";

    public static byte[] Build(string worldId, uint saveCounter = 1, DateTimeOffset? savedAtUtc = null, string? worldName = "TEST_WORLD", int fillerBytes = 64)
    {
        var guid = Convert.FromHexString(worldId);
        var name = worldName is null ? [] : Encoding.UTF8.GetBytes(worldName);
        var block = new byte[75 + name.Length];
        "S2BH"u8.CopyTo(block);
        guid.CopyTo(block, 14);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(71), (uint)name.Length);
        name.CopyTo(block, 75);

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

    public static string Write(string folder, string fileName, string worldId, uint saveCounter = 1, string? worldName = "TEST_WORLD")
    {
        var path = Path.Combine(folder, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Build(worldId, saveCounter, worldName: worldName));
        return path;
    }
}
