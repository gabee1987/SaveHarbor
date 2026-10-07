using System.IO.Compression;
using System.Text.Json;
using SaveHarbor.App.Domain;

namespace SaveHarbor.Tests.Support;

public static class ZipManifestEditor
{
    private const string ManifestName = "saveharbor-manifest.json";

    public static void Update(string zipPath, Action<BackupManifest> edit)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var entry = archive.GetEntry(ManifestName)!;
        BackupManifest manifest;
        using (var stream = entry.Open())
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(stream)!;
        }

        entry.Delete();
        edit(manifest);
        var replacement = archive.CreateEntry(ManifestName);
        using var writer = new StreamWriter(replacement.Open());
        writer.Write(JsonSerializer.Serialize(manifest));
    }

    public static string BuildLegacyDirectoryBackup(string zipPath, string worldId, string fileName, string content)
    {
        var staging = Path.Combine(Path.GetDirectoryName(zipPath)!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staging, "world"));
        File.WriteAllText(Path.Combine(staging, "world", fileName), content);
        File.WriteAllText(
            Path.Combine(staging, "world", "WorldDescription.json"),
            $$$"""{"Version":1,"WorldDescription":{"islandId":"{{{worldId}}}","WorldName":"Test World"}}""");
        File.WriteAllText(
            Path.Combine(staging, ManifestName),
            $$$"""{"SchemaVersion":1,"Game":"windrose","WorldId":"{{{worldId}}}","WorldName":"Test World","FileCount":1}""");
        ZipFile.CreateFromDirectory(staging, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        Directory.Delete(staging, recursive: true);
        return zipPath;
    }
}
