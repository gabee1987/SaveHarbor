using System.Globalization;
using System.Windows.Data;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.App.Views.Converters;

// Looks up a world's sync badge: values are the world id and the id-to-badge map.
public sealed class WorldSyncBadgeConverter : IMultiValueConverter
{
    public static WorldSyncBadgeConverter Instance { get; } = new();

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [string worldId, IReadOnlyDictionary<string, SyncBadge> badges] ? badges.GetValueOrDefault(worldId) : null;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
