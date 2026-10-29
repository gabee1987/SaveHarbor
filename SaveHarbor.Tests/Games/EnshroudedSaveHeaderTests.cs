using SaveHarbor.App.Infrastructure.Games.Enshrouded;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

public sealed class EnshroudedSaveHeaderTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    private string WriteBytes(byte[] bytes)
    {
        var path = temp.Combine("world0.ksd");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void TryRead_ValidWorld_ReadsIdCounterAndTimestamp()
    {
        var savedAt = new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.Zero);
        var path = WriteBytes(EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, saveCounter: 18, savedAtUtc: savedAt));

        var header = EnshroudedSaveHeader.TryRead(path);

        Assert.NotNull(header);
        Assert.Equal(EnshroudedSaveBuilder.WorldIdOne, header.WorldId);
        Assert.Equal(18u, header.SaveCounter);
        Assert.Equal(savedAt, header.SavedAtUtc);
        Assert.Equal(2, header.FormatVersion);
    }

    [Fact]
    public void TryRead_WorldId_KeepsFileByteOrder()
    {
        const string worldId = "0102030405060708090a0b0c0d0e0f10";
        var path = WriteBytes(EnshroudedSaveBuilder.Build(worldId));

        Assert.Equal(worldId, EnshroudedSaveHeader.TryRead(path)!.WorldId);
    }

    [Fact]
    public void TryRead_ExactlyHeaderLength_IsAccepted()
    {
        var path = WriteBytes(EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne)[..EnshroudedSaveHeader.Size]);

        Assert.NotNull(EnshroudedSaveHeader.TryRead(path));
    }

    [Theory]
    [InlineData(0, (byte)'X')]
    [InlineData(4, (byte)3)]
    [InlineData(6, (byte)64)]
    public void TryRead_WrongMagicVersionOrHeaderSize_ReturnsNull(int offset, byte value)
    {
        var bytes = EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne);
        bytes[offset] = value;

        Assert.Null(EnshroudedSaveHeader.TryRead(WriteBytes(bytes)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(47)]
    public void TryRead_ShortFile_ReturnsNull(int length)
    {
        var path = WriteBytes(EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne)[..length]);

        Assert.Null(EnshroudedSaveHeader.TryRead(path));
    }

    [Fact]
    public void TryRead_MissingFile_ReturnsNull()
    {
        Assert.Null(EnshroudedSaveHeader.TryRead(temp.Combine("world9.ksd")));
    }

    [Theory]
    [InlineData("00000000000000000000000000000001", true)]
    [InlineData("0000000000000000000000000000000A", false)]
    [InlineData("0000000000000000000000000000001", false)]
    [InlineData("..\\00000000000000000000000000000", false)]
    [InlineData("", false)]
    public void IsWorldId_AcceptsOnlyLowercaseHexOfSixteenBytes(string value, bool expected)
    {
        Assert.Equal(expected, EnshroudedSaveHeader.IsWorldId(value));
    }

    [Fact]
    public void WorldName_StoredAfterHeader_IsRead()
    {
        var path = WriteBytes(EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, worldName: "TEST_WORLD Ünïcode"));

        Assert.Equal("TEST_WORLD Ünïcode", EnshroudedWorldNameReader.TryRead(path));
    }

    [Fact]
    public void WorldName_GuidCopyDiffers_IsNotTrusted()
    {
        var bytes = EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne);
        bytes[EnshroudedSaveHeader.Size + 14] ^= 0xFF;

        Assert.Null(EnshroudedWorldNameReader.TryRead(WriteBytes(bytes)));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(EnshroudedWorldNameReader.MaxNameBytes + 1u)]
    [InlineData(uint.MaxValue)]
    public void WorldName_ImplausibleLength_IsNotTrusted(uint length)
    {
        var bytes = EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, fillerBytes: 512);
        BitConverter.TryWriteBytes(bytes.AsSpan(EnshroudedSaveHeader.Size + 71), length);

        Assert.Null(EnshroudedWorldNameReader.TryRead(WriteBytes(bytes)));
    }

    [Fact]
    public void WorldName_InvalidUtf8OrControlCharacters_IsNotTrusted()
    {
        var invalid = EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, worldName: "TEST_WORLD");
        invalid[EnshroudedSaveHeader.Size + 75] = 0xFF;
        var control = EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, worldName: "TEST\nWORLD");

        Assert.Null(EnshroudedWorldNameReader.TryParse(invalid));
        Assert.Null(EnshroudedWorldNameReader.TryParse(control));
    }

    [Fact]
    public void WorldName_FileEndsInsideTheName_IsNotTrusted()
    {
        var bytes = EnshroudedSaveBuilder.Build(EnshroudedSaveBuilder.WorldIdOne, worldName: "TEST_WORLD", fillerBytes: 0);

        Assert.Null(EnshroudedWorldNameReader.TryParse(bytes[..^2]));
    }
}
