using System.Xml.Linq;

namespace SaveHarbor.Tests.Themes;

public sealed class ThemeDictionaryTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] RequiredKeys =
    [
        "AppBackgroundBrush", "PanelBrush", "PanelAltBrush", "InputBrush", "LineBrush", "LineSoftBrush",
        "InkBrush", "MutedBrush", "SubtleBrush", "AccentBrush", "AccentHoverBrush", "AccentPressedBrush",
        "DangerBrush", "WarnBrush", "SuccessBrush", "InfoBrush", "OrnamentBrush", "PrimaryButtonForegroundBrush",
        "PlayCardBackgroundBrush", "ToneAccentBackgroundBrush", "ToneAccentBorderBrush", "ToneInfoBackgroundBrush",
        "ToneInfoBorderBrush", "ToneSuccessBackgroundBrush", "ToneSuccessBorderBrush", "ToneWarnBackgroundBrush",
        "ToneWarnBorderBrush", "ToneNeutralBackgroundBrush", "HoverSurfaceBrush", "HoverLineBrush",
        "PressedSurfaceBrush", "InputHoverBrush", "ItemHoverBrush", "ItemSelectedBrush",
        "PlayCardBorderBrush", "HeaderDividerBrush", "PlayGlowColor", "PlayGlowOpacity",
        "AppDisplayFontFamily", "ShowOrnamentGlyphs", "GameIconGeometry"
    ];

    private static HashSet<string> LoadKeys(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Themes", fileName);
        return XDocument.Load(path).Descendants()
            .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Windrose.Colors.xaml")]
    [InlineData("Dragonwilds.Colors.xaml")]
    [InlineData("HighContrast.Colors.xaml")]
    public void ThemeDictionary_DefinesEveryContractKey(string fileName)
    {
        var keys = LoadKeys(fileName);

        foreach (var key in RequiredKeys)
        {
            Assert.Contains(key, keys);
        }
    }

    [Theory]
    [InlineData("Dragonwilds.Colors.xaml")]
    [InlineData("HighContrast.Colors.xaml")]
    public void ThemeDictionary_HasSameKeySetAsWindrose(string fileName)
    {
        Assert.True(LoadKeys("Windrose.Colors.xaml").SetEquals(LoadKeys(fileName)));
    }
}
