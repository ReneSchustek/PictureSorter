using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace PictureSorter.App.Converters;

/// <summary>
/// Wandelt einen <see cref="bool"/> in eine <see cref="Visibility"/> um:
/// <see langword="true"/> ergibt <see cref="Visibility.Visible"/>, sonst
/// <see cref="Visibility.Collapsed"/>.
/// </summary>
internal sealed partial class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}
