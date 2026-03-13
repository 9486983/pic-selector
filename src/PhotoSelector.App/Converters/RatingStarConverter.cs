using System.Globalization;
using System.Windows.Data;

namespace PhotoSelector.App.Converters;

public sealed class RatingStarConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!int.TryParse(value?.ToString(), out var rating))
        {
            rating = 0;
        }

        if (!int.TryParse(parameter?.ToString(), out var starIndex))
        {
            starIndex = 1;
        }

        return rating >= starIndex ? "★" : "☆";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return System.Windows.Data.Binding.DoNothing;
    }
}
