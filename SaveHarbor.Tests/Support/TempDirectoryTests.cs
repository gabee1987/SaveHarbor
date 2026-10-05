namespace SaveHarbor.Tests.Support;

public sealed class TempDirectoryTests
{
    [Fact]
    public void Dispose_AfterCreate_RemovesFolder()
    {
        var directory = new TempDirectory();
        var path = directory.Path;
        Assert.True(Directory.Exists(path));

        directory.Dispose();

        Assert.False(Directory.Exists(path));
    }
}
