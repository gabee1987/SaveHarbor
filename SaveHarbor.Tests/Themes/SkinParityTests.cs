using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SaveHarbor.Tests.Themes;

// The shared main screen (Views/Shell) looks up Skin* keys dynamically: a key missing from one game's skin would
// silently render unstyled, so every key the screen uses must exist in every skin.
public sealed partial class SkinParityTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [GeneratedRegex(@"\bSkin[A-Z]\w*")]
    private static partial Regex SkinKey();

    private static HashSet<string> DefinedKeys(string skin) =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Themes", skin), "*.xaml")
            .SelectMany(file => XDocument.Load(file).Descendants())
            .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> UsedKeys() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ShellViews"))
            .SelectMany(file => SkinKey().Matches(File.ReadAllText(file)).Select(match => match.Value))
            .ToHashSet(StringComparer.Ordinal);

    [Theory]
    [InlineData("Dragonwilds")]
    [InlineData("Windrose")]
    public void Skin_DefinesEveryKeyTheSharedScreenUses(string skin)
    {
        var missing = UsedKeys().Except(DefinedKeys(skin)).Order().ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void Skins_ShareTheDragonwildsKeySet()
    {
        Assert.Empty(DefinedKeys("Dragonwilds").Except(DefinedKeys("Windrose")).Order().ToArray());
    }
}
