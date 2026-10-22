using System.Globalization;
using System.Windows.Data;

namespace SaveHarbor.App.Views.Converters;

// True when the bound number is at least the parameter, for step indicators: Tag="{Binding StepNumber, Converter=…, ConverterParameter=2}".
public sealed class AtLeastConverter : IValueConverter
{
    public static AtLeastConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int number && int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minimum) && number >= minimum;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
