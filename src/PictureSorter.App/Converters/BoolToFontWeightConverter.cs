using System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Data;

namespace PictureSorter.App.Converters;

/// <summary>
/// Wandelt einen <see cref="bool"/> in eine Schriftstärke um: <see langword="true"/>
/// ergibt halbfett (für den aktiven Schritt), sonst normal.
/// </summary>
internal sealed partial class BoolToFontWeightConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? FontWeights.SemiBold : FontWeights.Normal;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
