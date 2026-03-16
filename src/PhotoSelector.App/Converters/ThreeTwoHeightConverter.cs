using System.Globalization;
using System.Windows.Data;

namespace PhotoSelector.App.Converters;

public sealed class ThreeTwoHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double width && width > 0)
        {
            return width * 2.0 / 3.0;
        }

        return System.Windows.Data.Binding.DoNothing;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
