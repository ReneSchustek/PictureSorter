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

    /// <summary>
    /// Initialisiert den Dialogdienst.
    /// </summary>
    /// <param name="windowContext">Der Fensterkontext.</param>
    public ContentDialogConfirmationService(WindowContext windowContext)
    {
        ArgumentNullException.ThrowIfNull(windowContext);
        _windowContext = windowContext;
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

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
