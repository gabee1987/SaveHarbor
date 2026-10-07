using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Settings;

public sealed class SavedCloudFolderTests : IDisposable
{
    private const string FirstFolder = "TEST_FOLDER_ID_12345";
    private const string SecondFolder = "TEST_FOLDER_ID_67890";

    private readonly TempDirectory temp = new();
    private readonly CloudProviderOptions options = new();
    private readonly CloudProviderSettingsService service;

    public SavedCloudFolderTests()
    {
        service = new CloudProviderSettingsService(options, new FakeCloudProvider(), new TestPathProvider(temp), new NullAppLogger());
    }

    public void Dispose() => temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Save_RemembersEveryFolderWithItsName_CurrentFirst()
    {
        await service.SaveSharedFolderAsync(GameId.Dragonwilds, $"https://drive.google.com/drive/folders/{FirstFolder}?usp=sharing", "TEST_FOLDER_A", Token);
        await service.SaveSharedFolderAsync(GameId.Dragonwilds, SecondFolder, "TEST_FOLDER_B", Token);

        var saved = service.GetSavedFolders(GameId.Dragonwilds);

        Assert.Equal(new[] { SecondFolder, FirstFolder }, saved.Select(folder => folder.FolderId));
        Assert.Equal("TEST_FOLDER_A", saved[1].Name);
        Assert.Empty(service.GetSavedFolders(GameId.Windrose));
    }

    [Fact]
    public async Task Save_SameFolderAgain_KeepsOneEntry()
    {
        await service.SaveSharedFolderAsync(GameId.Dragonwilds, FirstFolder, "TEST_FOLDER_A", Token);
        await service.SaveSharedFolderAsync(GameId.Dragonwilds, FirstFolder, null, Token);

        var folder = Assert.Single(service.GetSavedFolders(GameId.Dragonwilds));
        Assert.Equal("TEST_FOLDER_A", folder.Name);
    }

    [Fact]
    public async Task FolderOfAnotherGame_IsRefused()
    {
        await service.SaveSharedFolderAsync(GameId.Windrose, FirstFolder, "TEST_FOLDER_A", Token);

        var test = await service.TestSharedFolderAsync(GameId.Dragonwilds, FirstFolder, Token);

        Assert.False(test.IsSuccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSharedFolderAsync(GameId.Dragonwilds, FirstFolder, null, Token));
        Assert.Equal(FirstFolder, options.GetSharedFolderId(GameId.Windrose));
        Assert.Empty(service.GetSavedFolders(GameId.Dragonwilds));
    }

    [Fact]
    public async Task Forget_RemovesOnlyThatFolder()
    {
        await service.SaveSharedFolderAsync(GameId.Dragonwilds, FirstFolder, "TEST_FOLDER_A", Token);
        await service.SaveSharedFolderAsync(GameId.Dragonwilds, SecondFolder, "TEST_FOLDER_B", Token);

        await service.ForgetSavedFolderAsync(GameId.Dragonwilds, FirstFolder, Token);

        Assert.Equal(SecondFolder, Assert.Single(service.GetSavedFolders(GameId.Dragonwilds)).FolderId);
    }

    [Fact]
    public void CurrentFolderFromBeforeSavedFolders_IsStillOffered()
    {
        options.SetSharedFolderId(GameId.Dragonwilds, FirstFolder);

        var folder = Assert.Single(service.GetSavedFolders(GameId.Dragonwilds));

        Assert.Equal(FirstFolder, folder.FolderId);
        Assert.Equal(string.Empty, folder.Name);
    }

    [Theory]
    [InlineData("not a folder link")]
    [InlineData("TEST'); DROP")]
    [InlineData("short")]
    public async Task Test_MalformedFolderId_IsRejectedBeforeAnyCloudCall(string input)
    {
        var result = await service.TestSharedFolderAsync(GameId.Dragonwilds, input, Token);

        Assert.False(result.IsSuccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSharedFolderAsync(GameId.Dragonwilds, input, null, Token));
    }
}
