using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace SaveHarbor.App.Infrastructure.Games.Dragonwilds;

public sealed record DragonwildsChunk(string Tag, long SizeBytes);

// Lists the top-level sections of a world save ("SAVE" + size, then tag + size chunks) by reading only the
// 8-byte chunk headers and seeking over the content. Section contents are never read.
public static class DragonwildsSaveLayout
{
    private const int MaxChunks = 64;

    public static async Task<IReadOnlyList<DragonwildsChunk>> ReadChunksAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[8];
            var chunks = new List<DragonwildsChunk>();
            if (stream.Length < 8 || await stream.ReadAsync(header, cancellationToken) != 8 || !header.AsSpan(0, 4).SequenceEqual("SAVE"u8))
            {
                return [];
            }

            while (chunks.Count < MaxChunks && stream.Length - stream.Position >= 8)
            {
                await stream.ReadExactlyAsync(header, cancellationToken);
                var size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                if (!header.Take(4).All(value => value is >= (byte)'A' and <= (byte)'Z') || size < 0 || size > stream.Length - stream.Position)
                {
                    break;
                }

                chunks.Add(new DragonwildsChunk(Encoding.ASCII.GetString(header, 0, 4), size));
                stream.Seek(size, SeekOrigin.Current);
            }

            return chunks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
