using System.Globalization;
using System.Windows.Data;
using SaveHarbor.App.Localization;

namespace SaveHarbor.App.Views.Converters;

// Bound headings in the game's tracked capitals style (see SpacedTextExtension).
public sealed class SpacedCapsConverter : IValueConverter
{
    public static SpacedCapsConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? SpacedTextExtension.Apply(text.ToUpperInvariant()) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
