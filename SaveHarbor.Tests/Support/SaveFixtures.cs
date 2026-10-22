using System.IO.Compression;

namespace SaveHarbor.Tests.Support;

public static class SaveFixtures
{
    public static string CreateWindroseWorld(
        string profilesRoot,
        string profileId,
        string worldFolder,
        string islandId,
        string worldName,
        string rocksDbRoot = "RocksDB_v2")
    {
        var worldPath = Path.Combine(profilesRoot, profileId, rocksDbRoot, "0.10.0", "Worlds", worldFolder);
        Directory.CreateDirectory(worldPath);

        File.WriteAllText(
            Path.Combine(worldPath, "WorldDescription.json"),
            $$$"""{"Version":1,"WorldDescription":{"islandId":"{{{islandId}}}","WorldName":"{{{worldName}}}","CreationTime":0,"WorldPresetType":"Medium"}}""");
        File.WriteAllText(Path.Combine(worldPath, "000001.sst"), "dummy");
        File.WriteAllText(Path.Combine(worldPath, "CURRENT"), "MANIFEST-000001");
        File.WriteAllText(Path.Combine(worldPath, "MANIFEST-000001"), "dummy");
        File.WriteAllText(Path.Combine(worldPath, "LOCK"), string.Empty);

        return worldPath;
    }

    // Writes the game's own checkpoint archive of a world, in the layout Windrose uses, matching the world folder's
    // tables and manifest. extraEntry adds one more entry (for tests of untrusted archives).
    public static string CreateWindroseGameArchive(string worldPath, string islandId, string? extraEntry = null)
    {
        var profile = Directory.GetParent(worldPath)!.Parent!.Parent!.Parent!.FullName;
        var folder = Path.Combine(profile, "RocksDB_v2_Backups", "Worlds", Path.GetFileName(worldPath));
        Directory.CreateDirectory(folder);
        var archivePath = Path.Combine(folder, $"{Path.GetFileName(worldPath)}_0.10.0_Latest.zip");

        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }

        Add("Checkpoint/meta/1", "TEST_META");
        foreach (var file in Directory.GetFiles(worldPath))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("MANIFEST-", StringComparison.Ordinal) || name == "CURRENT")
            {
                Add($"Checkpoint/private/1/{name}", "TEST_PRIVATE");
            }
            else if (name.EndsWith(".sst", StringComparison.Ordinal))
            {
                Add($"Checkpoint/shared_checksum/{Path.GetFileNameWithoutExtension(name)}_sTEST_1.sst", "TEST_TABLE");
            }
        }

        Add("AdditionalRecordFiles/WorldDescription.json", $$$"""{"Version":1,"WorldDescription":{"islandId":"{{{islandId}}}","WorldName":"TEST_WORLD"}}""");
        if (extraEntry is not null)
        {
            Add(extraEntry, "TEST_EXTRA");
        }

        return archivePath;
    }
}
