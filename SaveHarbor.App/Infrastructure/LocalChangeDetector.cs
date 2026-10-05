using System.IO;
using System.Security.Cryptography;
using System.Text;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

// Tells whether a world's files changed since they last matched a cloud version (upload or download), e.g. because
// the game was played without SaveHarbor. A quick signature (names, sizes, write times) is checked first; the content
// hash decides when the signature differs, so merely touched files do not count as progress.
public static class LocalChangeDetector
{
    // States saved before fingerprints existed: a world written this long after the last upload or download counts
    // as changed. The margin covers the write that a download itself performs.
    private static readonly TimeSpan LegacyTolerance = TimeSpan.FromMinutes(2);

    public static async Task CaptureAsync(LocalSyncState state, GameWorld world, IGameSaveAdapter adapter, CancellationToken cancellationToken)
    {
        var files = PayloadFiles(world, adapter);
        state.LocalBaseSignature = files is null ? string.Empty : Signature(world, files);
        state.LocalBaseContentHash = files is null ? string.Empty : await ContentHashAsync(world, files, cancellationToken);
    }

    // Null when it cannot be told (the world is missing or no baseline exists).
    public static async Task<bool?> HasChangedAsync(LocalSyncState state, GameWorld world, IGameSaveAdapter adapter, CancellationToken cancellationToken)
    {
        var files = PayloadFiles(world, adapter);
        if (files is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(state.LocalBaseContentHash))
        {
            var lastSync = new[] { state.LastUploadedAtUtc, state.LastDownloadedAtUtc }.Max();
            return lastSync is null ? null : files.Max(File.GetLastWriteTimeUtc) > lastSync.Value.UtcDateTime + LegacyTolerance;
        }

        if (string.Equals(Signature(world, files), state.LocalBaseSignature, StringComparison.Ordinal))
        {
            return false;
        }

        return !string.Equals(await ContentHashAsync(world, files, cancellationToken), state.LocalBaseContentHash, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string>? PayloadFiles(GameWorld world, IGameSaveAdapter adapter)
    {
        if (!File.Exists(world.SavePath) && !Directory.Exists(world.SavePath))
        {
            return null;
        }

        var files = adapter.GetPayloadFiles(world).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return files.Length == 0 ? null : files;
    }

    private static string Signature(GameWorld world, IReadOnlyList<string> files)
    {
        var builder = new StringBuilder();
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            builder.Append(RelativeName(world, file)).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    // Read with sharing, so it also works while the game holds the save open.
    private static async Task<string> ContentHashAsync(GameWorld world, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(RelativeName(world, file) + "\n"));
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string RelativeName(GameWorld world, string file)
    {
        var root = File.Exists(world.SavePath) ? Path.GetDirectoryName(world.SavePath)! : world.SavePath;
        return Path.GetRelativePath(root, file).Replace('\\', '/');
    }
}
