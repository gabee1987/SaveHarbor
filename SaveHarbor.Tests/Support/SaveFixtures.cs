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
}
