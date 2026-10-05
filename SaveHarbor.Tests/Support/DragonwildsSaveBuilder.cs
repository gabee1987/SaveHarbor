using System.Buffers.Binary;
using System.Text;

namespace SaveHarbor.Tests.Support;

// Builds a minimal Dragonwilds world save with the observed "SAVE" / "INFO" / "CINF" layout. Placeholder data only.
public sealed class DragonwildsSaveBuilder
{
    private readonly List<(string Key, byte[] Value)> entries = [];

    public DragonwildsSaveBuilder String(string key, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        entries.Add((key, [.. Int(bytes.Length), .. bytes]));
        return this;
    }

    public DragonwildsSaveBuilder Int32(string key, int value)
    {
        entries.Add((key, Int(value)));
        return this;
    }

    public DragonwildsSaveBuilder Byte(string key, byte value)
    {
        entries.Add((key, [value]));
        return this;
    }

    public DragonwildsSaveBuilder Single(string key, float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        entries.Add((key, bytes));
        return this;
    }

    public DragonwildsSaveBuilder Ticks(string key, DateTime utc)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, utc.Ticks);
        entries.Add((key, bytes));
        return this;
    }

    public DragonwildsSaveBuilder Raw(string key, byte[] value)
    {
        entries.Add((key, value));
        return this;
    }

    public byte[] Build()
    {
        var cinf = new List<byte>();
        cinf.AddRange(Int(entries.Count));
        foreach (var (key, _) in entries)
        {
            var bytes = Encoding.ASCII.GetBytes(key + "\0");
            cinf.AddRange(Int(bytes.Length));
            cinf.AddRange(bytes);
        }

        cinf.AddRange(Int(entries.Count));
        var offset = 0;
        foreach (var (_, value) in entries)
        {
            cinf.AddRange(Int(offset));
            offset += value.Length;
        }

        cinf.AddRange(Int(offset));
        foreach (var (_, value) in entries)
        {
            cinf.AddRange(value);
        }

        var timestamp = Encoding.ASCII.GetBytes("2026-01-01T00:00:00.000Z\0");
        byte[] info = [.. new byte[19], .. Int(timestamp.Length), .. timestamp, .. "CINF"u8, .. Int(cinf.Count), .. cinf];
        byte[] body = [.. "INFO"u8, .. Int(info.Length), .. info, .. "GLOB"u8, .. Int(4), 0, 0, 0, 0];
        return [.. "SAVE"u8, .. Int(body.Length), .. body];
    }

    private static byte[] Int(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
