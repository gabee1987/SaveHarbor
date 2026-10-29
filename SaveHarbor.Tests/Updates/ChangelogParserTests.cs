using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Utilities;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.Tests.Updates;

// The Patch notes window reads CHANGELOG.md, embedded in the app: releases, dates, sections and their entries.
public sealed class ChangelogParserTests
{
    private const string Changelog =
        """
        # Changelog

        Intro text that belongs to no release.

        ## [Unreleased]

        - Not shipped yet

        ## [1.1.0] - 2026-02-03

        Install it **once** by hand.

        ### Added

        - **Groups:** invite friends
        - Shortcuts

        ### Fixed

        - Locks expire

        ### Security

        - Placeholder note

        ## [1.0.1]

        ## [1.0.0] - 2026-01-15

        ### Changed

        - First version

        [1.1.0]: https://example.invalid/compare
        """;

    private static string TextOf(MarkdownBlock block) => string.Concat(block.Spans.Select(span => span.Text));

    [Fact]
    public void Parse_ReadsReleasesNewestFirst_WithoutUnreleasedOrEmptyOnes()
    {
        var releases = ChangelogParser.Parse(Changelog.Replace("\n", "\r\n"));

        Assert.Equal(["1.1.0", "1.0.0"], releases.Select(release => release.Version));
        Assert.Equal(new DateOnly(2026, 2, 3), releases[0].ReleasedOn);
        Assert.Equal(new DateOnly(2026, 1, 15), releases[1].ReleasedOn);
    }

    [Fact]
    public void Parse_ReadsSectionsWithTheirKindAndEntries()
    {
        var release = ChangelogParser.Parse(Changelog)[0];

        Assert.Equal(
            [("", ChangelogSectionKind.Other), ("Added", ChangelogSectionKind.Added), ("Fixed", ChangelogSectionKind.Fixed), ("Security", ChangelogSectionKind.Other)],
            release.Sections.Select(section => (section.Title, section.Kind)));
        Assert.Equal(["Groups: invite friends", "Shortcuts"], release.Sections[1].Blocks.Select(TextOf));
        Assert.True(release.Sections[1].Blocks[0].Spans[0].IsBold);
        Assert.Equal(MarkdownBlockKind.Paragraph, release.Sections[0].Blocks[0].Kind);
    }

    [Fact]
    public void Parse_LeavesOutLinkReferences()
    {
        var release = ChangelogParser.Parse(Changelog)[1];

        Assert.Equal(["First version"], release.Sections.Single().Blocks.Select(TextOf));
    }

    [Fact]
    public void Parse_ReleaseWithoutDate_HasNoDate()
    {
        var release = Assert.Single(ChangelogParser.Parse("## [2.0.0]\n\n### Fixed\n- TEST_ENTRY\n"));

        Assert.Null(release.ReleasedOn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# Changelog\n\n## [Unreleased]\n- Soon\n")]
    [InlineData("## Version 2\n- Not a release heading\n")]
    public void Parse_NoReleases_IsEmpty(string text) => Assert.Empty(ChangelogParser.Parse(text));

    [Fact]
    public void EmbeddedChangelog_StartsWithThisBuildsVersion_AndEveryReleaseIsDated()
    {
        var releases = PatchNotesCatalog.Releases;
        var version = typeof(PatchNotesCatalog).Assembly.GetName().Version!.ToString(3);

        Assert.True(releases.Count >= 2, "CHANGELOG.md is not embedded in the app.");
        Assert.Equal(version, releases[0].Version);
        Assert.All(releases, release => Assert.NotNull(release.ReleasedOn));
        Assert.Equal(releases.OrderByDescending(release => Version.Parse(release.Version)).Select(release => release.Version), releases.Select(release => release.Version));
    }

    [Fact]
    public void ViewModel_OpensOnTheInstalledVersion_AndCountsEntries()
    {
        var viewModel = new PatchNotesViewModel(ChangelogParser.Parse(Changelog), "1.0.0");

        Assert.Equal("1.0.0", viewModel.SelectedRelease?.Version);
        Assert.True(viewModel.Releases[0].IsNewest);
        Assert.False(viewModel.Releases[0].IsInstalled);
        Assert.True(viewModel.Releases[1].IsInstalled);
        Assert.Equal("2 added · 1 fixed", viewModel.Releases[0].SummaryText);
        Assert.Equal("February 2026", viewModel.Releases[0].MonthTitle);
        Assert.Equal("3 February 2026", viewModel.Releases[0].DateText);
        Assert.Contains("January 2026", viewModel.Subtitle);
    }

    [Fact]
    public void ViewModel_UnknownVersion_OpensOnTheNewest()
    {
        var viewModel = new PatchNotesViewModel(ChangelogParser.Parse(Changelog), "9.9.9");

        Assert.Equal("1.1.0", viewModel.SelectedRelease?.Version);
        Assert.DoesNotContain(viewModel.Releases, entry => entry.IsInstalled);
    }

    [Theory]
    [InlineData("2.3.0", false, "2.4.0", true)]
    [InlineData("2.4.0", true, "2.4.0", false)]
    [InlineData("2.5.0", true, "2.4.0", false)]
    [InlineData("", true, "2.4.0", true)]
    [InlineData("", false, "2.4.0", false)]
    [InlineData("TEST_VALUE", true, "2.4.0", false)]
    [InlineData("2.3.0", true, "", false)]
    public void ShouldOpenAfterStart_OnlyOnceAfterAnUpdate(string shown, bool usedBefore, string current, bool expected) =>
        Assert.Equal(expected, PatchNotesViewModel.ShouldOpenAfterStart(shown, usedBefore, current));

    [Fact]
    public void ViewModel_NoReleases_SelectsNothing()
    {
        var viewModel = new PatchNotesViewModel([], "1.0.0");

        Assert.Null(viewModel.SelectedRelease);
        Assert.False(viewModel.HasReleases);
    }
}
