using SaveHarbor.App.Utilities;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Utilities;

public sealed class SafePathTests
{
    [Theory]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("My World")]
    [InlineData("My·World")]
    [InlineData("world.sav")]
    public void IsSafeSegment_SafeName_ReturnsTrue(string name)
    {
        Assert.True(SafePath.IsSafeSegment(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("..\\evil")]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("C:\\x")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("a|b")]
    public void IsSafeSegment_UnsafeName_ReturnsFalse(string name)
    {
        Assert.False(SafePath.IsSafeSegment(name));
    }

    [Fact]
    public void CombineUnderRoot_SafeName_ReturnsPathUnderRoot()
    {
        using var temp = new TempDirectory();

        var combined = SafePath.CombineUnderRoot(temp.Path, "My World");

        Assert.Equal(Path.Combine(Path.GetFullPath(temp.Path), "My World"), combined);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("..\\evil")]
    [InlineData("C:\\x")]
    public void CombineUnderRoot_UnsafeName_Throws(string name)
    {
        using var temp = new TempDirectory();

        Assert.Throws<InvalidDataException>(() => SafePath.CombineUnderRoot(temp.Path, name));
    }
}
