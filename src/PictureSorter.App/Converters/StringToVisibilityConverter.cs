using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace PictureSorter.App.Converters;

/// <summary>
/// Blendet ein Element ein, solange sein Text nicht leer ist.
///
/// Für Hinweise, die nur zeitweise etwas zu sagen haben: Ohne den Wandler bliebe an ihrer
/// Stelle eine leere Zeile stehen und die Karte sähe kaputt aus.
/// </summary>
internal sealed partial class StringToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
