using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Winnow.App.Converters;

/// <summary>Visible when the bound number is greater than zero; ConverterParameter="Invert" reverses it.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is int count && count > 0) != (parameter as string == "Invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
