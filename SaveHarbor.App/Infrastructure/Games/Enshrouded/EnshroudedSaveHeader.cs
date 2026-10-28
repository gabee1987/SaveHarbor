using System.Buffers.Binary;
using System.IO;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// The fixed 48-byte "SV2C" header every Enshrouded 1.0 save starts with (world, character and global files alike).
// Only these bytes are read; the compressed blocks after them are never parsed or changed.
public sealed record EnshroudedSaveHeader(string WorldId, ushort FormatVersion, uint SaveCounter, DateTimeOffset SavedAtUtc)
{
    public const int Size = 48;
    private const ushort SupportedVersion = 2;
    private const int GuidOffset = 12;
    private const int GuidLength = 16;
    private static readonly byte[] Magic = "SV2C"u8.ToArray();

    // The GUID bytes in file order as 32 lowercase hex characters. Guid.ToString is not used: its byte order differs.
    public static string ToWorldId(ReadOnlySpan<byte> guidBytes) => Convert.ToHexStringLower(guidBytes);

    public static ReadOnlySpan<byte> GuidBytes(ReadOnlySpan<byte> header) => header.Slice(GuidOffset, GuidLength);

    public static bool IsWorldId(string? value) =>
        value is { Length: GuidLength * 2 } && value.All(char.IsAsciiHexDigitLower);

    // Null when the file is missing, locked, too short or not an Enshrouded 1.0 save.
    public static EnshroudedSaveHeader? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> buffer = stackalloc byte[Size];
            return stream.ReadAtLeast(buffer, Size, throwOnEndOfStream: false) == Size ? TryParse(buffer) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static EnshroudedSaveHeader? TryParse(ReadOnlySpan<byte> header)
    {
        if (header.Length < Size
            || !header[..4].SequenceEqual(Magic)
            || BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != SupportedVersion
            || BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != Size)
        {
            return null;
        }

        var seconds = BinaryPrimitives.ReadInt64LittleEndian(header[32..]);
        if (seconds < 0 || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return null;
        }

        return new EnshroudedSaveHeader(
            ToWorldId(GuidBytes(header)),
            SupportedVersion,
            BinaryPrimitives.ReadUInt32LittleEndian(header[28..]),
            DateTimeOffset.FromUnixTimeSeconds(seconds));
    }
}
