using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace PictureSorter.App.Converters;

/// <summary>
/// Bildet „bereit / nicht bereit" auf die Schwere einer <see cref="InfoBar"/> ab:
/// bereit wird grün, nicht bereit wird als Warnung dargestellt.
/// </summary>
internal sealed partial class BoolToInfoBarSeverityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? InfoBarSeverity.Success : InfoBarSeverity.Warning;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
