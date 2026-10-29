using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SaveHarbor.App.Views.Converters;

// Picks the shell template of the active game: the template keyed "<GameId>ShellTemplate" in Templates. A game without
// one fails loudly instead of silently showing another game's shell. A converter rather than a DataTemplateSelector,
// because the window's content (the view model) stays the same when the game changes, and a selector would not re-run.
public sealed class GameShellTemplateConverter : IValueConverter
{
    public ResourceDictionary Templates { get; set; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Templates[$"{value}ShellTemplate"] as DataTemplate
        ?? throw new InvalidOperationException($"No shell template is registered for the game '{value}'.");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
