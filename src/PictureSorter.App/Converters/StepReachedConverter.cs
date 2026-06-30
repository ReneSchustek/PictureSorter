using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace PictureSorter.App.Converters;

/// <summary>
/// Liefert <see langword="true"/>, wenn der über <c>ConverterParameter</c>
/// übergebene Schritt-Index kleiner oder gleich dem gebundenen
/// „höchsten erreichten Schritt" ist. Dient dazu, in der Schrittleiste nur bereits
/// erreichte Schritte anklickbar zu machen.
/// </summary>
internal sealed partial class StepReachedConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int maxReached
            && parameter is string text
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            return index <= maxReached;
        }

        return false;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
