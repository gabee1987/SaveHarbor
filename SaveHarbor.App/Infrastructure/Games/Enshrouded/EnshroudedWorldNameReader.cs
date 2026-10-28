using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// The world name sits uncompressed in the first block after the header: an "S2BH" block that repeats the world GUID,
// then a length-prefixed UTF-8 name at a fixed offset. The layout is observed on 1.0 worlds, not documented by the
// game, so every check below must pass or the name is treated as unknown and the caller falls back to the slot name.
public static class EnshroudedWorldNameReader
{
    public const int MaxNameBytes = 128;
    private const int BlockStart = EnshroudedSaveHeader.Size;
    private const int GuidCopyOffset = BlockStart + 14;
    private const int NameLengthOffset = BlockStart + 71;
    private const int NameOffset = NameLengthOffset + 4;
    private static readonly byte[] BlockMagic = "S2BH"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[NameOffset + MaxNameBytes];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return TryParse(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? TryParse(ReadOnlySpan<byte> start)
    {
        if (start.Length < NameOffset
            || EnshroudedSaveHeader.TryParse(start) is null
            || !start.Slice(BlockStart, BlockMagic.Length).SequenceEqual(BlockMagic)
            || !start.Slice(GuidCopyOffset, 16).SequenceEqual(EnshroudedSaveHeader.GuidBytes(start)))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(start[NameLengthOffset..]);
        if (length is 0 or > MaxNameBytes || start.Length < NameOffset + (int)length)
        {
            return null;
        }

        try
        {
            var name = StrictUtf8.GetString(start.Slice(NameOffset, (int)length)).Trim();
            return name.Length > 0 && !name.Any(char.IsControl) ? name : null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
