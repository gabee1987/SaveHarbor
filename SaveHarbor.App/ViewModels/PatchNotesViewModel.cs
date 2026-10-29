using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

// The Patch notes window: every version, newest first, grouped by the month it came out, with the selected version's
// notes on the right. Opens on the version that is running.
public sealed partial class PatchNotesViewModel : ObservableObject
{
    [ObservableProperty]
    private PatchNotesEntry? _selectedRelease;

    public PatchNotesViewModel(IReadOnlyList<ChangelogRelease> releases, string currentVersion)
    {
        Releases = releases
            .Select((release, index) => new PatchNotesEntry(release, IsNewest: index == 0, IsInstalled: release.Version == currentVersion))
            .ToArray();
        Subtitle = Releases.LastOrDefault(entry => entry.Release.ReleasedOn is not null)?.Release.ReleasedOn is { } first
            ? $"You have v{currentVersion}. Every version since {first.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}, newest first."
            : $"You have v{currentVersion}.";
        SelectedRelease = Releases.FirstOrDefault(entry => entry.IsInstalled) ?? Releases.FirstOrDefault();
    }

    public IReadOnlyList<PatchNotesEntry> Releases { get; }

    public string Subtitle { get; }

    public bool HasReleases => Releases.Count > 0;

    // After an update: open when this version is newer than the one whose notes were shown last. Without a record, only
    // a PC that ran SaveHarbor before (2.3.0 kept none) gets them; a fresh install starts quietly.
    public static bool ShouldOpenAfterStart(string shownVersion, bool usedBefore, string currentVersion)
    {
        if (!Version.TryParse(currentVersion, out var current))
        {
            return false;
        }

        return Version.TryParse(shownVersion, out var shown) ? current > shown : string.IsNullOrEmpty(shownVersion) && usedBefore;
    }
}

public sealed record PatchNotesEntry(ChangelogRelease Release, bool IsNewest, bool IsInstalled)
{
    private const string NotReleased = "Not released yet";

    public string Version => Release.Version;

    public IReadOnlyList<ChangelogSection> Sections => Release.Sections;

    public string MonthTitle => Release.ReleasedOn?.ToString("MMMM yyyy", CultureInfo.InvariantCulture) ?? NotReleased;

    public string DateText => Release.ReleasedOn?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) ?? NotReleased;

    public string ShortDateText => Release.ReleasedOn?.ToString("d MMM", CultureInfo.InvariantCulture) ?? NotReleased;

    // "3 added · 1 fixed": the number of entries in the Added, Changed and Fixed sections.
    public string SummaryText
    {
        get
        {
            var counts = Sections
                .Where(section => section.Kind != ChangelogSectionKind.Other)
                .GroupBy(section => section.Kind)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Sum(section => section.Blocks.Count(block => block.Kind == MarkdownBlockKind.Bullet))} {group.Key.ToString().ToLowerInvariant()}");
            return string.Join(" · ", counts);
        }
    }
}
