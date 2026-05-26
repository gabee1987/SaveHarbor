using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SaveHarbor.App.Utilities;

public static class DirectoryHashCalculator
{
    public static string ComputeSha256(string root)
    {
        using var sha = SHA256.Create();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(file => Path.GetRelativePath(root, file), StringComparer.OrdinalIgnoreCase))
        {
            var relativePathBytes = Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/'));
            sha.TransformBlock(relativePathBytes, 0, relativePathBytes.Length, null, 0);
            var bytes = File.ReadAllBytes(file);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash ?? []);
    }
}
