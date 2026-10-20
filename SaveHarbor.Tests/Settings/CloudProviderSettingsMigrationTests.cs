using System.Text.Json.Nodes;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Settings;

// cloud-provider-settings.json from before SaveHarbor Cloud: its Google Drive setup survives the groups being added.
public sealed class CloudProviderSettingsMigrationTests : IDisposable
{
    private const string GroupId = SupabaseHarness.GroupId;
    private const string FolderId = "TEST_FOLDER_ID_12345";

    private readonly TempDirectory temp = new();
    private readonly TestPathProvider paths;

    public CloudProviderSettingsMigrationTests()
    {
        paths = new TestPathProvider(temp);
    }

    public void Dispose() => temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CloudProviderSettingsService Service() =>
        new(new CloudProviderOptions(), new FakeCloudProvider(), paths, new NullAppLogger());

    private void WriteSettings(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.CloudProviderSettingsPath)!);
        File.WriteAllText(paths.CloudProviderSettingsPath, json);
    }

    private JsonNode ReadSettings() => JsonNode.Parse(File.ReadAllText(paths.CloudProviderSettingsPath))!;

    [Fact]
    public async Task Version1_KeepsItsLegacyFolderWhenAGroupIsChosen()
    {
        WriteSettings($$"""{"SchemaVersion":1,"GoogleSharedFolderId":"{{FolderId}}"}""");
        var service = Service();

        Assert.Empty(service.GetKnownGroups());
        await service.SetActiveGroupsAsync([GameId.Windrose], GroupId, Token);

        var saved = ReadSettings();
        Assert.Equal(3, saved["SchemaVersion"]!.GetValue<int>());
        Assert.Equal(FolderId, saved["GoogleSharedFolderId"]!.GetValue<string>());
        Assert.Equal(GroupId, Service().GetActiveGroupId(GameId.Windrose));
    }

    [Fact]
    public async Task Version2_KeepsFoldersAndSavedFoldersWhenGroupsAreListed()
    {
        WriteSettings($$"""
            {
              "SchemaVersion": 2,
              "SharedFolders": { "windrose": "{{FolderId}}" },
              "SavedFolders": [ { "Game": "windrose", "FolderId": "{{FolderId}}", "Name": "TEST_FOLDER" } ]
            }
            """);
        var service = Service();

        await service.SaveKnownGroupsAsync([new SavedCloudGroup { Id = GroupId, Name = "TEST_GROUP", Role = "member" }], Token);

        var saved = ReadSettings();
        Assert.Equal(FolderId, saved["SharedFolders"]!["windrose"]!.GetValue<string>());
        Assert.Equal("TEST_FOLDER", saved["SavedFolders"]![0]!["Name"]!.GetValue<string>());
        Assert.Equal("TEST_GROUP", Assert.Single(Service().GetKnownGroups()).Name);
    }

    [Fact]
    public void NullGroupLists_ReadAsEmpty()
    {
        WriteSettings("""{"SchemaVersion":3,"ActiveGroups":null,"KnownGroups":null}""");
        var service = Service();

        Assert.Empty(service.GetKnownGroups());
        Assert.Null(service.GetActiveGroupId(GameId.Dragonwilds));
    }

    [Fact]
    public async Task GameKeys_AreCaseInsensitive()
    {
        WriteSettings($$$"""{"SchemaVersion":3,"ActiveGroups":{"WINDROSE":"{{{GroupId}}}"}}""");

        Assert.Equal(GroupId, Service().GetActiveGroupId(GameId.Windrose));

        await Service().SetActiveGroupsAsync([GameId.Windrose], "12111111-1111-4111-8111-111111111111", Token);
        Assert.Equal("12111111-1111-4111-8111-111111111111", Service().GetActiveGroupId(GameId.Windrose));
    }

    [Fact]
    public async Task UnreadableFile_IsReplacedWithTheGroup()
    {
        WriteSettings("{ not json");

        await Service().SetActiveGroupsAsync([GameId.Dragonwilds], GroupId, Token);

        Assert.Equal(GroupId, Service().GetActiveGroupId(GameId.Dragonwilds));
    }

    [Fact]
    public async Task ClearGroups_LeavesTheDriveSetup()
    {
        WriteSettings($$$"""{"SchemaVersion":3,"SharedFolders":{"windrose":"{{{FolderId}}}"},"ActiveGroups":{"windrose":"{{{GroupId}}}"},"KnownGroups":[{"Id":"{{{GroupId}}}","Name":"TEST_GROUP","Role":"owner"}]}""");

        await Service().ClearGroupsAsync(Token);

        var saved = ReadSettings();
        Assert.Equal(FolderId, saved["SharedFolders"]!["windrose"]!.GetValue<string>());
        Assert.Empty(saved["KnownGroups"]!.AsArray());
        Assert.Empty(saved["ActiveGroups"]!.AsObject());
    }
}
