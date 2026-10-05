using System.Globalization;
using System.Windows.Data;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Views.Converters;

public sealed class ByteSizeConverter : IValueConverter
{
    public static ByteSizeConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? DisplayFormatter.FormatBytes(bytes) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
