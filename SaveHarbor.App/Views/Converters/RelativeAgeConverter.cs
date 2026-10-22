using System.Globalization;
using System.Windows.Data;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Views.Converters;

public sealed class RelativeAgeConverter : IValueConverter
{
    public static RelativeAgeConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset timestamp ? DisplayFormatter.FormatAge(timestamp) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
