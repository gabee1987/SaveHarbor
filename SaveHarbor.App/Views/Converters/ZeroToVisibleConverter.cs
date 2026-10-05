using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SaveHarbor.App.Views.Converters;

// Shows an empty-state hint only when a count is zero.
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public static ZeroToVisibleConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
