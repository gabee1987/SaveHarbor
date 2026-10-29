using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SaveHarbor.App.Utilities;

public enum ChangelogSectionKind
{
    Added,
    Changed,
    Fixed,
    Other
}

public sealed record ChangelogSection(string Title, ChangelogSectionKind Kind, IReadOnlyList<MarkdownBlock> Blocks);

public sealed record ChangelogRelease(string Version, DateOnly? ReleasedOn, IReadOnlyList<ChangelogSection> Sections);

// CHANGELOG.md in Keep a Changelog form: "## [x.y.z] - yyyy-MM-dd" starts a release, "### Title" a section, and the
// lines below are MarkdownLite text. "[Unreleased]", releases without notes and link references ("[x]: url") are left
// out. Releases keep the file's order, newest first.
public static partial class ChangelogParser
{
    public static IReadOnlyList<ChangelogRelease> Parse(string markdown)
    {
        var releases = new List<ChangelogRelease>();
        var sections = new List<ChangelogSection>();
        var body = new StringBuilder();
        string? version = null;
        DateOnly? releasedOn = null;
        var sectionTitle = string.Empty;

        void EndSection()
        {
            var blocks = MarkdownLite.Parse(body.ToString());
            if (blocks.Count > 0)
            {
                sections.Add(new ChangelogSection(sectionTitle, KindOf(sectionTitle), blocks));
            }

            sectionTitle = string.Empty;
            body.Clear();
        }

        void EndRelease()
        {
            EndSection();
            if (version is not null && sections.Count > 0)
            {
                releases.Add(new ChangelogRelease(version, releasedOn, sections.ToArray()));
            }

            version = null;
            releasedOn = null;
            sections.Clear();
        }

        foreach (var rawLine in (markdown ?? string.Empty).Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                EndRelease();
                var release = ReleaseHeading().Match(line);
                if (release.Success)
                {
                    version = release.Groups["version"].Value;
                    releasedOn = DateOnly.TryParseExact(release.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                        ? date
                        : null;
                }
            }
            else if (version is null || LinkReference().IsMatch(line))
            {
                continue;
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                EndSection();
                sectionTitle = line[4..].Trim();
            }
            else
            {
                body.Append(line).Append('\n');
            }
        }

        EndRelease();
        return releases;
    }

    private static ChangelogSectionKind KindOf(string title) =>
        Enum.TryParse<ChangelogSectionKind>(title, ignoreCase: true, out var kind) && kind != ChangelogSectionKind.Other
            ? kind
            : ChangelogSectionKind.Other;

    [GeneratedRegex(@"^## \[(?<version>[0-9]+\.[0-9]+\.[0-9]+)\](?:\s+-\s+(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2}))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseHeading();

    [GeneratedRegex(@"^\[[^\]]+\]:\s", RegexOptions.CultureInvariant)]
    private static partial Regex LinkReference();
}
