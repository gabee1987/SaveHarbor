using System.IO;
using System.Security.Cryptography;

namespace SaveHarbor.App.Utilities;

public static class FileHashCalculator
{
    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
