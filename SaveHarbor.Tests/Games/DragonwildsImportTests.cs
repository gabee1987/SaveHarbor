using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Games.Dragonwilds;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Games;

public sealed class DragonwildsImportTests : IDisposable
{
    private readonly BackupHarness harness = new();

    public void Dispose() => harness.Dispose();

    private static byte[] Save(string worldName, int revision, int guid = 12345) => new DragonwildsSaveBuilder()
        .Int32("VERSION", 9)
        .Int32("GUID_A", guid)
        .Int32("GUID_B", 2)
        .Int32("GUID_C", 3)
        .Int32("GUID_D", 4)
        .String("WorldName", worldName)
        .String("SessionPasswd", "TEST_SECRET_VALUE")
        .String("WorldOwnerId", "TEST_OWNER_ID")
        .String("WorldNameOwner", "TEST_USER")
        .Int32("Meta_SaveFileRevision", revision)
        .Int32("TEST_UNKNOWN_FIELD", 7)
        .Build();

    private string WriteOutside(string fileName, byte[] bytes)
    {
        var folder = Path.Combine(harness.TempPath, "TEST_DOWNLOADS");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task Candidate_RenamedDownload_IsImportedUnderTheStoredWorldName()
    {
        var path = WriteOutside("TEST_WORLD (1).sav", Save("TEST_WORLD", 40));

        var candidate = await harness.DragonwildsAdapter.ReadImportCandidateAsync(path, TestContext.Current.CancellationToken);

        Assert.NotNull(candidate);
        Assert.Equal("TEST_WORLD", candidate.World.WorldId);
        Assert.Equal("00003039-00000002-00000003-00000004", candidate.Fingerprint);
        Assert.Equal(40, candidate.Revision);
        Assert.DoesNotContain(candidate.Summary, item => item.Value.Contains("TEST_SECRET_VALUE") || item.Value.Contains("TEST_OWNER_ID"));
    }

    [Fact]
    public async Task Candidate_GameBackupCopy_IsAccepted()
    {
        var path = WriteOutside("TEST_WORLD.sav.backup", Save("TEST_WORLD", 39));

        var candidate = await harness.DragonwildsAdapter.ReadImportCandidateAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal("TEST_WORLD", candidate?.World.WorldId);
    }

    [Theory]
    [InlineData("TEST_WORLD.sav", "not a save")]
    [InlineData("TEST_WORLD.txt", "")]
    [InlineData("EnhancedInputUserSettings.sav", "")]
    public async Task Candidate_NotAWorldSave_IsRejected(string fileName, string content)
    {
        var bytes = content.Length > 0 ? System.Text.Encoding.ASCII.GetBytes(content) : Save("TEST_WORLD", 1);
        var path = WriteOutside(fileName, bytes);

        Assert.Null(await harness.DragonwildsAdapter.ReadImportCandidateAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportSaveFile_ReplacesLocalCopyAndKeepsBothAsBackups()
    {
        var localPath = Path.Combine(harness.DragonwildsRoot, "TEST_WORLD.sav");
        var localBytes = Save("TEST_WORLD", 30);
        File.WriteAllBytes(localPath, localBytes);
        var incomingBytes = Save("TEST_WORLD", 40);
        var candidate = (await harness.DragonwildsAdapter.ReadImportCandidateAsync(
            WriteOutside("TEST_WORLD (1).sav", incomingBytes), TestContext.Current.CancellationToken))!;
        var root = (await harness.DragonwildsAdapter.DiscoverSaveRootsAsync(TestContext.Current.CancellationToken))[0];

        var snapshot = await harness.Backups.CreateImportSnapshotAsync(candidate, TestContext.Current.CancellationToken);
        var imported = await harness.Backups.ImportBackupAsNewWorldAsync(snapshot.FilePath, root, overwriteExisting: true, TestContext.Current.CancellationToken);

        Assert.Equal(localPath, imported);
        Assert.Equal(incomingBytes, File.ReadAllBytes(localPath));
        var reasons = (await harness.Backups.ListBackupsAsync(GameId.Dragonwilds, TestContext.Current.CancellationToken))
            .Select(backup => backup.FileName).ToArray();
        Assert.Contains(reasons, name => name.Contains(BackupReasons.Imported, StringComparison.Ordinal));
        Assert.Contains(reasons, name => name.Contains(BackupReasons.PreImport, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportSnapshot_FileSwappedAfterPreview_IsRejected()
    {
        var path = WriteOutside("TEST_WORLD.sav", Save("TEST_WORLD", 40));
        var candidate = (await harness.DragonwildsAdapter.ReadImportCandidateAsync(path, TestContext.Current.CancellationToken))!;
        File.WriteAllBytes(path, Save("TEST_WORLD", 40, guid: 99999));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            harness.Backups.CreateImportSnapshotAsync(candidate, TestContext.Current.CancellationToken));
        Assert.Empty(await harness.Backups.ListBackupsAsync(GameId.Dragonwilds, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Inspect_ListsEveryFieldButNeverPrivateValues()
    {
        var path = Path.Combine(harness.DragonwildsRoot, "TEST_WORLD.sav");
        File.WriteAllBytes(path, Save("TEST_WORLD", 40));
        File.WriteAllBytes(path + ".backup", Save("TEST_WORLD", 39));
        var world = await harness.ReadDragonwildsWorldAsync(path);

        var sections = await harness.DragonwildsAdapter.InspectWorldAsync(world, TestContext.Current.CancellationToken);

        var items = sections.SelectMany(section => section.Items).ToArray();
        Assert.Contains(items, item => item.Label == "SessionPasswd" && item.Value.StartsWith("Private", StringComparison.Ordinal));
        Assert.Contains(items, item => item.Label == "TEST_UNKNOWN_FIELD" && item.Value == "Not decoded (4 bytes)");
        Assert.Contains(items, item => item.Label == "Sections" && item.Value.StartsWith("INFO", StringComparison.Ordinal));
        Assert.Contains(sections, section => section.Title == "Game's own backup copy");
        Assert.DoesNotContain(items, item => item.Value.Contains("TEST_SECRET_VALUE") || item.Value.Contains("TEST_OWNER_ID"));
        Assert.Equal(path + ".backup", harness.DragonwildsAdapter.GetGameBackupCopyPath(world));
    }
}
