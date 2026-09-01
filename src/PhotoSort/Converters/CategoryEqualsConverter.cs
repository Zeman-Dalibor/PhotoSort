using System.Globalization;
using Avalonia.Data.Converters;
using PhotoSort.Models;

namespace PhotoSort.Converters;

/// <summary>True when the bound category matches the one named by the converter parameter.</summary>
public sealed class CategoryEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is PhotoCategory category &&
        parameter is string name &&
        Enum.TryParse<PhotoCategory>(name, ignoreCase: true, out var expected) &&
        category == expected;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
