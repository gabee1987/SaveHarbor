using System.Windows;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class ThemeService : IThemeService
{
    private const int ColorsDictionaryIndex = 0;
    private static readonly Uri HighContrastDictionary = new("/Resources/Themes/HighContrast.Colors.xaml", UriKind.Relative);

    public ThemeService(IActiveGameContext activeGame)
    {
        activeGame.ActiveGameChanged += (_, game) => Apply(game);
        Apply(activeGame.Current);
    }

    public void Apply(IGameDefinition game)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var colors = SystemParameters.HighContrast ? HighContrastDictionary : game.ThemeDictionary;
        dictionaries[ColorsDictionaryIndex] = new ResourceDictionary { Source = colors };
    }
}
