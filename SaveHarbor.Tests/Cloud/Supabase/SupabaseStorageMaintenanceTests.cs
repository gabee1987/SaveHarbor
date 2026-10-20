using System.Net;
using System.Net.Http;
using SaveHarbor.App.Domain;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// Storage used by the group, and the clean-up of archives no version points to.
public sealed class SupabaseStorageMaintenanceTests : IDisposable
{
    private const string GamePrefix = SupabaseHarness.GroupId + "/windrose/";
    private const string OrphanPath = GamePrefix + "TEST_WORLD_ID/20261019_090000_TEST_USER_v9.zip";
    private const string ListPath = "storage/v1/object/list/saves";

    private readonly SupabaseHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Archives(params (string Path, long Size)[] rows) =>
        "[" + string.Join(",", rows.Select((row, index) =>
            $$"""{"id":"3333333{{index}}-3333-4333-8333-333333333333","archive_path":"{{row.Path}}","archive_size_bytes":{{row.Size}}}""")) + "]";

    private static string Files(params (string Name, string CreatedAt, long Size)[] files) =>
        "[" + string.Join(",", files.Select(file =>
            $$$"""{"name":"{{{file.Name}}}","id":"55555555-5555-4555-8555-555555555555","created_at":"{{{file.CreatedAt}}}","metadata":{"size":{{{file.Size}}}}}""")) + "]";

    private const string WorldFolder = """[{"name":"TEST_WORLD_ID","id":null,"created_at":null,"metadata":null}]""";

    [Fact]
    public async Task StorageUsage_SumsTheGroupsVersions()
    {
        await harness.SignInWithGroupAsync();
        harness.Handler.On(HttpMethod.Get, "rest/v1/world_versions", HttpStatusCode.OK, Archives((SupabaseHarness.ObjectPath, 1000), (OrphanPath, 2500)));

        var usage = await harness.Provider.GetStorageUsageAsync(GameId.Windrose, Token);

        Assert.Equal(new CloudStorageUsage(3500, 1073741824), usage);
        Assert.Contains($"group_id=eq.{SupabaseHarness.GroupId}", harness.Handler.To("rest/v1/world_versions").Single().Query);
    }

    [Fact]
    public async Task StorageUsage_WithoutAGroup_IsUnknown()
    {
        harness.SignIn();

        Assert.Null(await harness.Provider.GetStorageUsageAsync(GameId.Windrose, Token));
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task UnusedFiles_AreOldArchivesNoVersionPointsTo()
    {
        await harness.SignInWithGroupAsync();
        harness.Handler.On(HttpMethod.Get, "rest/v1/world_versions", HttpStatusCode.OK, Archives((SupabaseHarness.ObjectPath, 1000)));
        harness.Handler
            .On(HttpMethod.Post, ListPath, HttpStatusCode.OK, WorldFolder)
            .On(HttpMethod.Post, ListPath, HttpStatusCode.OK, Files(
                (SupabaseHarness.VersionId + ".zip", "2026-10-19T09:00:00Z", 1000),
                ("20261019_090000_TEST_USER_v9.zip", "2026-10-19T09:00:00Z", 700),
                ("20261020_122000_TEST_USER_v10.zip", "2026-10-20T12:20:00Z", 900)));

        var unused = await harness.Provider.FindUnusedFilesAsync(GameId.Windrose, Token);

        // The v10 archive is ten minutes old: a friend's upload may still be publishing it.
        Assert.Equal([OrphanPath], unused.Paths);
        Assert.Equal(700, unused.TotalBytes);
        var listings = harness.Handler.To(ListPath);
        Assert.Contains($"\"prefix\":\"{GamePrefix}\"", listings[0].Body);
        Assert.Contains($"\"prefix\":\"{GamePrefix}TEST_WORLD_ID/\"", listings[1].Body);
    }

    [Fact]
    public async Task UnusedFiles_SkipFoldersThatAreNoWorld()
    {
        await harness.SignInWithGroupAsync();
        harness.Handler.On(HttpMethod.Get, "rest/v1/world_versions", HttpStatusCode.OK, "[]");
        harness.Handler.On(HttpMethod.Post, ListPath, HttpStatusCode.OK, """[{"name":"..","id":null}]""");

        var unused = await harness.Provider.FindUnusedFilesAsync(GameId.Windrose, Token);

        Assert.Equal(0, unused.Count);
        Assert.Single(harness.Handler.To(ListPath));
    }

    [Fact]
    public async Task UnusedFiles_WhenTheVersionListFails_FindNothingToDelete()
    {
        await harness.SignInWithGroupAsync();
        harness.Handler.On(HttpMethod.Get, "rest/v1/world_versions", HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAnyAsync<Exception>(() => harness.Provider.FindUnusedFilesAsync(GameId.Windrose, Token));
        Assert.Empty(harness.Handler.To(ListPath));
    }

    [Fact]
    public async Task RemoveUnusedFiles_DeletesOnlyPathsOfThisGamesFolder()
    {
        await harness.SignInWithGroupAsync();
        harness.Handler.On(HttpMethod.Delete, "storage/v1/object/saves", HttpStatusCode.OK, "[]");
        var files = new CloudUnusedFiles(
            [OrphanPath, "12111111-1111-4111-8111-111111111111/windrose/TEST_WORLD_ID/x.zip", GamePrefix + "../dragonwilds/x.zip"],
            1);

        await harness.Provider.RemoveUnusedFilesAsync(GameId.Windrose, files, Token);

        var delete = harness.Handler.To("storage/v1/object/saves").Single();
        Assert.Contains(OrphanPath, delete.Body);
        Assert.DoesNotContain("12111111", delete.Body);
        Assert.DoesNotContain("dragonwilds", delete.Body);
    }

    [Fact]
    public async Task GroupArchives_AreReadInPages()
    {
        await harness.SignInWithGroupAsync();
        var fullPage = "[" + string.Join(",", Enumerable.Range(0, 1000).Select(index =>
            $$"""{"id":"{{Guid.NewGuid():D}}","archive_path":"{{GamePrefix}}TEST_WORLD_ID/{{index}}.zip","archive_size_bytes":1}""")) + "]";
        harness.Handler
            .On(HttpMethod.Get, "rest/v1/world_versions", HttpStatusCode.OK, fullPage)
            .On(HttpMethod.Get, "rest/v1/world_versions", HttpStatusCode.OK, Archives((OrphanPath, 5)));

        var usage = await harness.Provider.GetStorageUsageAsync(GameId.Windrose, Token);

        Assert.Equal(1005, usage!.UsedBytes);
        var pages = harness.Handler.To("rest/v1/world_versions");
        Assert.Equal(2, pages.Count);
        Assert.Contains("offset=1000", pages[1].Query);
    }
}
