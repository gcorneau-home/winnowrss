using System.Globalization;
using System.Windows.Data;

namespace Winnow.App.Converters;

/// <summary>Subtracts the converter parameter from a double, never going below zero.</summary>
public sealed class SubtractConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Math.Max(0, (double)value - double.Parse((string)parameter, CultureInfo.InvariantCulture));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
