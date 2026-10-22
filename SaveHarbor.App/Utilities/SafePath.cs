using System.IO;

namespace SaveHarbor.App.Utilities;

public static class SafePath
{
    private static readonly string[] ReservedDeviceNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    public static bool IsSafeSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
        {
            return false;
        }

        if (segment.EndsWith('.') || segment.EndsWith(' ') || segment.StartsWith(' '))
        {
            return false;
        }

        if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(segment);
        return !ReservedDeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    public static string CombineUnderRoot(string root, string untrustedSegment)
    {
        if (!IsSafeSegment(untrustedSegment))
        {
            throw new InvalidDataException("A name received from shared data is not a valid file name.");
        }

        var rootFull = Path.GetFullPath(root);
        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(rootFull, untrustedSegment));

        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A name received from shared data resolves outside its folder.");
        }

        return combined;
    }
}
