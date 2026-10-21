using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// The sign-in is kept encrypted for this Windows user; anything unreadable counts as signed out.
public sealed class SupabaseSessionStoreTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly TestPathProvider paths;
    private readonly SupabaseSessionStore store;

    public SupabaseSessionStoreTests()
    {
        paths = new TestPathProvider(temp);
        store = new SupabaseSessionStore(paths, new NullAppLogger());
    }

    public void Dispose() => temp.Dispose();

    private static SupabaseSession Session(string accessToken = "TEST_ACCESS_TOKEN") =>
        new(accessToken, "TEST_REFRESH_TOKEN", new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero), "test@example.invalid", "12345");

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        store.Save(Session());

        Assert.Equal(Session(), store.Load());
    }

    [Fact]
    public void Save_EncryptsTheFile()
    {
        store.Save(Session());

        var bytes = File.ReadAllBytes(paths.SupabaseSessionPath);
        Assert.DoesNotContain("TEST_ACCESS_TOKEN", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("test@example.invalid", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Save_HidesTheFileAndCanReplaceIt()
    {
        store.Save(Session());
        store.Save(Session("TEST_ACCESS_TOKEN_2"));

        var attributes = File.GetAttributes(paths.SupabaseSessionPath);
        Assert.True(attributes.HasFlag(FileAttributes.Hidden));
        Assert.True(attributes.HasFlag(FileAttributes.NotContentIndexed));
        Assert.Equal("TEST_ACCESS_TOKEN_2", store.Load()!.AccessToken);
    }

    [Fact]
    public void Load_NoFile_IsSignedOut()
    {
        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_CorruptFile_IsSignedOut()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.SupabaseSessionPath)!);
        File.WriteAllBytes(paths.SupabaseSessionPath, [1, 2, 3, 4]);

        Assert.Null(store.Load());
    }

    [Fact]
    public void Clear_RemovesTheHiddenFile()
    {
        store.Save(Session());

        store.Clear();

        Assert.False(File.Exists(paths.SupabaseSessionPath));
    }

    [Fact]
    public void ToString_LeavesTokensOut()
    {
        var text = Session().ToString();

        Assert.DoesNotContain("TEST_ACCESS_TOKEN", text);
        Assert.DoesNotContain("TEST_REFRESH_TOKEN", text);
        Assert.DoesNotContain("test@example.invalid", text);
    }
}
