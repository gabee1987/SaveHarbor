using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SaveHarbor.App.Views.Converters;

// Hides a section while its list is empty.
public sealed class ZeroToCollapsedConverter : IValueConverter
{
    public static ZeroToCollapsedConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
