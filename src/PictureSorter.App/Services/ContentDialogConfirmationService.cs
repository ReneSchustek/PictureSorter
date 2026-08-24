using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureSorter.Application.Services;

namespace PictureSorter.App.Services;

/// <summary>
/// Holt Bestätigungen über einen <see cref="ContentDialog"/> ein. Wird vom
/// Hauptfenster über dessen <see cref="XamlRoot"/> verankert.
/// </summary>
internal sealed class ContentDialogConfirmationService : IConfirmationService
{
    private readonly WindowContext _windowContext;
    private readonly ThemeService _themeService;

    /// <summary>
    /// Initialisiert den Dialogdienst.
    /// </summary>
    /// <param name="windowContext">Der Fensterkontext.</param>
    /// <param name="themeService">Liefert das Design, das der Dialog tragen soll.</param>
    public ContentDialogConfirmationService(WindowContext windowContext, ThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(windowContext);
        ArgumentNullException.ThrowIfNull(themeService);
        _windowContext = windowContext;
        _themeService = themeService;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        XamlRoot? xamlRoot = _windowContext.MainWindow?.Content?.XamlRoot;
        if (xamlRoot is null)
        {
            return false;
        }

        ContentDialog dialog = new()
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        // Der Dialog liegt über dem Fenster, nicht darin, und erbt dessen Design nicht.
        _themeService.ApplyTo(dialog);

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
