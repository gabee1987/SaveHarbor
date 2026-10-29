using System.IO;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Infrastructure;

// The patch notes are CHANGELOG.md from the repository root, embedded at build time (see the csproj), so they always
// match the version that is running and need no connection.
public static class PatchNotesCatalog
{
    public const string ResourceName = "SaveHarbor.App.CHANGELOG.md";

    private static readonly Lazy<IReadOnlyList<ChangelogRelease>> releases = new(Load);

    public static IReadOnlyList<ChangelogRelease> Releases => releases.Value;

    private static IReadOnlyList<ChangelogRelease> Load()
    {
        using var stream = typeof(PatchNotesCatalog).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        return ChangelogParser.Parse(reader.ReadToEnd());
    }
}
