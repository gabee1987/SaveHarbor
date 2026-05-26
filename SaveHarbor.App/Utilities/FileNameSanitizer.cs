using System.IO;

namespace SaveHarbor.App.Utilities;

public static class FileNameSanitizer
{
    public static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", value.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
