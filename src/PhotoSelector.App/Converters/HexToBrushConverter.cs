using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PhotoSelector.App.Converters;

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var input = value?.ToString();
        if (string.IsNullOrWhiteSpace(input))
        {
            return System.Windows.Media.Brushes.Transparent;
        }

        try
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(input)!;
        }
        catch
        {
            return System.Windows.Media.Brushes.Transparent;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return System.Windows.Data.Binding.DoNothing;
    }
}
