using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SaveHarbor.App.Views.Converters;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public static InverseBooleanToVisibilityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
