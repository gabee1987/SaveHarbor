using System.Text.Json;
using System.Text.RegularExpressions;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.Tests.Localization;

// A key missing from ui-text.en.json shows up in the app as the raw key, so every key the app asks for must exist.
public sealed partial class UiTextKeysTests
{
    private static readonly string AppFolder = Path.Combine(FindRepositoryRoot(), "SaveHarbor.App");

    [GeneratedRegex(@"\{ui:(?:UiText|SpacedText)\s+(?:Key=)?([A-Za-z0-9_.]+)\s*\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"UiTextCatalog\.Get\(""([A-Za-z0-9_.]+)""\)")]
    private static partial Regex CodeKey();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SaveHarbor.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }

    private static Dictionary<string, string> Texts() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppFolder, "Resources", "Texts", "ui-text.en.json")))!;

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(AppFolder, pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void EveryKeyInXaml_Exists()
    {
        var texts = Texts();
        var missing = SourceFiles("*.xaml")
            .SelectMany(file => XamlKey().Matches(File.ReadAllText(file)).Select(match => $"{Path.GetFileName(file)}: {match.Groups[1].Value}"))
            .Where(entry => !texts.ContainsKey(entry[(entry.IndexOf(": ", StringComparison.Ordinal) + 2)..]))
            .Distinct()
            .Order()
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryKeyInCode_Exists()
    {
        var texts = Texts();
        var missing = SourceFiles("*.cs")
            .SelectMany(file => CodeKey().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .Where(key => !texts.ContainsKey(key))
            .Distinct()
            .Order()
            .ToArray();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData(CloudProviderKind.Supabase)]
    [InlineData(CloudProviderKind.GoogleDrive)]
    public void EveryChoosableProvider_HasItsCardTexts(string kind)
    {
        var texts = Texts();

        Assert.True(texts.ContainsKey($"CloudProviderChoice.{kind}Title"));
        Assert.True(texts.ContainsKey($"CloudProviderChoice.{kind}Description"));
    }

    [Fact]
    public void UserTexts_NameSupabaseOnlyInTheSettingsDescription()
    {
        var mentions = Texts()
            .Where(entry => entry.Value.Contains("Supabase", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Key)
            .ToArray();

        Assert.Equal(["Settings.ProviderHint"], mentions);
    }

    [Fact]
    public void Texts_HaveNoExclamationMarks()
    {
        Assert.DoesNotContain(Texts(), entry => entry.Key.StartsWith("CloudGroup.", StringComparison.Ordinal) && entry.Value.Contains('!'));
    }
}
