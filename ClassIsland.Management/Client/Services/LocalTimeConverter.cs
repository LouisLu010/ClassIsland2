using System.Globalization;
using Avalonia.Data.Converters;

namespace ClassIsland.Management.Client.Services;

public sealed class LocalTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset timestamp ? timestamp.ToLocalTime().ToString("MM-dd HH:mm:ss", culture) : value;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
