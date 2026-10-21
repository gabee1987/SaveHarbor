using System.Diagnostics;
using System.Text.RegularExpressions;
using SaveHarbor.App.Utilities;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.ReleasePipeline;

// The release workflow takes the notes of a tag from CHANGELOG.md with build/Get-ReleaseNotes.ps1. A missing section
// must stop the release, and the notes must stay inside what the release notes dialog can show.
public sealed partial class ReleaseNotesScriptTests : IDisposable
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string Script = Path.Combine(RepositoryRoot, "build", "Get-ReleaseNotes.ps1");
    private static readonly string Changelog = Path.Combine(RepositoryRoot, "CHANGELOG.md");

    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    [GeneratedRegex(@"^##\s+\[(\d+\.\d+\.\d+)\]", RegexOptions.Multiline)]
    private static partial Regex VersionHeading();

    [GeneratedRegex(@"\[[^\]]+\]\([^)]+\)")]
    private static partial Regex MarkdownLink();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SaveHarbor.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }

    private (int ExitCode, string Output, string Notes) Run(string version, string? changelog = null)
    {
        var output = temp.Combine("notes.md");
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", Script, "-Version", version, "-Out", output })
        {
            start.ArgumentList.Add(argument);
        }

        if (changelog is not null)
        {
            start.ArgumentList.Add("-Changelog");
            start.ArgumentList.Add(changelog);
        }

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Skip("PowerShell 7 (pwsh) is not installed.");
            throw;
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            process.WaitForExit();
            var notes = File.Exists(output) ? File.ReadAllText(output) : string.Empty;
            return (process.ExitCode, stdout.Result + stderr.Result, notes);
        }
    }

    private string WriteChangelog(string content, string name = "CHANGELOG.md")
    {
        var path = temp.Combine(name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Section_IsCutAtTheNextHeadingAndTheLinkReferences()
    {
        var changelog = WriteChangelog(
            "# Changelog\n\n## [Unreleased]\n\n## [2.4.0] - 2026-12-01\n\n### Fixed\n- **Locks** expire\n\n## [2.3.0]\n- Old\n");
        var other = WriteChangelog("## [2.4.0]\n- New\n\n[2.4.0]: https://example.invalid/compare\n", "links.md");

        var cut = Run("2.4.0", changelog);
        var linked = Run("2.4.0", other);

        Assert.Equal(0, cut.ExitCode);
        Assert.Equal("### Fixed\n- **Locks** expire\n", cut.Notes);
        Assert.Equal("- New\n", linked.Notes);
    }

    [Fact]
    public void MissingSection_FailsAndWritesNothing()
    {
        var changelog = WriteChangelog("# Changelog\n\n## [2.3.0]\n- Old\n");

        var result = Run("2.3.1", changelog);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("no '## [2.3.1]' section", result.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Notes);
    }

    [Fact]
    public void EmptySection_Fails()
    {
        var changelog = WriteChangelog("## [2.4.0]\n\n## [2.3.0]\n- Old\n");

        var result = Run("2.4.0", changelog);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("is empty", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionDots_AreNotWildcards()
    {
        var changelog = WriteChangelog("## [2x3x0]\n- Wrong\n");

        Assert.NotEqual(0, Run("2.3.0", changelog).ExitCode);
    }

    [Fact]
    public void FirstInstallerRelease_TellsFriendsToInstallOnce()
    {
        var result = Run("2.3.0");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("SaveHarborApp-win-Setup.exe", result.Notes, StringComparison.Ordinal);
        Assert.Contains(@"%LOCALAPPDATA%\SaveHarbor", result.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryReleasedSection_UsesOnlyWhatTheNotesDialogShows()
    {
        var text = File.ReadAllText(Changelog);
        var versions = VersionHeading().Matches(text).Select(match => match.Groups[1].Value).ToArray();
        Assert.NotEmpty(versions);

        foreach (var version in versions)
        {
            var notes = Run(version).Notes;
            Assert.DoesNotMatch(MarkdownLink(), notes);
            Assert.NotEmpty(MarkdownLite.Parse(notes));
        }
    }
}
