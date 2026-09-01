using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PhotoSort.Models;

namespace PhotoSort.Converters;

/// <summary>Colour used for the category badge and the highlighted category button.</summary>
public sealed class CategoryToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Edit = new(Color.Parse("#2D7DD2"));
    private static readonly SolidColorBrush Archive = new(Color.Parse("#4C9F70"));
    private static readonly SolidColorBrush Delete = new(Color.Parse("#D64550"));
    private static readonly SolidColorBrush None = new(Color.Parse("#3A3F46"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PhotoCategory.Edit => Edit,
        PhotoCategory.Archive => Archive,
        PhotoCategory.Delete => Delete,
        _ => None
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
