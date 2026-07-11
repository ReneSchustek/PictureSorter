using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using PictureSorter.App.ViewModels;

namespace PictureSorter.App.Converters;

/// <summary>
/// Bildet den UI-freien <see cref="StatusSeverity"/> der Application-Schicht auf
/// die WinUI-<see cref="InfoBarSeverity"/> der Statusleiste ab.
/// </summary>
internal sealed partial class StatusSeverityToInfoBarSeverityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is StatusSeverity severity
            ? severity switch
            {
                StatusSeverity.Success => InfoBarSeverity.Success,
                StatusSeverity.Warning => InfoBarSeverity.Warning,
                StatusSeverity.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational,
            }
            : InfoBarSeverity.Informational;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
