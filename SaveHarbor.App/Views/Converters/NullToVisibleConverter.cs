using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SaveHarbor.App.Views.Converters;

public sealed class NullToVisibleConverter : IValueConverter
{
    public static NullToVisibleConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
